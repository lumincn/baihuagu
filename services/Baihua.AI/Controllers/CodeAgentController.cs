using Baihua.AI.Services;
using Baihua.Contracts.Ai;
using Baihua.Core.Localization;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;

namespace Baihua.AI.Controllers;

/// <summary>
/// 编程 Agent 控制器：基于 Microsoft Agent Framework (MAF) 的代码生成。
/// 供 WebUI 编程页面调用，默认使用本地编程模型（OpenVINO qwen2.5-coder）。
/// </summary>
[ApiController]
[Route("api/ai/code")]
public class CodeAgentController : ControllerBase
{
    private readonly CodeAgentService _codeAgent;
    private readonly AiSettingsService _aiSettings;
    private readonly ILogger<CodeAgentController> _logger;
    private readonly IStringLocalizer<SharedResources> _loc;

    public CodeAgentController(
        CodeAgentService codeAgent,
        AiSettingsService aiSettings,
        ILogger<CodeAgentController> logger,
        IStringLocalizer<SharedResources> loc)
    {
        _codeAgent = codeAgent;
        _aiSettings = aiSettings;
        _logger = logger;
        _loc = loc;
    }

    /// <summary>
    /// 编程 Agent：非流式，一次性返回生成代码。
    /// </summary>
    [HttpPost("agent")]
    public async Task<ActionResult<CodeAgentResponse>> RunAgent([FromBody] CodeAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = _loc["AiChat_MessageEmpty"].Value });

        try
        {
            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            var agent = _codeAgent.CreateAgent(providerId, model);

            var messages = new List<ChatMessage>
            {
                CodeAgentService.BuildUserMessage(request.Prompt, request.Language, request.Context)
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var result = await agent.RunAsync(messages, session: null, options: null, linkedCts.Token);
            var output = result.ToString() ?? "";

            var (code, fileName) = CodeAgentService.ExtractCode(output);
            return Ok(new CodeAgentResponse
            {
                Success = true,
                Message = _loc["Ai_Chat_ReplySuccess"].Value,
                Code = string.IsNullOrWhiteSpace(code) ? output : code,
                FileName = fileName,
                ProviderId = providerId,
                Model = model
            });
        }
        catch (OperationCanceledException)
        {
            return Ok(new CodeAgentResponse { Success = false, Message = _loc["AiChat_Timeout"].Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "编程 Agent 执行失败");
            return Ok(new CodeAgentResponse { Success = false, Message = _loc["Ai_Chat_Failed", ex.Message].Value });
        }
    }

    /// <summary>
    /// 编程 Agent：流式（SSE），逐步返回生成内容。
    /// </summary>
    [HttpPost("agent/stream")]
    public async Task StreamAgent([FromBody] CodeAgentRequest request)
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

        try
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                await SendSse("error", _loc["AiChat_MessageEmpty"].Value);
                return;
            }

            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            var agent = _codeAgent.CreateAgent(providerId, model);

            await SendSse("meta", System.Text.Json.JsonSerializer.Serialize(new { providerId, model }));

            var messages = new List<ChatMessage>
            {
                CodeAgentService.BuildUserMessage(request.Prompt, request.Language, request.Context)
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            // 工具调用展示：跟踪 callId -> 工具名，把工具调用/结果作为 SSE 事件推给前端
            var toolNames = new Dictionary<string, string>();
            await foreach (var update in agent.RunStreamingAsync(messages, session: null, options: null, linkedCts.Token))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case Microsoft.Extensions.AI.TextContent text:
                        {
                            var t = text.Text;
                            if (!string.IsNullOrEmpty(t))
                            {
                                await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = t }));
                            }
                            break;
                        }
                        case Microsoft.Extensions.AI.FunctionCallContent fc:
                        {
                            if (!string.IsNullOrEmpty(fc.CallId)) toolNames[fc.CallId] = fc.Name ?? "";
                            await SendSse("tool", System.Text.Json.JsonSerializer.Serialize(new
                            {
                                kind = "call",
                                name = fc.Name ?? "",
                                detail = Truncate(fc.Arguments is null ? "" : System.Text.Json.JsonSerializer.Serialize(fc.Arguments), 300)
                            }));
                            break;
                        }
                        case Microsoft.Extensions.AI.FunctionResultContent fr:
                        {
                            toolNames.TryGetValue(fr.CallId ?? "", out var name);
                            await SendSse("tool", System.Text.Json.JsonSerializer.Serialize(new
                            {
                                kind = "result",
                                name = name ?? fr.CallId ?? "",
                                detail = Truncate(fr.Result?.ToString() ?? "", 200)
                            }));
                            break;
                        }
                    }
                }
            }

            await SendSse("done", "");
        }
        catch (OperationCanceledException)
        {
            await SendSse("error", _loc["AiChat_Timeout"].Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "编程 Agent 流式执行失败");
            await SendSse("error", _loc["Ai_Chat_Failed", ex.Message].Value);
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");

    private (string ProviderId, string Model) ResolveProviderAndModel(string? providerId, string? model)
    {
        var providers = _aiSettings.GetAiProviders();

        // 优先显式指定；其次找名称/ID 含 "coder" 的本地编程模型；最后主提供方
        var provider = !string.IsNullOrEmpty(providerId)
            ? providers.FirstOrDefault(p => p.Id == providerId)
            : providers.FirstOrDefault(p =>
                  p.Id?.Contains("coder", StringComparison.OrdinalIgnoreCase) == true
                  || p.Name?.Contains("coder", StringComparison.OrdinalIgnoreCase) == true)
              ?? providers.FirstOrDefault(p => p.IsMain)
              ?? providers.FirstOrDefault();

        if (provider == null)
            throw new InvalidOperationException(_loc["AiChat_ProviderNotFound"].Value);

        var resolvedModel = !string.IsNullOrEmpty(model)
            ? model
            : provider.GetMainModel();
        if (string.IsNullOrEmpty(resolvedModel))
            resolvedModel = provider.GetModelOptions().FirstOrDefault()?.Name ?? "";

        return (provider.Id, resolvedModel);
    }
}
