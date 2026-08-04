using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Microsoft.Extensions.Options;

namespace Baihua.Family.Services.LocalAI;

/// <summary>
/// 本地视觉推理配置（Qwen2.5-VL + OpenVINO，通过常驻 Python 服务调用）
/// </summary>
public class LocalVisionOptions
{
    /// <summary>功能开关</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Python 视觉服务端口</summary>
    public int Port { get; set; } = 8801;

    /// <summary>Python 可执行文件（缺省用 PATH 里的 python）</summary>
    public string? PythonExe { get; set; }

    /// <summary>vision_server.py 路径（缺省在 Baihua.AI 内容根目录 LocalVision 下）</summary>
    public string? ScriptPath { get; set; }

    /// <summary>首次调用时自动拉起 Python 服务</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>服务启动健康检查超时（秒）</summary>
    public int StartupTimeoutSeconds { get; set; } = 60;

    /// <summary>模型配置</summary>
    public List<LocalVisionModelOptions> Models { get; set; } = new()
    {
        new() { Id = "3b", Name = "Qwen2.5-VL-3B-Instruct (INT4)" },
        new() { Id = "7b", Name = "Qwen2.5-VL-7B-Instruct (INT4)" },
    };
}

public class LocalVisionModelOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
}

/// <summary>
/// 本地视觉推理服务：管理常驻 Python 进程（vision_server.py），提供图片识别能力
/// </summary>
public class OpenVinoVisionService
{
    private readonly LocalVisionOptions _options;
    private readonly ILogger<OpenVinoVisionService> _logger;
    private readonly string _baseUrl;
    private readonly object _startLock = new();
    private bool _started;

    public OpenVinoVisionService(IOptions<LocalVisionOptions> options, ILogger<OpenVinoVisionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _baseUrl = $"http://127.0.0.1:{_options.Port}";
    }

    public bool Enabled => _options.Enabled;

    private string BaseUrl => _baseUrl;

    /// <summary>模型目录解析：配置路径 -> 环境变量覆盖 -> 用户目录默认</summary>
    private static string ResolveModelPath(LocalVisionModelOptions model)
    {
        if (!string.IsNullOrWhiteSpace(model.Path))
            return model.Path;
        var envVar = model.Id == "7b" ? "VISION_MODEL_7B" : "VISION_MODEL_3B";
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        // 目录名中的 B 为大写（Qwen2.5-VL-3B-Instruct-int4-ov）
        var folderSuffix = model.Id == "7b" ? "7B" : "3B";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".openclaw", "models", $"Qwen2.5-VL-{folderSuffix}-Instruct-int4-ov");
    }

    /// <summary>
    /// 确保 Python 视觉服务在运行（未运行且 AutoStart 时自动拉起）
    /// </summary>
    public async Task EnsureServerRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await IsServerRunningAsync(cancellationToken))
            return;

        if (!_options.AutoStart)
            throw new InvalidOperationException("本地视觉服务未运行");

        lock (_startLock)
        {
            if (_started)
            {
                // 已尝试启动过（可能失败），再查一次
            }
            else
            {
                StartPythonServer();
                _started = true;
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsServerRunningAsync(cancellationToken))
                return;
            await Task.Delay(1000, cancellationToken);
        }

        throw new TimeoutException($"本地视觉服务启动超时（{_options.StartupTimeoutSeconds}s），请检查 vision_server.log");
    }

    private void StartPythonServer()
    {
        var scriptPath = ResolveScriptPath();
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"vision_server.py 不存在: {scriptPath}");

        var pythonExe = ResolvePythonExe();
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "vision_server.log");

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        psi.ArgumentList.Add(scriptPath);
        psi.Environment["VISION_PORT"] = _options.Port.ToString();
        // 输出重定向到日志文件
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        _logger.LogInformation("启动本地视觉服务: {Python} {Script} (port={Port})", pythonExe, scriptPath, _options.Port);
        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("无法启动 Python 视觉服务进程");

        // 异步把输出写入日志文件，避免管道阻塞
        _ = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(logFile, append: true, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === vision server started (pid={process.Id}) ===");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var outTask = stdout.ContinueWith(t => writer.WriteLine(t.IsCompletedSuccessfully ? t.Result : ""));
                var errTask = stderr.ContinueWith(t => writer.WriteLine(t.IsCompletedSuccessfully ? t.Result : ""));
                await Task.WhenAll(outTask, errTask);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 vision_server 日志失败");
            }
        });
    }

    private string ResolveScriptPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ScriptPath) && File.Exists(_options.ScriptPath))
            return _options.ScriptPath;

        // 默认：Baihua.AI 内容根目录/LocalVision/vision_server.py
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LocalVision", "vision_server.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Baihua.AI", "LocalVision", "vision_server.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "services", "Baihua.AI", "LocalVision", "vision_server.py"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(Path.GetFullPath(c)))
                return Path.GetFullPath(c);
        }
        return Path.GetFullPath(candidates[0]);
    }

    private string ResolvePythonExe()
    {
        if (!string.IsNullOrWhiteSpace(_options.PythonExe))
            return _options.PythonExe;
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    /// <summary>查询视觉服务运行状态</summary>
    public async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var resp = await client.GetAsync("/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>获取完整状态（含模型信息）</summary>
    public async Task<VisionStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new VisionStatusDto { Enabled = Enabled };
        var seen = new HashSet<string>();
        foreach (var model in _options.Models)
        {
            // 配置绑定可能因默认值+配置叠加产生重复项，按 Id 去重
            if (!seen.Add(model.Id))
                continue;
            var path = ResolveModelPath(model);
            var exists = Directory.Exists(path);
            status.Models.Add(new VisionModelInfo
            {
                Id = model.Id,
                Name = model.Name,
                Path = path,
                Exists = exists,
                SizeBytes = exists ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                }) : 0,
            });
        }

        status.ServerRunning = await IsServerRunningAsync(cancellationToken);
        return status;
    }

    /// <summary>识别图片</summary>
    public async Task<VisionResultDto> RecognizeAsync(
        byte[] imageBytes, string prompt, string modelId, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await EnsureServerRunningAsync(cancellationToken);

        var request = new VisionRequestDto
        {
            ImageBase64 = Convert.ToBase64String(imageBytes),
            Prompt = string.IsNullOrWhiteSpace(prompt) ? "请详细描述这张图片的内容。" : prompt,
            Model = string.IsNullOrWhiteSpace(modelId) ? "3b" : modelId,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(10)); // 首次加载模型可能较慢

        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await client.PostAsJsonAsync("/v1/vision", request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
            throw new InvalidOperationException($"视觉服务返回 {(int)response.StatusCode}: {errorBody}");
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"本地视觉服务错误: {err.GetString()}");

        sw.Stop();
        return new VisionResultDto
        {
            Text = text,
            Model = request.Model,
            ElapsedMs = sw.ElapsedMilliseconds,
            ServerRunning = true,
        };
    }
}
