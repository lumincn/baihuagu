using Baihua.AI.Services;
using Baihua.Contracts.Ai;
using Baihua.Core.Localization;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using System.Diagnostics;

namespace Baihua.AI.Controllers;

/// <summary>
/// 编程 Agent 控制器：基于 Microsoft Agent Framework (MAF) 的代码生成。
/// 供 WebUI 编程页面调用，默认使用本地编程模型（OpenVINO qwen2.5-coder）。
/// </summary>
[ApiController]
[Route("api/ai/code")]
public class CodeAgentController : ControllerBase
{
    /// <summary>CodeAgent 观测：trace 源（与 service 同源，OpenObserve 里按此过滤）</summary>
    private static readonly ActivitySource CodeAgentActivity = new("Baihua.AI.CodeAgent");

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
            var agent = _codeAgent.CreateAgent(providerId, model, request.ToolMode);

            var messages = new List<ChatMessage>
            {
                CodeAgentService.BuildUserMessage(request.Prompt, request.Language, request.Context)
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var sw = Stopwatch.StartNew();
            var agentError = (string?)null;
            long? inTokens = null;
            long? outTokens = null;
            try
            {
                var result = await agent.RunAsync(messages, session: null, options: null, linkedCts.Token);
                inTokens = result.Usage?.InputTokenCount;
                outTokens = result.Usage?.OutputTokenCount;
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                agentError = ex.Message;
                throw;
            }
            finally
            {
                sw.Stop();
                using var act = CodeAgentActivity.StartActivity("AgentRun");
                act?.SetTag("provider", providerId);
                act?.SetTag("model", model);
                act?.SetStatus(agentError == null ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                await _codeAgent.RecordUsageAsync(providerId, model, "codeagent", sw.ElapsedMilliseconds,
                    inTokens, outTokens, agentError);
            }
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

        var sw = Stopwatch.StartNew();
        var error = (string?)null;
        (string ProviderId, string Model)? resolved = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                await SendSse("error", _loc["AiChat_MessageEmpty"].Value);
                return;
            }

            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            resolved = (providerId, model);
            var agent = _codeAgent.CreateAgent(providerId, model, request.ToolMode);

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
            error = _loc["AiChat_Timeout"].Value;
            await SendSse("error", _loc["AiChat_Timeout"].Value);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "编程 Agent 流式执行失败");
            await SendSse("error", _loc["Ai_Chat_Failed", ex.Message].Value);
        }
        finally
        {
            sw.Stop();
            if (resolved is { } r)
            {
                using var act = CodeAgentActivity.StartActivity("AgentRunStreaming");
                act?.SetTag("provider", r.ProviderId);
                act?.SetTag("model", r.Model);
                act?.SetStatus(error == null ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                await _codeAgent.RecordUsageAsync(r.ProviderId, r.Model, "codeagent-stream", sw.ElapsedMilliseconds,
                    null, null, error);
            }
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");

    /// <summary>
    /// 编程 Agent 流水线（调研→编码→审查）：非流式，返回各阶段结果。
    /// </summary>
    [HttpPost("pipeline")]
    public async Task<ActionResult<CodeAgentPipelineResponse>> RunPipeline([FromBody] CodeAgentPipelineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = _loc["AiChat_MessageEmpty"].Value });

        try
        {
            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes * 3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var result = await _codeAgent.RunPipelineAsync(providerId, model, request.Prompt, request.Language,
                request.Context, request.SkipResearch, request.SkipReview, linkedCts.Token);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return Ok(new CodeAgentPipelineResponse { Success = false, Message = _loc["AiChat_Timeout"].Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "编程 Agent 流水线执行失败");
            return Ok(new CodeAgentPipelineResponse { Success = false, Message = _loc["Ai_Chat_Failed", ex.Message].Value });
        }
    }

    /// <summary>
    /// 编程 Agent 流水线（调研→编码→审查）：SSE 流式，按阶段推送。
    /// </summary>
    [HttpPost("pipeline/stream")]
    public async Task StreamPipeline([FromBody] CodeAgentPipelineRequest request)
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

        var sw = Stopwatch.StartNew();
        var error = (string?)null;
        (string ProviderId, string Model)? resolved = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                await SendSse("error", _loc["AiChat_MessageEmpty"].Value);
                return;
            }

            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            resolved = (providerId, model);
            await SendSse("meta", System.Text.Json.JsonSerializer.Serialize(new { providerId, model, pipeline = true }));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes * 3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            await foreach (var update in _codeAgent.RunPipelineStreamingAsync(providerId, model, request.Prompt, request.Language,
                               request.Context, request.SkipResearch, request.SkipReview, linkedCts.Token))
            {
                switch (update)
                {
                    case StageUpdate stage:
                        await SendSse("stage", System.Text.Json.JsonSerializer.Serialize(new { name = stage.Name }));
                        break;
                    case DeltaUpdate delta:
                        await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = delta.Text }));
                        break;
                    case ToolCallUpdate tc:
                        await SendSse("tool", System.Text.Json.JsonSerializer.Serialize(new { kind = "call", name = tc.Name, detail = tc.Detail }));
                        break;
                    case ToolResultUpdate tr:
                        await SendSse("tool", System.Text.Json.JsonSerializer.Serialize(new { kind = "result", name = tr.Name, detail = tr.Detail }));
                        break;
                }
            }

            await SendSse("done", "");
        }
        catch (OperationCanceledException)
        {
            error = _loc["AiChat_Timeout"].Value;
            await SendSse("error", _loc["AiChat_Timeout"].Value);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "编程 Agent 流水线流式执行失败");
            await SendSse("error", _loc["Ai_Chat_Failed", ex.Message].Value);
        }
        finally
        {
            sw.Stop();
            if (resolved is { } r)
            {
                await _codeAgent.RecordUsageAsync(r.ProviderId, r.Model, "codeagent-pipeline-stream", sw.ElapsedMilliseconds,
                    null, null, error);
            }
        }
    }

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
