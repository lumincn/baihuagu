using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Baihua.Contracts.LocalModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baihua.Family.Services;

/// <summary>
/// OpenVINO LLM 运行管理器：把已下载的模型目录启动为 openvino_llm_server.py 子进程
/// （Intel Arc 核显 GPU 推理），支持停止/状态探测。
/// </summary>
public class OpenVinoRuntimeManager
{
    private readonly ILogger<OpenVinoRuntimeManager> _logger;
    private readonly LocalAiOptions _options;
    private readonly ConcurrentDictionary<int, RunningInstance> _running = new();
    private static readonly object PythonLock = new();
    private static string? _pythonCache;

    private sealed record RunningInstance(int Port, int ProcessId, string ModelPath, DateTime StartedAt);

    public OpenVinoRuntimeManager(ILogger<OpenVinoRuntimeManager> logger, IOptions<LocalAiOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>模型根目录（可通过 LocalAI:DownloadDirectory 配置）</summary>
    public string ModelRoot => _options.GetModelRoot();

    /// <summary>扫描已下载模型目录</summary>
    public List<OpenVinoInstalledModelDto> GetInstalledModels()
    {
        var result = new List<OpenVinoInstalledModelDto>();
        try
        {
            if (!Directory.Exists(ModelRoot)) return result;
            foreach (var dir in Directory.GetDirectories(ModelRoot))
            {
                var bin = Path.Combine(dir, "openvino_model.bin");
                if (!File.Exists(bin)) continue;
                var size = DirectorySize(dir);
                var name = Path.GetFileName(dir);
                var running = _running.Values.FirstOrDefault(r => Path.GetFullPath(r.ModelPath) == Path.GetFullPath(dir));
                result.Add(new OpenVinoInstalledModelDto
                {
                    Name = name,
                    Path = dir,
                    SizeBytes = size,
                    HasOpenVinoBin = true,
                    IsRunning = running != null,
                    Port = running?.Port,
                    LastModified = Directory.GetLastWriteTime(dir)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫描模型目录失败");
        }
        return result.OrderBy(m => m.Name).ToList();
    }

    /// <summary>启动模型（GPU 推理），等待就绪后返回端口</summary>
    public async Task<OpenVinoRunResult> StartAsync(string modelPath, string device, CancellationToken ct = default)
    {
        modelPath = modelPath.Trim().Trim('"');
        if (!File.Exists(Path.Combine(modelPath, "openvino_model.bin")))
            return new OpenVinoRunResult { Success = false, Error = $"目录中未找到 openvino_model.bin: {modelPath}" };

        // 已运行则直接返回
        var existing = _running.Values.FirstOrDefault(r => Path.GetFullPath(r.ModelPath) == Path.GetFullPath(modelPath));
        if (existing != null)
            return new OpenVinoRunResult { Success = true, Port = existing.Port, ProcessId = existing.ProcessId, Endpoint = $"http://127.0.0.1:{existing.Port}/v1" };

        var python = await FindPythonWithOpenVinoAsync(ct);
        if (python == null)
            return new OpenVinoRunResult { Success = false, Error = "未找到可用 Python（需要 pip install openvino-genai）" };

        var script = ResolveScriptPath();
        if (script == null)
            return new OpenVinoRunResult { Success = false, Error = "未找到 openvino_llm_server.py" };

        var port = await FindFreePortAsync(8000, 8030, ct);
        if (port == 0)
            return new OpenVinoRunResult { Success = false, Error = "8000-8029 端口均被占用" };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\" --model \"{modelPath}\" --device {device} --port {port} --max-context-size 4096",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            if (proc == null)
                return new OpenVinoRunResult { Success = false, Error = "进程启动失败" };

            // 等待 /v1/models 就绪（模型加载可能需要 1-3 分钟）
            var deadline = DateTime.UtcNow.AddMinutes(3);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (proc.HasExited)
                    return new OpenVinoRunResult { Success = false, Error = $"进程提前退出（exit={proc.ExitCode}），模型可能无效或设备不支持" };
                try
                {
                    var resp = await http.GetAsync($"http://127.0.0.1:{port}/v1/models", ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        _running[port] = new RunningInstance(port, proc.Id, Path.GetFullPath(modelPath), DateTime.Now);
                        _logger.LogInformation("OpenVINO 模型已启动: {Model} @ {Port}", modelPath, port);
                        return new OpenVinoRunResult
                        {
                            Success = true,
                            Port = port,
                            ProcessId = proc.Id,
                            Endpoint = $"http://127.0.0.1:{port}/v1"
                        };
                    }
                }
                catch { /* 未就绪，继续等 */ }
                await Task.Delay(2000, ct);
            }
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new OpenVinoRunResult { Success = false, Error = "模型加载超时（3 分钟），已停止进程" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 OpenVINO 模型失败: {Model}", modelPath);
            return new OpenVinoRunResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>停止模型（按端口，仅管理本管理器启动的实例）</summary>
    public async Task<bool> StopAsync(int port, CancellationToken ct = default)
    {
        if (!_running.TryRemove(port, out var inst))
            return false;
        try
        {
            var p = Process.GetProcessById(inst.ProcessId);
            p.Kill(entireProcessTree: true);
        }
        catch { }
        await Task.CompletedTask;
        return true;
    }

    /// <summary>当前运行中的模型列表（含本管理器启动的）</summary>
    public List<OpenVinoInstalledModelDto> GetRunning()
    {
        var result = new List<OpenVinoInstalledModelDto>();
        foreach (var r in _running.Values)
        {
            result.Add(new OpenVinoInstalledModelDto
            {
                Name = Path.GetFileName(r.ModelPath.TrimEnd(Path.DirectorySeparatorChar)),
                Path = r.ModelPath,
                IsRunning = true,
                Port = r.Port,
            });
        }
        return result;
    }

    #region 工具

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    private static async Task<int> FindFreePortAsync(int start, int end, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var port = start; port <= end; port++)
        {
            try
            {
                var resp = await http.GetAsync($"http://127.0.0.1:{port}/v1/models", ct);
                _ = resp; // 有响应说明被占用
            }
            catch
            {
                return port; // 连接失败 = 空闲
            }
        }
        return 0;
    }

    private string? ResolveScriptPath()
    {
        var published = Path.Combine(AppContext.BaseDirectory, "LocalVision", "openvino_llm_server.py");
        if (File.Exists(published)) return published;
        // 开发环境：源码目录
        var dev = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "services", "Baihua.AI.Provider", "LocalVision", "openvino_llm_server.py");
        if (File.Exists(dev)) return Path.GetFullPath(dev);
        return null;
    }

    private async Task<string?> FindPythonWithOpenVinoAsync(CancellationToken ct)
    {
        if (_pythonCache != null) return _pythonCache;
        lock (PythonLock)
        {
            if (_pythonCache != null) return _pythonCache;
            foreach (var candidate in new[] { "python", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = candidate == "py" ? "-3 -c \"import openvino_genai\"" : "-c \"import openvino_genai\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) continue;
                    _ = proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit(15000);
                    if (proc.HasExited && proc.ExitCode == 0)
                    {
                        _pythonCache = candidate == "py" ? "py" : "python";
                        return _pythonCache;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    #endregion
}
