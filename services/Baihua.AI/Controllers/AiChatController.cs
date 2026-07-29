using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Models;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

/// <summary>
/// 纯 AI 聊天控制器（无 RAG / 无记忆 / 无 Function Calling）
/// 供 Family 编排层通过 HTTP 调用，实现 AI 计算与业务逻辑的解耦。
/// </summary>
[ApiController]
[Route("api/ai/chat")]
public class AiChatController : ControllerBase
{
    private readonly AiSettingsService _aiSettings;
    private readonly AiClientService _aiClientService;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(
        AiSettingsService aiSettings,
        AiClientService aiClientService,
        ILogger<AiChatController> logger)
    {
        _aiSettings = aiSettings;
        _aiClientService = aiClientService;
        _logger = logger;
    }

    /// <summary>
    /// 非流式 AI 聊天
    /// </summary>
    [HttpPost("completion")]
    public async Task<ActionResult<TaskRunner.Contracts.Ai.ChatResponse>> Completion([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "消息不能为空" });

        try
        {
            var (provider, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            var messages = BuildMessages(request.History, request.Message);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, messages, AiClientService.BuildChatOptions(), HttpContext.RequestAborted);
            stopwatch.Stop();

            return Ok(new TaskRunner.Contracts.Ai.ChatResponse
            {
                Success = true,
                Message = "回复成功",
                Reply = result.Text ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 聊天失败");
            return Ok(new TaskRunner.Contracts.Ai.ChatResponse
            {
                Success = false,
                Message = $"聊天失败：{ex.Message}"
            });
        }
    }

    /// <summary>
    /// 流式 AI 聊天（SSE）
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatRequest request)
    {
        var response = HttpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        async Task SendSse(string eventType, string data)
        {
            await response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
            await response.Body.FlushAsync();
        }

        AiProviderConfig? provider = null;
        string model = "";

        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                await SendSse("error", "消息不能为空");
                return;
            }

            (provider, model) = ResolveProviderAndModel(request.ProviderId, request.Model);

            var ready = await _aiClientService.EnsureProviderReadyAsync(provider);
            if (!ready && IsLocalProvider(provider))
            {
                await SendSse("error", $"本地 AI 服务 {provider.Name} 未运行且自动启动失败，请手动启动后重试。");
                return;
            }

            await SendSse("meta", System.Text.Json.JsonSerializer.Serialize(new { provider = provider.Name, model }));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var messages = BuildMessages(request.History, request.Message);
            var options = AiClientService.BuildChatOptions();

            var client = _aiClientService.CreateChatClient(provider, model);
            await foreach (var update in client.GetStreamingResponseAsync(messages, options, linkedCts.Token))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = text }));
                }
            }

            await SendSse("done", "");
        }
        catch (OperationCanceledException)
        {
            await SendSse("error", "AI 调用超时或已被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 流式聊天失败");
            await SendSse("error", $"聊天失败：{ex.Message}");
        }
    }

    private (AiProviderConfig Provider, string Model) ResolveProviderAndModel(string? providerId, string? model)
    {
        var providers = _aiSettings.GetAiProviders();
        var provider = string.IsNullOrEmpty(providerId)
            ? (string.IsNullOrEmpty(model)
                ? providers.FirstOrDefault(p => p.IsMain) ?? providers.FirstOrDefault()
                : providers.FirstOrDefault(p =>
                    p.Models.Any(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase))
                  ) ?? providers.FirstOrDefault(p => p.IsMain) ?? providers.FirstOrDefault())
            : providers.FirstOrDefault(p => p.Id == providerId);

        if (provider == null)
            throw new Exception("未找到指定的 AI 提供商");

        var modelOptions = provider.GetModelOptions();
        var resolvedModel = !string.IsNullOrEmpty(model)
            ? model
            : modelOptions.FirstOrDefault(m => m.IsMain)?.Name
              ?? modelOptions.FirstOrDefault()?.Name
              ?? "Qwen/Qwen2.5-14B-Instruct";

        return (provider, resolvedModel);
    }

    private static List<ChatMessage> BuildMessages(List<ChatHistoryItem>? history, string currentMessage)
    {
        var messages = new List<ChatMessage>();

        if (history != null)
        {
            foreach (var item in history)
            {
                var role = item.Role?.ToLowerInvariant() switch
                {
                    "assistant" => ChatRole.Assistant,
                    "system" => ChatRole.System,
                    _ => ChatRole.User
                };
                messages.Add(new ChatMessage(role, item.Content));
            }
        }

        messages.Add(new ChatMessage(ChatRole.User, currentMessage));
        return messages;
    }

    private static bool IsLocalProvider(AiProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider?.AiBaseUrl))
            return false;
        var url = provider.AiBaseUrl.ToLowerInvariant();
        return url.Contains("localhost") || url.Contains("127.0.0.1") || url.Contains("0.0.0.0");
    }
}
