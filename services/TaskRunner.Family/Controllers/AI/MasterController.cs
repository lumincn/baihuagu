using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Contracts.Master;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Models;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class MasterController : ControllerBase
{
    private readonly AiClientService _aiClientService;
    private readonly AiSettingsService _aiSettings;
    private readonly MasterPromptBuilder _promptBuilder;
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;
    private readonly ILogger<MasterController> _logger;

    public MasterController(
        AiClientService aiClientService,
        AiSettingsService aiSettings,
        MasterPromptBuilder promptBuilder,
        IDbContextFactory<FamilyDbContext> dbFactory,
        ILogger<MasterController> logger)
    {
        _aiClientService = aiClientService;
        _aiSettings = aiSettings;
        _promptBuilder = promptBuilder;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    [HttpPost("create")]
    public async Task<ActionResult<CreateMasterResponse>> Create([FromBody] CreateMasterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return BadRequest(new CreateMasterResponse { Success = false, Message = "目标不能为空" });
        if (string.IsNullOrWhiteSpace(request.Industry))
            return BadRequest(new CreateMasterResponse { Success = false, Message = "行业不能为空" });

        try
        {
            var masterId = Guid.NewGuid().ToString("N");
            var masterName = _promptBuilder.ResolveMasterName(request.Industry);
            var outline = _promptBuilder.MatchExamOutline(request.Goal, request.Industry);
            var stages = _promptBuilder.GetStagesForOutline(outline);

            var (provider, model) = ResolveProviderAndModel(null, null);

            var outlineContext = _promptBuilder.GetOutlineContext(outline, "入道");
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                request.Goal, request.Industry, masterName, "入道", null, outlineContext);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, $"我想{request.Goal}，请作为我的师父，先了解一下我的情况。")
            };

            var options = AiClientService.BuildChatOptions(temperature: 0.7f, maxOutputTokens: 500);
            var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, messages, options, HttpContext.RequestAborted, operation: "master-create");

            var greeting = response.Text ?? "欢迎，让我们开始你的学习之旅。";

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Masters.Add(new Master
            {
                MasterId = masterId,
                MasterName = masterName,
                Goal = request.Goal,
                Industry = request.Industry,
                CurrentStage = "入道",
                GraduatedStagesJson = "[]"
            });
            db.MasterConversations.Add(new MasterConversation
            {
                MasterId = masterId,
                Role = "assistant",
                Content = greeting,
                Stage = "入道"
            });
            await db.SaveChangesAsync();

            _logger.LogInformation("师父创建成功：{MasterName}（{Industry}），目标：{Goal}", masterName, request.Industry, request.Goal);

            return Ok(new CreateMasterResponse
            {
                Success = true,
                Message = greeting,
                MasterId = masterId,
                MasterName = masterName,
                Stages = stages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建师父失败");
            return StatusCode(500, new CreateMasterResponse { Success = false, Message = $"创建师父失败：{ex.Message}" });
        }
    }

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

    [HttpPost("{id}/stage-complete")]
    public async Task<ActionResult<StageCompleteResponse>> StageComplete(string id, [FromBody] StageCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new StageCompleteResponse { Success = false, Message = "师父ID不能为空" });

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new StageCompleteResponse { Success = false, Message = "师父不存在" });

            var stageOrder = new Dictionary<string, int>
            {
                ["入道"] = 1, ["筑基"] = 2, ["精进"] = 3, ["磨砺"] = 4, ["出师"] = 5
            };

            var currentOrder = stageOrder.GetValueOrDefault(request.StageName, 0);
            var nextStageName = stageOrder.FirstOrDefault(s => s.Value == currentOrder + 1).Key ?? "";

            var (provider, model) = ResolveProviderAndModel(null, null);

            var summaryPrompt = $"请为学徒在「{request.StageName}」阶段的学习生成一份简洁摘要（200字以内），包括：已掌握的知识点、仍需加强的方面、对下一阶段的建议。";
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一位学习评估专家，请简洁客观地总结学习成果。"),
                new(ChatRole.User, summaryPrompt)
            };

            var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 500);
            var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, messages, options, HttpContext.RequestAborted, operation: "master-stage-summary");

            var summary = response.Text ?? "";

            var graduated = System.Text.Json.JsonSerializer.Deserialize<List<string>>(master.GraduatedStagesJson) ?? new();
            if (!graduated.Contains(request.StageName))
                graduated.Add(request.StageName);
            master.GraduatedStagesJson = System.Text.Json.JsonSerializer.Serialize(graduated);
            master.CurrentStage = string.IsNullOrEmpty(nextStageName) ? master.CurrentStage : nextStageName;

            db.StageSummaries.Add(new StageSummary
            {
                MasterId = id,
                StageName = request.StageName,
                Summary = summary
            });

            await db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(nextStageName))
            {
                var focusedVaults = await db.VaultFocusStates
                    .Where(v => v.MasterId == id && v.State == "focused")
                    .ToListAsync();
                foreach (var v in focusedVaults)
                {
                    v.State = "archived";
                    v.UpdatedAt = DateTime.Now;
                }
                var discoveredVaults = await db.VaultFocusStates
                    .Where(v => v.MasterId == id && v.State == "discovered" && v.StageName == nextStageName)
                    .ToListAsync();
                foreach (var v in discoveredVaults)
                {
                    v.State = "focused";
                    v.UpdatedAt = DateTime.Now;
                }
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("师父 {MasterId} 阶段 {Stage} 完成，下一阶段：{Next}", id, request.StageName, nextStageName);

            return Ok(new StageCompleteResponse
            {
                Success = true,
                Message = $"阶段「{request.StageName}」已完成",
                NextStage = nextStageName,
                Summary = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "阶段完成处理失败");
            return StatusCode(500, new StageCompleteResponse { Success = false, Message = $"阶段完成处理失败：{ex.Message}" });
        }
    }

    [HttpGet("{id}/profile")]
    public async Task<ActionResult<ApprenticeProfileResponse>> GetProfile(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new ApprenticeProfileResponse { Success = false, Message = "师父ID不能为空" });

        await using var db = await _dbFactory.CreateDbContextAsync();
        var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
        if (master == null)
            return NotFound(new ApprenticeProfileResponse { Success = false, Message = "师父不存在" });

        var profile = await db.ApprenticeProfiles.FirstOrDefaultAsync(p => p.MasterId == id);
        var graduated = System.Text.Json.JsonSerializer.Deserialize<List<string>>(master.GraduatedStagesJson) ?? new();

        return Ok(new ApprenticeProfileResponse
        {
            Success = true,
            Message = "获取画像成功",
            MasterId = id,
            Goal = master.Goal,
            Foundation = profile?.Foundation,
            LearningStyle = profile?.LearningStyle,
            Strengths = profile?.Strengths,
            Weaknesses = profile?.Weaknesses,
            GraduatedStages = graduated,
            CurrentStage = master.CurrentStage,
            UpdatedAt = (profile?.UpdatedAt ?? master.UpdatedAt).ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    [HttpPost("{id}/assess")]
    public async Task<ActionResult<AssessResponse>> Assess(string id, [FromBody] AssessRequest request)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new AssessResponse { Success = false, Message = "师父ID不能为空" });

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new AssessResponse { Success = false, Message = "师父不存在" });

            var (provider, model) = ResolveProviderAndModel(null, null);

            var assessPrompt = request.Type switch
            {
                "daily" => "请出1-2道日常小测验题，评估学徒今日学习效果。",
                "weekly" => "请出10道综合题，评估学徒本周学习成果。",
                "stage" => "请出一份完整的阶段考核试卷，评估学徒是否可以进入下一阶段。",
                _ => "请对学徒进行综合能力评估，给出通过概率、薄弱环节和改进建议。"
            };

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一位严谨的考试评估专家。请客观评估学徒能力，给出具体的通过概率、薄弱环节和改进建议。以JSON格式返回：{\"report\": \"...\", \"passProbability\": 0.75, \"weakPoints\": [...], \"advice\": \"...\"}"),
                new(ChatRole.User, assessPrompt)
            };

            var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 1000);
            var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, messages, options, HttpContext.RequestAborted, operation: "master-assess");

            var result = response.Text ?? "";

            double passProbability = 0;
            var weakPoints = new List<string>();
            var advice = "";
            var report = result;

            try
            {
                var jsonStart = result.IndexOf('{');
                var jsonEnd = result.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = result.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("report", out var r))
                        report = r.GetString() ?? report;
                    if (doc.RootElement.TryGetProperty("passProbability", out var p))
                        passProbability = p.GetDouble();
                    if (doc.RootElement.TryGetProperty("weakPoints", out var w))
                        weakPoints = w.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                    if (doc.RootElement.TryGetProperty("advice", out var a))
                        advice = a.GetString() ?? "";
                }
            }
            catch { }

            db.ExamCheckpoints.Add(new ExamCheckpoint
            {
                MasterId = id,
                StageName = master.CurrentStage,
                Score = 0,
                PassProbability = passProbability,
                WeakPointsJson = System.Text.Json.JsonSerializer.Serialize(weakPoints),
                Advice = advice
            });
            await db.SaveChangesAsync();

            return Ok(new AssessResponse
            {
                Success = true,
                Message = "评估完成",
                Report = report,
                PassProbability = passProbability,
                WeakPoints = weakPoints,
                Advice = advice
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "能力评估失败");
            return StatusCode(500, new AssessResponse { Success = false, Message = $"能力评估失败：{ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<MasterListItem>>> List()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var masters = await db.Masters
            .Where(m => m.Status == "active")
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        var items = masters.Select(m => new MasterListItem
        {
            MasterId = m.MasterId,
            MasterName = m.MasterName,
            Goal = m.Goal,
            Industry = m.Industry,
            CurrentStage = m.CurrentStage,
            CurrentStageOrder = GetStageOrder(m.CurrentStage),
            GraduatedStages = System.Text.Json.JsonSerializer.Deserialize<List<string>>(m.GraduatedStagesJson) ?? new(),
            CreatedAt = m.CreatedAt
        }).ToList();

        return Ok(items);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
        if (master == null)
            return NotFound();

        master.Status = "deleted";
        await db.SaveChangesAsync();

        return Ok(new { Success = true });
    }

    [HttpPost("{id}/compress")]
    public async Task<ActionResult> Compress(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new { Success = false, Message = "师父不存在" });

            var graduated = System.Text.Json.JsonSerializer.Deserialize<List<string>>(master.GraduatedStagesJson) ?? new();
            var cutoff = DateTime.Now.AddDays(-7);

            var compressedCount = 0;
            foreach (var stage in graduated)
            {
                var existingSummary = await db.StageSummaries
                    .FirstOrDefaultAsync(s => s.MasterId == id && s.StageName == stage);
                if (existingSummary != null) continue;

                var conversations = await db.MasterConversations
                    .Where(c => c.MasterId == id && c.Stage == stage && c.CreatedAt < cutoff)
                    .OrderBy(c => c.CreatedAt)
                    .ToListAsync();

                if (conversations.Count == 0) continue;

                var (provider, model) = ResolveProviderAndModel(null, null);
                var convText = string.Join("\n", conversations.Select(c => $"{c.Role}: {c.Content}"));
                var summaryPrompt = $"请为以下对话生成简洁摘要（200字以内），提取关键知识点和学习要点：\n\n{TruncateText(convText, 3000)}";
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, "你是一位学习记录整理专家，请简洁客观地总结对话内容。"),
                    new(ChatRole.User, summaryPrompt)
                };
                var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 500);
                var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                    provider, model, messages, options, HttpContext.RequestAborted, operation: "master-compress");

                var summary = response.Text ?? "";

                db.StageSummaries.Add(new StageSummary
                {
                    MasterId = id,
                    StageName = stage,
                    Summary = summary
                });

                db.MasterConversations.RemoveRange(conversations);
                compressedCount++;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("师父 {MasterId} 压缩完成，处理 {Count} 个阶段", id, compressedCount);
            return Ok(new { Success = true, CompressedStages = compressedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据压缩失败");
            return StatusCode(500, new { Success = false, Message = $"压缩失败：{ex.Message}" });
        }
    }

    [HttpPost("{id}/evict")]
    public async Task<ActionResult> Evict(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var master = await db.Masters.FirstOrDefaultAsync(m => m.MasterId == id);
            if (master == null)
                return NotFound(new { Success = false, Message = "师父不存在" });

            var graduated = System.Text.Json.JsonSerializer.Deserialize<List<string>>(master.GraduatedStagesJson) ?? new();
            var cutoff = DateTime.Now.AddDays(-30);

            var evictedCount = 0;
            var profile = await db.ApprenticeProfiles.FirstOrDefaultAsync(p => p.MasterId == id);
            if (profile == null)
            {
                profile = new ApprenticeProfile { MasterId = id };
                db.ApprenticeProfiles.Add(profile);
            }

            foreach (var stage in graduated)
            {
                var summary = await db.StageSummaries
                    .FirstOrDefaultAsync(s => s.MasterId == id && s.StageName == stage);
                if (summary == null) continue;
                if (summary.CreatedAt > cutoff) continue;

                var (provider, model) = ResolveProviderAndModel(null, null);
                var profilePrompt = $"根据以下阶段学习摘要，提取学徒的核心能力画像（基础、学习风格、优势、薄弱点），以JSON格式返回：{{\"foundation\": \"...\", \"learningStyle\": \"...\", \"strengths\": \"...\", \"weaknesses\": \"...\"}}\n\n阶段：{stage}\n摘要：{summary.Summary}";
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, "你是一位学习评估专家，请客观提取学徒能力画像。"),
                    new(ChatRole.User, profilePrompt)
                };
                var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 500);
                var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                    provider, model, messages, options, HttpContext.RequestAborted, operation: "master-evict");

                var result = response.Text ?? "";
                try
                {
                    var jsonStart = result.IndexOf('{');
                    var jsonEnd = result.LastIndexOf('}');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var json = result.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("foundation", out var f) && !string.IsNullOrEmpty(f.GetString()))
                            profile.Foundation = (profile.Foundation ?? "") + $"[{stage}] {f.GetString()}; ";
                        if (doc.RootElement.TryGetProperty("learningStyle", out var ls) && !string.IsNullOrEmpty(ls.GetString()))
                            profile.LearningStyle = ls.GetString();
                        if (doc.RootElement.TryGetProperty("strengths", out var s) && !string.IsNullOrEmpty(s.GetString()))
                            profile.Strengths = (profile.Strengths ?? "") + $"[{stage}] {s.GetString()}; ";
                        if (doc.RootElement.TryGetProperty("weaknesses", out var w) && !string.IsNullOrEmpty(w.GetString()))
                            profile.Weaknesses = (profile.Weaknesses ?? "") + $"[{stage}] {w.GetString()}; ";
                    }
                }
                catch { }

                db.StageSummaries.Remove(summary);
                var remainingConvs = await db.MasterConversations
                    .Where(c => c.MasterId == id && c.Stage == stage)
                    .ToListAsync();
                db.MasterConversations.RemoveRange(remainingConvs);
                evictedCount++;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("师父 {MasterId} 淘汰完成，处理 {Count} 个阶段", id, evictedCount);
            return Ok(new { Success = true, EvictedStages = evictedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据淘汰失败");
            return StatusCode(500, new { Success = false, Message = $"淘汰失败：{ex.Message}" });
        }
    }

    private static string TruncateText(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private (AiProviderConfig Provider, string Model) ResolveProviderAndModel(string? providerId, string? model)
    {
        var providers = _aiSettings.GetAiProviders();
        var provider = string.IsNullOrEmpty(providerId)
            ? (string.IsNullOrEmpty(model)
                ? providers.FirstOrDefault(p => p.IsMain) ?? providers.FirstOrDefault()
                : providers.FirstOrDefault(p =>
                    p.Models.Any(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase)))
                  ?? providers.FirstOrDefault(p => p.IsMain)
                  ?? providers.FirstOrDefault())
            : providers.FirstOrDefault(p => p.Id == providerId);

        if (provider == null)
            throw new Exception("未找到可用的AI提供商");

        var modelOptions = provider.GetModelOptions();
        var resolvedModel = !string.IsNullOrEmpty(model)
            ? model
            : modelOptions.FirstOrDefault(m => m.IsMain)?.Name
              ?? modelOptions.FirstOrDefault()?.Name
              ?? "Qwen/Qwen2.5-14B-Instruct";

        return (provider, resolvedModel);
    }

    private static int GetStageOrder(string stageName)
    {
        return stageName switch
        {
            "入道" => 1,
            "筑基" => 2,
            "精进" => 3,
            "磨砺" => 4,
            "出师" => 5,
            _ => 0
        };
    }
}
