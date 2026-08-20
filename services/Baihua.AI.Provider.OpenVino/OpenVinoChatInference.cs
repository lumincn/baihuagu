using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider.OpenVino;

/// <summary>
/// OpenVINO 本地模型对话推理（对接 OVMS 的 /v3/chat/completions 纯文本端点）
/// modelPath 形如 "openvino://3b" 或 "3b"，统一映射到 OVMS 对话模型 qwen2.5。
/// </summary>
public class OpenVinoChatInference : ILocalModelInference
{
    private readonly LocalVisionOptions _options;
    private readonly OmsOptions _omsOptions;
    private readonly ILogger<OpenVinoChatInference> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenVinoChatInference(
        IOptions<LocalVisionOptions> options,
        IOptions<OmsOptions> omsOptions,
        ILogger<OpenVinoChatInference> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _omsOptions = omsOptions.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public string ModelType => "openvino";

    /// <summary>OVMS REST 基地址</summary>
    private string BaseUrl => _omsOptions.Enabled ? _omsOptions.BaseUrl.TrimEnd('/') : string.Empty;

    private static string NormalizeModelId(string modelPath)
    {
        var id = modelPath.StartsWith("openvino://", StringComparison.OrdinalIgnoreCase)
            ? modelPath["openvino://".Length..]
            : modelPath;
        return id.Trim().Trim('/', '\\');
    }

    public async Task<bool> IsModelAvailableAsync(string modelPath)
    {
        _ = NormalizeModelId(modelPath);
        if (string.IsNullOrEmpty(BaseUrl)) return false;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var data = await client.GetFromJsonAsync<JsonElement>(BaseUrl + "/v1/models");
            if (data.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                return list.EnumerateArray().Any(e =>
                    e.TryGetProperty("id", out var id) && id.GetString() == OmsModelMap.ChatModelId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "检查 OVMS 对话模型可用性失败");
        }
        return false;
    }

    public async IAsyncEnumerable<string> ChatAsync(
        string modelPath,
        string message,
        string? systemPrompt = null,
        List<(string Role, string Content)>? history = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = NormalizeModelId(modelPath);
        var text = await ChatOnceAsync(message, systemPrompt, history, cancellationToken);
        if (!string.IsNullOrWhiteSpace(text))
            yield return text;
    }

    private async Task<string?> ChatOnceAsync(string message, string? systemPrompt,
        List<(string Role, string Content)>? history, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(BaseUrl))
        {
            _logger.LogWarning("OVMS 未启用");
            return "[OpenVINO 对话失败: OVMS 未启用]";
        }

        // 组装 OpenAI 风格 messages
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        if (history != null)
        {
            foreach (var (role, content) in history)
            {
                var r = role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                messages.Add(new { role = r, content });
            }
        }
        messages.Add(new { role = "user", content = message });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            var resp = await client.PostAsJsonAsync(
                BaseUrl + "/v3/chat/completions",
                new { model = OmsModelMap.ChatModelId, messages, max_tokens = 1024, stream = false },
                cts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            // 解析 choices[0].message.content
            if (json.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }
            _logger.LogWarning("OVMS 对话响应缺少 choices[0].message.content");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OVMS 对话失败");
            return $"[OpenVINO 对话失败: {ex.Message}]";
        }
    }
}
