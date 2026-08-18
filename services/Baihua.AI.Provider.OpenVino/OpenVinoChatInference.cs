using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider.OpenVino;

/// <summary>
/// OpenVINO 本地模型对话推理（对接 vision_server.py 的 /v1/chat 纯文本端点）
/// modelPath 形如 "openvino://3b" 或 "3b"，对应 LocalVision 配置里的模型 Id
/// </summary>
public class OpenVinoChatInference : ILocalModelInference
{
    private readonly LocalVisionOptions _options;
    private readonly ILogger<OpenVinoChatInference> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenVinoChatInference(
        IOptions<LocalVisionOptions> options,
        ILogger<OpenVinoChatInference> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public string ModelType => "openvino";

    private string BaseUrl => $"http://127.0.0.1:{_options.Port}";

    private static string NormalizeModelId(string modelPath)
    {
        var id = modelPath.StartsWith("openvino://", StringComparison.OrdinalIgnoreCase)
            ? modelPath["openvino://".Length..]
            : modelPath;
        return id.Trim().Trim('/', '\\');
    }

    public async Task<bool> IsModelAvailableAsync(string modelPath)
    {
        var id = NormalizeModelId(modelPath);
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var health = await client.GetFromJsonAsync<JsonElement>(BaseUrl + "/health");
            if (health.TryGetProperty("loaded", out var loaded) && loaded.ValueKind == JsonValueKind.Array)
            {
                return loaded.EnumerateArray().Any(e => e.GetString() == id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "检查 OpenVINO 模型可用性失败");
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
        var id = NormalizeModelId(modelPath);
        var prompt = BuildPrompt(message, systemPrompt, history);
        var text = await ChatOnceAsync(id, prompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(text))
            yield return text;
    }

    private async Task<string?> ChatOnceAsync(string modelId, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            var resp = await client.PostAsJsonAsync(
                BaseUrl + "/v1/chat",
                new { model = modelId, prompt, max_tokens = 1024 },
                cts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            return json.TryGetProperty("text", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenVINO 对话失败");
            return $"[OpenVINO 对话失败: {ex.Message}]";
        }
    }

    private static string BuildPrompt(string message, string? systemPrompt, List<(string Role, string Content)>? history)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.Append("<|im_start|>system\n").Append(systemPrompt).Append("\n<|im_end|>\n");
        }
        if (history != null)
        {
            foreach (var (role, content) in history)
            {
                var r = role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                sb.Append("<|im_start|>").Append(r).Append('\n').Append(content).Append("\n<|im_end|>\n");
            }
        }
        sb.Append("<|im_start|>user\n").Append(message).Append("\n<|im_end|>\n<|im_start|>assistant\n");
        return sb.ToString();
    }
}
