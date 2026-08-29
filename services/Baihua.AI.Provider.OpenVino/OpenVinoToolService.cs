using System.Net.Http.Json;
using System.Text.Json;
using Baihua.Contracts.LocalModels;
using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider.OpenVino;

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
/// OpenVINO GenAI 本地工具（对接 OVMS 常驻服务）：模型已由 OVMS 托管（config.json 注册，
/// 首次推理自动加载），本类负责状态探测 / 目录扫描 / 详情。不再启动自研 Python 服务。
/// </summary>
public class OpenVinoToolService : ILocalModelTool
{
    private readonly OpenVinoToolOptions _options;
    private readonly OmsOptions _omsOptions;
    private readonly ILogger<OpenVinoToolService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // 版本探测/目录扫描缓存（避免每次页面刷新都冷启动 python / 遍历大目录）
    private string? _versionCache;
    private DateTime _versionCacheAt;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, DateTime At)> _dirSizeCache = new();

    public OpenVinoToolService(
        IOptions<OpenVinoToolOptions> options,
        IOptions<OmsOptions> omsOptions,
        ILogger<OpenVinoToolService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _omsOptions = omsOptions.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>ILocalModelTool 标识</summary>
    public string Id => "openvino";
    public string Name => "OpenVINO";

    /// <summary>OVMS REST 基地址</summary>
    private string BaseUrl => _omsOptions.BaseUrl.TrimEnd('/');

    /// <summary>OVMS 监听端口（从 BaseUrl 解析，默认 8000）</summary>
    private int OmsPort
    {
        get
        {
            try { return new Uri(BaseUrl).Port; }
            catch { return 8000; }
        }
    }

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
        var env = Environment.GetEnvironmentVariable("VISION_MODEL_7B");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        // 主视觉/多模态模型：Qwen3.5-9B（VL-7B 已清理）
        return Path.Combine(ModelRoot, "Qwen3.5-9B-int8-ov");
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
            // OVMS（OpenVINO Model Server）不提供 /health，用 OpenAI 兼容的 /v1/models 探测；
            // 旧自研 host（openvino_llm_server.py）同样提供 /v1/models，双兼容。
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var data = await client.GetFromJsonAsync<JsonElement>(podUrl.TrimEnd('/') + "/v1/models", ct);
            return data.TryGetProperty("data", out var list)
                && list.ValueKind == JsonValueKind.Array
                && list.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> DetectOpenVinoVersionAsync(CancellationToken ct = default)
    {
        // 5 分钟内不重复探测（python 冷启动约 1-2s）
        if (_versionCache != null && DateTime.UtcNow - _versionCacheAt < TimeSpan.FromMinutes(5))
            return _versionCache;
        try
        {
            var python = ResolvePythonExe();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import openvino; print(openvino.__version__)");
            using var p = System.Diagnostics.Process.Start(psi);
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

    private string ResolvePythonExe()
    {
        if (!string.IsNullOrWhiteSpace(_options.PythonExe))
            return _options.PythonExe;
        return OperatingSystem.IsWindows() ? "python" : "python3";
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

    /// <summary>探测 OVMS 是否运行（查询模型列表端点）</summary>
    public async Task<bool> IsServerRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var resp = await client.GetAsync(BaseUrl + "/v1/models", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>确保 OVMS 在运行（模型常驻，无需启动 Python）</summary>
    public async Task EnsureServerRunningAsync(CancellationToken ct = default)
    {
        if (await IsServerRunningAsync(ct))
            return;
        throw new InvalidOperationException("OVMS 服务不可达：请确认 ovms 服务已启动（http://127.0.0.1:8000）且 OpenVinoOms:BaseUrl 配置正确");
    }

    /// <summary>OVMS 当前注册的模型 id 集合（探测 /v1/models）</summary>
    private async Task<HashSet<string>> GetOmsModelIdsAsync(CancellationToken ct = default)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var data = await client.GetFromJsonAsync<JsonElement>(BaseUrl + "/v1/models", cts.Token);
            if (data.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in list.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        result.Add(id.GetString()!);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取 OVMS 模型列表失败");
        }
        return result;
    }

    /// <summary>可用模型列表（本地目录中存在的 OpenVINO 模型 id）</summary>
    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var m in DistinctModels())
        {
            if (Directory.Exists(ResolveModelPath(m)))
                result.Add(m.Id);
        }
        return await Task.FromResult(result);
    }

    /// <summary>
    /// 把前端传入的模型名（可能是显示名 Name 或内部 Id，如 "Qwen2.5-VL-7B-Instruct (INT4)" 或 "7b"）
    /// 规整为内部 Id（7b）。模型由 OVMS 托管，此处仅用于目录扫描/详情/状态匹配。
    /// </summary>
    private string NormalizeModelId(string modelOrName)
    {
        var key = modelOrName?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return "7b";
        foreach (var m in DistinctModels())
        {
            if (string.Equals(m.Id, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase))
                return m.Id;
        }
        if (key.Equals("7b", StringComparison.OrdinalIgnoreCase)) return "7b";
        return key;
    }

    /// <summary>
    /// 加载模型：模型由 OVMS 常驻托管（config.json 已注册），首次推理自动加载。
    /// 这里校验该模型在 OVMS 可见（或本地目录存在）即返回成功，不做真正加载/卸载。
    /// </summary>
    public async Task<bool> LoadModelAsync(string modelId, CancellationToken ct = default)
    {
        _ = NormalizeModelId(modelId);
        // OVMS 已注册则视为已加载（懒加载，首次请求自动编译）；探测失败时退回目录存在判定
        var omsIds = await GetOmsModelIdsAsync(ct);
        var registered = omsIds.Count > 0 && omsIds.Contains(OmsModelMap.VisionModelId("7b"));
        return registered || omsIds.Count == 0;
    }

    /// <summary>卸载模型：OVMS 常驻托管，不支持手动卸载，始终返回成功（幂等）+ 状态探测</summary>
    public async Task<bool> UnloadModelAsync(string modelId, CancellationToken ct = default)
    {
        _ = NormalizeModelId(modelId);
        if (!await IsServerRunningAsync(ct))
            return true; // 服务不可达视为无需卸载
        return true;
    }

    /// <summary>
    /// 已加载（运行中）模型：枚举 OVMS /v1/models 注册的全部模型
    /// （视觉 qwen2.5-vl-7b / 对话 qwen2.5、biancang / 嵌入 bge-small-zh），
    /// 注册即视为运行中（懒加载，首次推理自动编译加载）。
    /// </summary>
    public async Task<List<RunningModelDto>> GetRunningModelsAsync(CancellationToken ct = default)
    {
        var result = new List<RunningModelDto>();
        var omsIds = await GetOmsModelIdsAsync(ct);
        foreach (var omsId in omsIds)
        {
            var dto = BuildRunningModelDto(omsId);
            if (dto != null) result.Add(dto);
        }
        return result.OrderBy(r => r.DisplayName).ToList();
    }

    /// <summary>OVMS 模型 id → 运行中模型 DTO（未知 id 也兜底显示，不丢弃 OVMS 注册模型）</summary>
    private RunningModelDto? BuildRunningModelDto(string omsId)
    {
        var key = omsId?.Trim() ?? "";
        if (key.Length == 0) return null;

        // OVMS 注册 id → 百花内部标识 / 显示名 / 家族（与目录/下载列表保持一致）
        string modelId, displayName, family;
        switch (key.ToLowerInvariant())
        {
            case "qwen2.5-vl-7b":
                modelId = "7b";
                displayName = "Qwen2.5-VL-7B-Instruct (INT4)";
                family = "Qwen2.5-VL";
                break;
            case "qwen2.5-vl-3b":
                modelId = "qwen2.5-vl-3b";
                displayName = "Qwen 2.5 VL 3B（视觉）";
                family = "Qwen2.5-VL";
                break;
            case "qwen2.5":
                modelId = "qwen2.5-7b";
                displayName = "Qwen 2.5 7B Instruct";
                family = "Qwen2.5";
                break;
            case "qwen2.5-14b":
                modelId = "qwen2.5-14b";
                displayName = "Qwen 2.5 14B Instruct";
                family = "Qwen2.5";
                break;
            case "qwen2.5-coder-7b":
                modelId = "qwen2.5-coder-7b";
                displayName = "Qwen 2.5 Coder 7B";
                family = "Qwen2.5-Coder";
                break;
            case "qwen3.5-9b":
                modelId = "qwen3.5-9b";
                displayName = "Qwen 3.5 9B（int8）";
                family = "Qwen3.5";
                break;
            case "biancang":
                modelId = "biancang-instruct";
                displayName = "扁仓 BianCang Instruct（医疗）";
                family = "BianCang";
                break;
            case "bge-small-zh":
                modelId = "bge-small-zh";
                displayName = "BGE Small ZH v1.5（嵌入）";
                family = "BGE";
                break;
            default:
                modelId = key;
                displayName = key;
                family = null;
                break;
        }

        long size = 0;
        var dirName = OmsModelMap.DirNameForOmsId(key);
        if (!string.IsNullOrWhiteSpace(dirName))
        {
            var path = Path.Combine(ModelRoot, dirName);
            if (Directory.Exists(path)) size = GetDirSizeCached(path);
        }

        return new RunningModelDto
        {
            ToolId = "openvino",
            ToolName = "OpenVINO",
            ModelName = modelId,
            DisplayName = displayName,
            Port = OmsPort,
            SizeBytes = size,
            RamBytes = null,
            VramBytes = null,
            Family = family,
        };
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

        // 本地 OVMS 常驻托管：注册模型即视为"已下载且运行中"（目录里可能查不到，但 OVMS 已托管）
        MergeOmsServed(result, BaseUrl, ct);

        // k8s：视觉/LLM 模型托管在 bh-openvino pod（OVMS，/models PVC），本机模型根没有——
        // 通过 OPENVINO_LLM_URL/OPENVINO_HOST_URL 的 /v1/models 合并显示为"已下载且运行中"
        var podUrl = Environment.GetEnvironmentVariable("OPENVINO_LLM_URL")
            ?? Environment.GetEnvironmentVariable("OPENVINO_HOST_URL");
        if (!string.IsNullOrWhiteSpace(podUrl))
            MergeOmsServed(result, podUrl, ct);

        return result;
    }

    /// <summary>把 OVMS 服务（本地常驻 / k8s pod）注册的模型合并进"已下载且运行中"列表</summary>
    private void MergeOmsServed(List<DownloadedModelDto> result, string baseUrl, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var data = client.GetFromJsonAsync<JsonElement>(baseUrl.TrimEnd('/') + "/v1/models", cts.Token)
                .GetAwaiter().GetResult();
            if (!data.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array) return;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                // 已知模型用显示名（与本地扫描/目录列表去重，避免同一模型出现两次）
                var name = BuildRunningModelDto(id)?.DisplayName ?? id.Split('/').Last();
                if (result.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
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
        catch
        {
            // 服务不可达则忽略
        }
    }

    /// <summary>
    /// OpenVINO 模型详情：定位模型目录并读取大小 / Vision 类型 / dtype（openvino_config.json）
    /// 与 config.json 的 model_type / architectures，供『详情』弹窗展示。
    /// </summary>
    public async Task<ModelDetailsDto?> GetModelDetailsAsync(string modelName, CancellationToken ct = default)
    {
        var key = NormalizeModelId(modelName);
        var model = DistinctModels().FirstOrDefault(m => string.Equals(m.Id, key, StringComparison.OrdinalIgnoreCase));
        if (model == null) return null;

        var path = ResolveModelPath(model);
        var details = new ModelDetailsDto { Name = model.Name, ToolId = "openvino" };
        if (!Directory.Exists(path))
            return details;

        var sb = new List<string>
        {
            $"路径: {path}",
            $"大小: {FormatSizeText(GetDirSizeCached(path))}",
        };
        var isVl = File.Exists(Path.Combine(path, "openvino_vision_embeddings_model.xml"))
                   || File.Exists(Path.Combine(path, "openvino_language_model.bin"))
                   || File.Exists(Path.Combine(path, "openvino_vision_embeddings_model.bin"));
        sb.Add($"类型: {(isVl ? "Vision-Language (VL)" : "LLM")}");
        sb.Add($"托管: OVMS (模型 id: {OmsModelMap.VisionModelId(model.Id)})");

        // 从 openvino_config.json 读 dtype/量化
        var ovCfg = Path.Combine(path, "openvino_config.json");
        if (File.Exists(ovCfg))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ovCfg));
                if (doc.RootElement.TryGetProperty("weight_format", out var wf) && wf.ValueKind == JsonValueKind.String)
                    sb.Add($"量化: {wf.GetString()}");
            }
            catch { }
        }
        // 从 config.json 读 model_type / architectures
        var cfg = Path.Combine(path, "config.json");
        if (File.Exists(cfg))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.TryGetProperty("model_type", out var mt) && mt.ValueKind == JsonValueKind.String)
                    sb.Add($"模型类型: {mt.GetString()}");
                if (doc.RootElement.TryGetProperty("architectures", out var arch) && arch.ValueKind == JsonValueKind.Array)
                    sb.Add($"架构: {string.Join(", ", arch.EnumerateArray().Select(e => e.GetString()))}");
            }
            catch { }
        }

        details.Parameters = string.Join("\n", sb);
        await Task.CompletedTask;
        return details;
    }

    private static string FormatSizeText(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / 1024d / 1024d / 1024d:F2} GB";
        if (bytes >= 1L << 20) return $"{bytes / 1024d / 1024d:F1} MB";
        return $"{bytes / 1024d:F0} KB";
    }
}
