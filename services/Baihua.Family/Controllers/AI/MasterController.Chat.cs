using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Contracts.Master;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

/// <summary>
/// 师父对话和消息处理
/// </summary>
public partial class MasterController
{
    /// <summary>
    /// AI 流式对话（SSE 事件流）
    /// </summary>
    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] MasterChatRequest request)
    {
        var httpResponse = HttpContext.Response;
        httpResponse.ContentType = "text/event-stream";
        httpResponse.Headers["Cache-Control"] = "no-cache";
        httpResponse.Headers["X-Accel-Buffering"] = "no";

        async Task SendSse(string eventType, string data)
        {
            await httpResponse.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
            await httpResponse.Body.FlushAsync();
        }

        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                await SendSse("error", "消息不能为空");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.MasterId))
            {
                await SendSse("error", "师父ID不能为空");
                return;
            }

            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == request.MasterId);
            if (master == null)
            {
                await SendSse("error", "师父不存在");
                return;
            }

            var currentStage = string.IsNullOrEmpty(request.Stage) ? master.CurrentStage : request.Stage;

            var profile = await db.ApprenticeProfiles.FirstOrDefaultAsync(p => p.MasterId == request.MasterId);
            var coreProfile = profile != null
                ? $"基础：{profile.Foundation ?? "未知"}；学习风格：{profile.LearningStyle ?? "未知"}；优势：{profile.Strengths ?? "未知"}；薄弱：{profile.Weaknesses ?? "未知"}"
                : null;

            var stageSummaryEntity = await db.StageSummaries
                .Where(s => s.MasterId == request.MasterId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
            var stageSummary = stageSummaryEntity?.Summary;

            var outline = _promptBuilder.MatchExamOutline(master.Goal, master.Industry);
            var outlineContext = _promptBuilder.GetOutlineContext(outline, currentStage);
            var combinedSummary = new List<string>();
            if (!string.IsNullOrEmpty(stageSummary)) combinedSummary.Add(stageSummary);
            if (!string.IsNullOrEmpty(outlineContext)) combinedSummary.Add(outlineContext);
            var finalSummary = combinedSummary.Count > 0 ? string.Join("\n\n", combinedSummary) : null;

            var (provider, model) = ResolveProviderAndModel(null, null);

            await SendSse("meta", System.Text.Json.JsonSerializer.Serialize(new { provider = provider.Name, model, masterId = request.MasterId, stage = currentStage }));

            if (_promptBuilder.ContainsBlockedContent(request.Message))
            {
                var refusal = _promptBuilder.BuildSafetyRefusal();
                await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = refusal }));
                await SendSse("done", "");
                return;
            }

            var messages = _promptBuilder.BuildMessages(
                goal: master.Goal,
                industry: master.Industry,
                masterName: master.MasterName,
                currentStage: currentStage,
                coreProfile: coreProfile,
                stageSummary: finalSummary,
                recentHistory: request.History,
                userMessage: request.Message);

            var vaultContext = await BuildVaultContextAsync(db, request.MasterId, request.Message);
            if (!string.IsNullOrEmpty(vaultContext))
            {
                var lastUserIndex = messages.FindLastIndex(m => m.Role == ChatRole.User);
                if (lastUserIndex >= 0)
                {
                    messages[lastUserIndex] = new ChatMessage(ChatRole.User,
                        $"{vaultContext}\n\n---\n\n{request.Message}");
                    await SendSse("vault", System.Text.Json.JsonSerializer.Serialize(new { enriched = true }));
                }
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_aiSettings.AiRequestTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var chatOptions = AiClientService.BuildChatOptions(temperature: 0.7f, maxOutputTokens: 2000);
            var client = _aiClientService.CreateChatClient(provider, model);

            var fullResponse = new System.Text.StringBuilder();

            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, linkedCts.Token))
            {
                var text = update.Text
                    ?? update.Contents.OfType<TextContent>().FirstOrDefault()?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    fullResponse.Append(text);
                    await SendSse("delta", System.Text.Json.JsonSerializer.Serialize(new { content = text }));
                }
            }

            await SendSse("done", "");

            db.MasterConversations.Add(new MasterConversation
            {
                MasterId = request.MasterId,
                Role = "user",
                Content = request.Message,
                Stage = currentStage
            });
            db.MasterConversations.Add(new MasterConversation
            {
                MasterId = request.MasterId,
                Role = "assistant",
                Content = fullResponse.ToString(),
                Stage = currentStage
            });
            await db.SaveChangesAsync();
        }
        catch (OperationCanceledException)
        {
            await SendSse("error", "AI 调用超时或已被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "师父对话失败");
            await SendSse("error", $"对话失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取对话历史
    /// </summary>
    [HttpGet("{id}/conversations")]
    public async Task<ActionResult<ConversationHistoryResponse>> GetConversations(string id, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new ConversationHistoryResponse { Success = false, Message = "师父ID不能为空" });

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new ConversationHistoryResponse { Success = false, Message = "师父不存在" });

            var conversations = await db.MasterConversations
                .Where(c => c.MasterId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(limit)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new ConversationHistoryItem
                {
                    Role = c.Role,
                    Content = c.Content,
                    Stage = c.Stage,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return Ok(new ConversationHistoryResponse
            {
                Success = true,
                Message = "获取对话历史成功",
                Items = conversations
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取对话历史失败");
            return StatusCode(500, new ConversationHistoryResponse { Success = false, Message = $"获取失败：{ex.Message}" });
        }
    }

    /// <summary>
    /// 同步对话记录（从客户端推送历史对话）
    /// </summary>
    [HttpPost("{id}/conversations/sync")]
    public async Task<ActionResult<ConversationSyncResponse>> SyncConversations(string id, [FromBody] ConversationSyncRequest request)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new ConversationSyncResponse { Success = false, Message = "师父ID不能为空" });

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new ConversationSyncResponse { Success = false, Message = "师父不存在" });

            var syncedCount = 0;
            foreach (var item in request.Items)
            {
                db.MasterConversations.Add(new MasterConversation
                {
                    MasterId = id,
                    Role = item.Role,
                    Content = item.Content,
                    Stage = item.Stage,
                    CreatedAt = item.CreatedAt
                });
                syncedCount++;
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("师父 {MasterId} 对话同步：新增 {Synced} 条", id, syncedCount);

            return Ok(new ConversationSyncResponse
            {
                Success = true,
                Message = $"同步成功，新增 {syncedCount} 条对话",
                SyncedCount = syncedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "对话同步失败");
            return StatusCode(500, new ConversationSyncResponse { Success = false, Message = $"同步失败：{ex.Message}" });
        }
    }
}
