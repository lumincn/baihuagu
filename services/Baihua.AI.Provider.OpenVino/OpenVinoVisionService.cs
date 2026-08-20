using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider.OpenVino;

/// <summary>
/// 本地视觉推理配置（Qwen2.5-VL + OpenVINO，通过 OVMS 的 /v3/chat/completions 调用）
/// </summary>
public class LocalVisionOptions
{
    /// <summary>功能开关</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>遗留字段：视觉端口（已由 OVMS BaseUrl 取代，保留兼容）</summary>
    public int Port { get; set; } = 8801;

    /// <summary>Python 可执行文件（已废弃——不再自研 Python 服务，保留字段兼容配置）</summary>
    public string? PythonExe { get; set; }

    /// <summary>vision_server.py 路径（已废弃，保留兼容）</summary>
    public string? ScriptPath { get; set; }

    /// <summary>首次调用时自动拉起服务（已由 OVMS 常驻取代，保留兼容）</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>服务启动健康检查超时（秒，保留兼容）</summary>
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
/// 本地视觉推理服务：通过 OVMS 的 OpenAI 兼容 /v3/chat/completions 提供图片识别。
/// 模型常驻 OVMS（config.json 注册），首次看图自动加载，无需手动启动 Python 服务。
/// </summary>
public class OpenVinoVisionService : ILocalVisionInference
{
    private readonly LocalVisionOptions _options;
    private readonly OmsOptions _omsOptions;
    private readonly ILogger<OpenVinoVisionService> _logger;

    public OpenVinoVisionService(IOptions<LocalVisionOptions> options, IOptions<OmsOptions> omsOptions, ILogger<OpenVinoVisionService> logger)
    {
        _options = options.Value;
        _omsOptions = omsOptions.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled && _omsOptions.Enabled;

    private string BaseUrl => _omsOptions.BaseUrl.TrimEnd('/');

    /// <summary>模型目录解析：配置路径 -> 环境变量覆盖 -> 用户目录默认（详情展示用，不启动服务）</summary>
    private static string ResolveModelPath(LocalVisionModelOptions model)
    {
        if (!string.IsNullOrWhiteSpace(model.Path))
            return model.Path;
        var envVar = model.Id == "7b" ? "VISION_MODEL_7B" : "VISION_MODEL_3B";
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        var folderSuffix = model.Id == "7b" ? "7B" : "3B";
        return Path.Combine(Baihua.Contracts.BaihuaPaths.Home, "models", $"Qwen2.5-VL-{folderSuffix}-Instruct-int4-ov");
    }

    /// <summary>
    /// 确保 OVMS 在运行（不可达则抛异常提示）。模型懒加载，由 OVMS 自动编译。
    /// </summary>
    public async Task EnsureServerRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await IsServerRunningAsync(cancellationToken))
            return;
        throw new InvalidOperationException("OVMS 服务不可达：请确认 ovms 服务已启动（http://127.0.0.1:8000）");
    }

    /// <summary>查询 OVMS 运行状态（探测模型列表端点）</summary>
    public async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await client.GetAsync(BaseUrl + "/v1/models", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "探测 OVMS 状态失败");
            return false;
        }
    }

    /// <summary>获取完整状态（含模型信息）</summary>
    public async Task<VisionStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new VisionStatusDto { Enabled = Enabled, Port = _omsOptions.Enabled ? 8000 : 0 };
        var seen = new HashSet<string>();
        foreach (var model in _options.Models)
        {
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

    /// <summary>识别图片（调用 OVMS /v3/chat/completions，image_url 传 base64）</summary>
    public async Task<VisionResultDto> RecognizeAsync(
        byte[] imageBytes, string prompt, string modelId, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await EnsureServerRunningAsync(cancellationToken);

        var ovmsModel = OmsModelMap.VisionModelId(modelId);
        var imageB64 = Convert.ToBase64String(imageBytes);
        var promptText = string.IsNullOrWhiteSpace(prompt) ? "请详细描述这张图片的内容。" : prompt;

        var messages = new[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = promptText },
                    new { type = "image_url", image_url = new { url = $"data:image/png;base64,{imageB64}" } },
                },
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(10)); // 首次加载模型可能较慢

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var response = await client.PostAsJsonAsync(
            BaseUrl + "/v3/chat/completions",
            new { model = ovmsModel, messages, max_tokens = 1024, stream = false },
            cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
            throw new InvalidOperationException($"OVMS 视觉返回 {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = "";
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg)
            && msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString() ?? "";
        }
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"OVMS 视觉服务错误: {err.GetString()}");

        sw.Stop();
        return new VisionResultDto
        {
            Text = text,
            Model = ovmsModel,
            ElapsedMs = sw.ElapsedMilliseconds,
            ServerRunning = true,
        };
    }
}
