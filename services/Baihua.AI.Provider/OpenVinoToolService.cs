using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.LocalModels;
using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider;

/// <summary>
/// OpenVINO 工具配置（与 Baihua.AI 的 LocalVision 配置同名同结构）
/// </summary>
public class OpenVinoToolOptions
{
    public int Port { get; set; } = 8801;
    public string? PythonExe { get; set; }
    public string? ScriptPath { get; set; }
    public bool AutoStart { get; set; } = true;
    public int StartupTimeoutSeconds { get; set; } = 90;
    public string ModelRoot { get; set; } = "";
    public List<OpenVinoModelOption> Models { get; set; } = new()
    {
        new() { Id = "3b", Name = "Qwen2.5-VL-3B-Instruct (INT4)" },
        new() { Id = "7b", Name = "Qwen2.5-VL-7B-Instruct (INT4)" },
    };
}

public class OpenVinoModelOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
}

/// <summary>
/// OpenVINO GenAI 本地视觉工具（对接常驻 vision_server.py：模型加载/卸载/运行状态）
/// </summary>
public class OpenVinoToolService
{
    private readonly OpenVinoToolOptions _options;
    private readonly ILogger<OpenVinoToolService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _startLock = new();
    private bool _started;

    // 版本探测/目录扫描缓存（避免每次页面刷新都冷启动 python / 遍历大目录）
    private string? _versionCache;
    private DateTime _versionCacheAt;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, DateTime At)> _dirSizeCache = new();

    public OpenVinoToolService(IOptions<OpenVinoToolOptions> options, ILogger<OpenVinoToolService> logger, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private string BaseUrl => $"http://127.0.0.1:{_options.Port}";

    /// <summary>去重后的模型配置（options 绑定可能因默认值+配置叠加产生重复）</summary>
    private IEnumerable<OpenVinoModelOption> DistinctModels()
    {
        var seen = new HashSet<string>();
        foreach (var m in _options.Models)
        {
            if (seen.Add(m.Id))
                yield return m;
        }
    }

    private string ModelRoot =>
        string.IsNullOrWhiteSpace(_options.ModelRoot)
            ? Path.Combine(Baihua.Contracts.BaihuaPaths.Home, "models")
            : _options.ModelRoot;

    public string DefaultModelPath => ModelRoot;

    private string ResolveModelPath(OpenVinoModelOption model)
    {
        if (!string.IsNullOrWhiteSpace(model.Path))
            return model.Path;
        var envVar = model.Id == "7b" ? "VISION_MODEL_7B" : "VISION_MODEL_3B";
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        var suffix = model.Id == "7b" ? "7B" : "3B";
        return Path.Combine(ModelRoot, $"Qwen2.5-VL-{suffix}-Instruct-int4-ov");
    }

    /// <summary>探测工具状态：(是否安装, 版本, 是否运行, 模型目录)</summary>
    public async Task<(bool Installed, string? Version, bool Running, string ModelPath)> GetToolInfoAsync(CancellationToken ct = default)
    {
        var models = DistinctModels();
        var exists = models.Any(m => Directory.Exists(ResolveModelPath(m)));
        var version = await DetectOpenVinoVersionAsync(ct);
        var installed = exists && !string.IsNullOrEmpty(version);
        var running = await IsServerRunningAsync(ct);

        // k8s：OpenVINO 由 bh-openvino pod 托管（本容器无 python/openvino-genai），
        // 探测 OPENVINO_LLM_URL/OPENVINO_HOST_URL 的 /health 视为"已安装且运行中"
        if (!installed && await IsRemotePodServingAsync(ct))
        {
            installed = true;
            running = true;
            version ??= "pod (k8s)";
        }
        return (installed, version, running, ModelRoot);
    }

    private async Task<bool> IsRemotePodServingAsync(CancellationToken ct = default)
    {
        var podUrl = Environment.GetEnvironmentVariable("OPENVINO_LLM_URL")
            ?? Environment.GetEnvironmentVariable("OPENVINO_HOST_URL");
        if (string.IsNullOrWhiteSpace(podUrl)) return false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var health = await client.GetFromJsonAsync<RemoteHealthDto>(podUrl.TrimEnd('/') + "/health", ct);
            return health is { Ok: true };
        }
        catch
        {
            return false;
        }
    }

    private sealed class RemoteHealthDto
    {
        public bool Ok { get; set; }
        public string? Model { get; set; }
        public string? ModelPath { get; set; }
    }

    private async Task<string?> DetectOpenVinoVersionAsync(CancellationToken ct = default)
    {
        // 5 分钟内不重复探测（python 冷启动约 1-2s）
        if (_versionCache != null && DateTime.UtcNow - _versionCacheAt < TimeSpan.FromMinutes(5))
            return _versionCache;
        try
        {
            var python = ResolvePythonExe();
            var psi = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import openvino; print(openvino.__version__)");
            using var p = Process.Start(psi);
            if (p == null) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var outTask = p.StandardOutput.ReadToEndAsync(cts.Token);
            var errTask = p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            var ver = (await outTask).Trim();
            if (!string.IsNullOrWhiteSpace(ver))
            {
                _versionCache = ver;
                _versionCacheAt = DateTime.UtcNow;
            }
            return string.IsNullOrWhiteSpace(ver) ? null : ver;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "detect openvino version failed");
            return null;
        }
    }

    /// <summary>目录总大小（30s 缓存，避免每次轮询都遍历模型目录）</summary>
    private long GetDirSizeCached(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_dirSizeCache.TryGetValue(path, out var entry) && now - entry.At < TimeSpan.FromSeconds(30))
                return entry.Size;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            _dirSizeCache[path] = (total, now);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> IsServerRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var resp = await client.GetAsync(BaseUrl + "/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureServerRunningAsync(CancellationToken ct = default)
    {
        if (await IsServerRunningAsync(ct))
            return;
        if (!_options.AutoStart)
            throw new InvalidOperationException("OpenVINO 视觉服务未运行（AutoStart 关闭）");

        lock (_startLock)
        {
            if (!_started)
            {
                StartPythonServer();
                _started = true;
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsServerRunningAsync(ct))
                return;
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException($"OpenVINO 视觉服务启动超时（{_options.StartupTimeoutSeconds}s），请检查日志");
    }

    private void StartPythonServer()
    {
        var scriptPath = ResolveScriptPath();
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"vision_server.py 不存在: {scriptPath}");

        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "vision_server.log");

        var psi = new ProcessStartInfo
        {
            FileName = ResolvePythonExe(),
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(scriptPath);
        psi.Environment["VISION_PORT"] = _options.Port.ToString();

        _logger.LogInformation("启动 OpenVINO 视觉服务: {Script} (port={Port})", scriptPath, _options.Port);
        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("无法启动 Python 视觉服务进程");

        _ = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(logFile, append: true, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === vision server started (pid={process.Id}) ===");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(
                    stdout.ContinueWith(t => writer.WriteLine(t.IsCompletedSuccessfully ? t.Result : "")),
                    stderr.ContinueWith(t => writer.WriteLine(t.IsCompletedSuccessfully ? t.Result : "")));
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
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LocalVision", "vision_server.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Baihua.AI.Provider", "LocalVision", "vision_server.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "services", "Baihua.AI.Provider", "LocalVision", "vision_server.py"),
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

    /// <summary>可用模型列表（返回模型 Id：3b / 7b）</summary>
    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var m in DistinctModels())
        {
            if (Directory.Exists(ResolveModelPath(m)))
                result.Add(m.Id);
        }
        return result;
    }

    public async Task<bool> LoadModelAsync(string modelId, CancellationToken ct = default)
    {
        await EnsureServerRunningAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var resp = await client.PostAsJsonAsync(BaseUrl + "/v1/vision/reload", new { model = modelId }, cts.Token);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> UnloadModelAsync(string modelId, CancellationToken ct = default)
    {
        if (!await IsServerRunningAsync(ct))
            return true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var resp = await client.PostAsJsonAsync(BaseUrl + "/v1/vision/unload", new { model = modelId }, cts.Token);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>已加载（运行中）模型</summary>
    public async Task<List<RunningModelDto>> GetRunningModelsAsync(CancellationToken ct = default)
    {
        var result = new List<RunningModelDto>();
        if (!await IsServerRunningAsync(ct))
            return result;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var health = await client.GetFromJsonAsync<JsonElement>(BaseUrl + "/health", cts.Token);
            if (health.TryGetProperty("loaded", out var loaded) && loaded.ValueKind == JsonValueKind.Array)
            {
                var loadedIds = loaded.EnumerateArray().Select(e => e.GetString()).ToHashSet();
                foreach (var m in DistinctModels())
                {
                    if (loadedIds.Contains(m.Id))
                    {
                        var path = ResolveModelPath(m);
                        result.Add(new RunningModelDto
                        {
                            ToolId = "openvino",
                            ToolName = "OpenVINO",
                            ModelName = m.Id,
                            DisplayName = m.Name,
                            SizeBytes = Directory.Exists(path) ? GetDirSizeCached(path) : 0,
                            RamBytes = null,
                            VramBytes = null,
                            Family = "Qwen2.5-VL",
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取 OpenVINO 运行模型失败");
        }
        return result;
    }

    /// <summary>已下载模型（OpenVINO IR 目录）</summary>
    public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(CancellationToken ct = default)
    {
        var result = new List<DownloadedModelDto>();
        var running = await GetRunningModelsAsync(ct);
        var runningIds = running.Select(r => r.ModelName).ToHashSet();
        foreach (var m in DistinctModels())
        {
            var path = ResolveModelPath(m);
            if (!Directory.Exists(path))
                continue;
            result.Add(new DownloadedModelDto
            {
                Name = m.Name,
                ToolId = "openvino",
                ToolName = "OpenVINO",
                SizeBytes = GetDirSizeCached(path),
                ModifiedAt = Directory.GetLastWriteTime(path),
                IsRunning = runningIds.Contains(m.Id),
            });
        }

        // k8s：视觉/LLM 模型托管在 bh-openvino pod（/models），本机模型根没有——
        // 通过 OPENVINO_LLM_URL/OPENVINO_HOST_URL 的 /health 合并显示为"已下载且运行中"
        try
        {
            var podUrl = Environment.GetEnvironmentVariable("OPENVINO_LLM_URL")
                ?? Environment.GetEnvironmentVariable("OPENVINO_HOST_URL");
            if (!string.IsNullOrWhiteSpace(podUrl))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var health = await client.GetFromJsonAsync<RemoteHealthDto>(podUrl.TrimEnd('/') + "/health", ct);
                if (health != null && !string.IsNullOrWhiteSpace(health.ModelPath))
                {
                    var name = Path.GetFileName(health.ModelPath.TrimEnd('/').TrimEnd('\\'));
                    if (!string.IsNullOrWhiteSpace(name) && !result.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(new DownloadedModelDto
                        {
                            Name = name,
                            ToolId = "openvino",
                            ToolName = "OpenVINO",
                            SizeBytes = 0,
                            ModifiedAt = DateTime.Now,
                            IsRunning = true,
                        });
                    }
                }
            }
        }
        catch
        {
            // 远端不可达则忽略
        }

        return result;
    }
}
