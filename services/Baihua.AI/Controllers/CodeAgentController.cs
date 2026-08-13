using Baihua.AI.Services;
using Baihua.Contracts.Ai;
using Baihua.Core.Localization;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

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
    private readonly IDbContextFactory<AIDbContext> _aiDbFactory;

    public CodeAgentController(
        CodeAgentService codeAgent,
        AiSettingsService aiSettings,
        ILogger<CodeAgentController> logger,
        IStringLocalizer<SharedResources> loc,
        IDbContextFactory<AIDbContext> aiDbFactory)
    {
        _codeAgent = codeAgent;
        _aiSettings = aiSettings;
        _logger = logger;
        _loc = loc;
        _aiDbFactory = aiDbFactory;
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
            var agent = _codeAgent.CreateAgent(providerId, model, request.ToolMode, enableCompaction: true);

            var messages = new List<ChatMessage>
            {
                CodeAgentService.BuildUserMessage(request.Prompt, request.Language, request.Context)
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            // 会话模式：SessionId 命中历史记录时恢复上下文（含消息历史与压缩状态）
            var sessionStateJson = await LoadSessionStateJsonAsync(request.SessionId);
            var session = await CodeAgentService.ResolveSessionAsync(agent, sessionStateJson);
            session ??= await agent.CreateSessionAsync();   // 首次生成：创建会话（MAF 累积消息历史，供续聊与压缩）
            var savedSessionId = request.SessionId;

            var sw = Stopwatch.StartNew();
            var agentError = (string?)null;
            long? inTokens = null;
            long? outTokens = null;
            try
            {
                var result = await agent.RunAsync(messages, session: session, options: null, linkedCts.Token);
                inTokens = result.Usage?.InputTokenCount;
                outTokens = result.Usage?.OutputTokenCount;
                var output = result.ToString() ?? "";

                var (code, fileName) = CodeAgentService.ExtractCode(output);
                var newSessionState = await CodeAgentService.SerializeSessionAsync(agent, session);

                // 会话模式：把更新后的会话状态（含新消息历史）持久化到对应历史记录
                if (savedSessionId is int sid)
                {
                    await UpdateSessionStateAsync(sid, newSessionState, output, fileName);
                }

                return Ok(new CodeAgentResponse
                {
                    Success = true,
                    Message = _loc["Ai_Chat_ReplySuccess"].Value,
                    Code = string.IsNullOrWhiteSpace(code) ? output : code,
                    FileName = fileName,
                    ProviderId = providerId,
                    Model = model,
                    SessionId = savedSessionId,
                    SessionStateJson = newSessionState
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
        long? streamInTokens = null;
        long? streamOutTokens = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                await SendSse("error", _loc["AiChat_MessageEmpty"].Value);
                return;
            }

            var (providerId, model) = ResolveProviderAndModel(request.ProviderId, request.Model);
            resolved = (providerId, model);
            var agent = _codeAgent.CreateAgent(providerId, model, request.ToolMode, enableCompaction: true);

            await SendSse("meta", System.Text.Json.JsonSerializer.Serialize(new { providerId, model }));

            var messages = new List<ChatMessage>
            {
                CodeAgentService.BuildUserMessage(request.Prompt, request.Language, request.Context)
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            // 会话模式：恢复上下文（含压缩状态），流式累积完整输出后持久化
            var sessionStateJson = await LoadSessionStateJsonAsync(request.SessionId);
            var session = await CodeAgentService.ResolveSessionAsync(agent, sessionStateJson);
            session ??= await agent.CreateSessionAsync();   // 首次生成：创建会话（MAF 累积消息历史，供续聊与压缩）
            var fullOutput = new System.Text.StringBuilder();

            // 工具调用展示：跟踪 callId -> 工具名，把工具调用/结果作为 SSE 事件推给前端
            var toolNames = new Dictionary<string, string>();
            await foreach (var update in agent.RunStreamingAsync(messages, session: session, options: null, linkedCts.Token))
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
                                fullOutput.Append(t);
                                await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = t }));
                            }
                            break;
                        }
                        case Microsoft.Extensions.AI.UsageContent usage:
                        {
                            // 流式 update 携带 token 用量（供性能监控页 TPS 指标）
                            if (usage.Details is { } ud)
                            {
                                streamInTokens = ud.InputTokenCount ?? streamInTokens;
                                streamOutTokens = ud.OutputTokenCount ?? streamOutTokens;
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

            // 会话持久化：续聊更新原记录；首次生成自动建档（含会话状态），done 返回新 id
            var finalState = await CodeAgentService.SerializeSessionAsync(agent, session);
            var (_, fname) = CodeAgentService.ExtractCode(fullOutput.ToString());
            int? savedId = request.SessionId;
            if (savedId is int sid)
            {
                await UpdateSessionStateAsync(sid, finalState, fullOutput.ToString(), fname);
            }
            else if (fullOutput.Length > 0 && !string.IsNullOrWhiteSpace(request.Prompt))
            {
                savedId = await CreateSessionRecordAsync(request, fullOutput.ToString(), finalState, fname);
            }

            await SendSse("done", System.Text.Json.JsonSerializer.Serialize(new { sessionId = savedId }));
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
                    streamInTokens, streamOutTokens, error);
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
                request.Context, request.SkipResearch, request.SkipReview, request.PlanModel, request.ReviewModel, linkedCts.Token);
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
                               request.Context, request.SkipResearch, request.SkipReview, request.PlanModel, request.ReviewModel, linkedCts.Token))
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

    #region 会话历史

    /// <summary>读取历史记录的会话状态 JSON。</summary>
    private async Task<string?> LoadSessionStateJsonAsync(int? sessionId)
    {
        if (sessionId is not int id) return null;
        try
        {
            await using var db = await _aiDbFactory.CreateDbContextAsync();
            var s = await db.CodeAgentSessions.FindAsync(id);
            return s?.SessionStateJson;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取会话状态失败");
            return null;
        }
    }

    /// <summary>更新历史记录的会话状态与输出（继续对话后）。</summary>
    private async Task UpdateSessionStateAsync(int id, string? sessionStateJson, string? output, string? fileName)
    {
        try
        {
            await using var db = await _aiDbFactory.CreateDbContextAsync();
            var s = await db.CodeAgentSessions.FindAsync(id);
            if (s is null) return;
            s.SessionStateJson = sessionStateJson;
            if (!string.IsNullOrWhiteSpace(output))
            {
                if (s.IsPipeline) { s.Code = output; }
                else { s.Output = output; }
                s.FileName = fileName ?? s.FileName;
            }
            s.CreatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "更新会话状态失败");
        }
    }

    /// <summary>首次生成自动建档（流式端点）：含会话状态与输出，返回新记录 Id。</summary>
    private async Task<int> CreateSessionRecordAsync(CodeAgentRequest request, string output, string? sessionStateJson, string? fileName)
    {
        await using var db = await _aiDbFactory.CreateDbContextAsync();
        var entity = new CodeAgentSession
        {
            Prompt = request.Prompt.Trim(),
            Language = request.Language,
            ProviderId = request.ProviderId,
            Model = request.Model,
            ToolMode = request.ToolMode.ToString(),
            IsPipeline = false,
            Output = output,
            FileName = fileName,
            SessionStateJson = sessionStateJson
        };
        db.CodeAgentSessions.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    /// <summary>
    /// 保存一次生成记录（由前端在生成完成后调用，含完整输出）。
    /// </summary>
    [HttpPost("history")]
    public async Task<ActionResult<object>> SaveSession([FromBody] CodeAgentSessionSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "prompt required" });

        await using var db = await _aiDbFactory.CreateDbContextAsync();
        var entity = new CodeAgentSession
        {
            Prompt = request.Prompt.Trim(),
            Language = request.Language,
            ProviderId = request.ProviderId,
            Model = request.Model,
            ToolMode = string.IsNullOrWhiteSpace(request.ToolMode) ? "All" : request.ToolMode!,
            IsPipeline = request.IsPipeline,
            PlanPro = request.PlanPro,
            Output = request.Output,
            Research = request.Research,
            Code = request.Code,
            Review = request.Review,
            FileName = request.FileName,
            SessionStateJson = request.SessionStateJson
        };
        db.CodeAgentSessions.Add(entity);
        await db.SaveChangesAsync();
        return Ok(new { id = entity.Id });
    }

    /// <summary>
    /// 历史列表（倒序，不含大文本）。
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<CodeAgentSessionSummaryDto>>> GetSessions([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var db = await _aiDbFactory.CreateDbContextAsync();
        var raw = await db.CodeAgentSessions
            .OrderByDescending(s => s.Id)
            .Take(limit)
            .Select(s => new
            {
                s.Id, s.CreatedAt, s.Prompt, s.Language, s.ProviderId, s.Model, s.ToolMode,
                s.IsPipeline, s.PlanPro, s.FileName,
                OutLen = s.Output != null ? s.Output.Length : 0,
                ResLen = s.Research != null ? s.Research.Length : 0,
                CodeLen = s.Code != null ? s.Code.Length : 0,
                RevLen = s.Review != null ? s.Review.Length : 0
            })
            .ToListAsync();
        var items = raw.Select(s => new CodeAgentSessionSummaryDto
        {
            Id = s.Id,
            CreatedAt = s.CreatedAt,
            Prompt = s.Prompt,
            Language = s.Language,
            ProviderId = s.ProviderId,
            Model = s.Model,
            ToolMode = s.ToolMode,
            IsPipeline = s.IsPipeline,
            PlanPro = s.PlanPro,
            FileName = s.FileName,
            OutputLength = s.OutLen + s.ResLen + s.CodeLen + s.RevLen
        }).ToList();
        return Ok(items);
    }

    /// <summary>
    /// 历史详情（含完整输出）。
    /// </summary>
    [HttpGet("history/{id:int}")]
    public async Task<ActionResult<CodeAgentSessionDetailDto>> GetSession(int id)
    {
        await using var db = await _aiDbFactory.CreateDbContextAsync();
        var s = await db.CodeAgentSessions.FindAsync(id);
        if (s is null) return NotFound();
        return Ok(new CodeAgentSessionDetailDto
        {
            Id = s.Id,
            CreatedAt = s.CreatedAt,
            Prompt = s.Prompt,
            Language = s.Language,
            ProviderId = s.ProviderId,
            Model = s.Model,
            ToolMode = s.ToolMode,
            IsPipeline = s.IsPipeline,
            PlanPro = s.PlanPro,
            FileName = s.FileName,
            Output = s.Output,
            Research = s.Research,
            Code = s.Code,
            Review = s.Review
        });
    }

    /// <summary>
    /// 删除一条历史记录。
    /// </summary>
    [HttpDelete("history/{id:int}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        await using var db = await _aiDbFactory.CreateDbContextAsync();
        var s = await db.CodeAgentSessions.FindAsync(id);
        if (s is null) return NotFound();
        db.CodeAgentSessions.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    #endregion
}




