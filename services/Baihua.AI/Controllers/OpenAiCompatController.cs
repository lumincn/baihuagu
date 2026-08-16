using System.Text.Json;
using System.Text.Json.Serialization;
using Baihua.Core.Models;
using Baihua.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace Baihua.Family.Controllers;

/// <summary>
/// OpenAI 兼容推理端点（/mg/ai/v1/*）。
/// 供局域网内其他百花服务器的算力池自动注册为本机 AI 提供方后跨机调用：
/// 本机 AiClientService 用 OpenAI 协议请求 /mg/ai/v1/chat/completions，
/// 本端点按模型名路由到本机配置的 AI 提供方（含本地 Ollama/llama.cpp/OpenVINO）。
/// 鉴权：Authorization: Bearer 与 BAIHUA_AI_EXTERNAL_TOKEN 比对（未配置则局域网内免鉴权）。
/// </summary>
[ApiController]
[Route("mg/ai/v1")]
public class OpenAiCompatController : ControllerBase
{
    private readonly AiSettingsService _aiSettings;
    private readonly AiClientService _aiClientService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiCompatController> _logger;

    public OpenAiCompatController(
        AiSettingsService aiSettings,
        AiClientService aiClientService,
        IConfiguration configuration,
        ILogger<OpenAiCompatController> logger)
    {
        _aiSettings = aiSettings;
        _aiClientService = aiClientService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>模型列表（OpenAI /v1/models 兼容）</summary>
    [HttpGet("models")]
    public ActionResult<object> ListModels()
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });

        var models = _aiSettings.GetAiProviders()
            .Where(p => p.Models is { Count: > 0 })
            .SelectMany(p => p.Models.Select(m => new { id = m.Name, @object = "model", owned_by = p.Id }))
            .ToList();
        return Ok(new { @object = "list", data = models });
    }

    /// <summary>聊天补全（OpenAI /v1/chat/completions 兼容，非流式 + 流式 SSE）</summary>
    [HttpPost("chat/completions")]
    public async Task ChatCompletions([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!Authorize())
        {
            Response.StatusCode = 401;
            await Response.WriteAsJsonAsync(new { error = new { message = "invalid token", type = "invalid_request_error" } });
            return;
        }

        try
        {
            var modelName = body.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
            var stream = body.TryGetProperty("stream", out var s) && s.GetBoolean();
            var messages = ParseMessages(body);

            if (messages.Count == 0)
            {
                await WriteErrorAsync("messages is required");
                return;
            }

            var (provider, resolvedModel) = ResolveProviderAndModel(modelName);
            if (provider == null)
            {
                await WriteErrorAsync($"no provider found for model {modelName}");
                return;
            }

            var ready = await _aiClientService.EnsureProviderReadyAsync(provider);
            if (!ready)
            {
                await WriteErrorAsync($"local model provider {provider.Name} is not running");
                return;
            }

            if (stream)
            {
                await StreamResponseAsync(provider, resolvedModel, messages, ct);
            }
            else
            {
                await NonStreamResponseAsync(provider, resolvedModel, messages, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI 兼容端点调用失败");
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsJsonAsync(new { error = new { message = ex.Message, type = "server_error" } });
            }
        }
    }

    private bool Authorize()
    {
        var expected = _configuration["BAIHUA_AI_EXTERNAL_TOKEN"] ?? "";
        if (string.IsNullOrEmpty(expected)) return true; // 未配置则局域网内信任
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var token = auth["Bearer ".Length..].Trim();
        return string.Equals(token, expected, StringComparison.Ordinal);
    }

    private static List<ChatMessage> ParseMessages(JsonElement body)
    {
        var list = new List<ChatMessage>();
        if (!body.TryGetProperty("messages", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            var role = item.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
            var content = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            list.Add(role.ToLowerInvariant() switch
            {
                "assistant" => new ChatMessage(ChatRole.Assistant, content),
                "system" => new ChatMessage(ChatRole.System, content),
                _ => new ChatMessage(ChatRole.User, content)
            });
        }
        return list;
    }

    private (AiProviderConfig? Provider, string Model) ResolveProviderAndModel(string modelName)
    {
        var providers = _aiSettings.GetAiProviders();
        AiProviderConfig? provider = null;
        if (!string.IsNullOrEmpty(modelName))
        {
            provider = providers.FirstOrDefault(p =>
                p.Models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)));
        }
        provider ??= providers.FirstOrDefault(p => p.IsMain) ?? providers.FirstOrDefault();

        var resolvedModel = !string.IsNullOrEmpty(modelName)
            ? modelName
            : provider?.GetMainModel() ?? "";

        return (provider, resolvedModel);
    }

    private async Task NonStreamResponseAsync(
        AiProviderConfig provider, string model, List<ChatMessage> messages, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _aiClientService.GetChatResponseWithAutoStartAsync(provider, model, messages, AiClientService.BuildChatOptions(), ct);
        sw.Stop();

        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}"[..24],
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = result.Text ?? "" },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = 0,
                completion_tokens = 0,
                total_tokens = 0,
                // 扩展字段：实测性能
                elapsed_ms = sw.ElapsedMilliseconds
            }
        });
    }

    private async Task StreamResponseAsync(
        AiProviderConfig provider, string model, List<ChatMessage> messages, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var client = _aiClientService.CreateChatClient(provider, model);
        await foreach (var update in client.GetStreamingResponseAsync(messages, AiClientService.BuildChatOptions(), linkedCts.Token))
        {
            var text = update.Text;
            if (string.IsNullOrEmpty(text)) continue;
            var chunk = new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}"[..24],
                @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model,
                choices = new[] { new { index = 0, delta = new { content = text }, finish_reason = (string?)null } }
            };
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n");
        await Response.Body.FlushAsync(ct);
    }

    private async Task WriteErrorAsync(string message)
    {
        Response.StatusCode = 400;
        await Response.WriteAsJsonAsync(new { error = new { message, type = "invalid_request_error" } });
    }
}
