using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Baihua.Contracts.Ai;
using Baihua.Contracts.Master;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

/// <summary>
/// 师父数据压缩和淘汰 — 用于长期保留的对话记录管理
/// </summary>
public partial class MasterController
{
    /// <summary>
    /// 压缩已毕业阶段的对话记录，生成摘要后删除原始对话
    /// </summary>
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

    /// <summary>
    /// 淘汰超过30天的阶段内容，合并到学徒画像
    /// </summary>
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

    /// <summary>
    /// 对所有 active 的师父执行批量压缩和淘汰
    /// </summary>
    [HttpPost("evict-all")]
    public async Task<ActionResult<MasterEvictResponse>> EvictAll()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var masters = await db.Masters.Where(m => m.Status == "active").ToListAsync();

            int compressedCount = 0;
            int evictedCount = 0;

            foreach (var master in masters)
            {
                var graduated = System.Text.Json.JsonSerializer.Deserialize<List<string>>(master.GraduatedStagesJson) ?? new();
                var compressCutoff = DateTime.Now.AddDays(-7);

                foreach (var stage in graduated)
                {
                    var existingSummary = await db.StageSummaries
                        .FirstOrDefaultAsync(s => s.MasterId == master.MasterId && s.StageName == stage);
                    if (existingSummary != null) continue;

                    var conversations = await db.MasterConversations
                        .Where(c => c.MasterId == master.MasterId && c.Stage == stage && c.CreatedAt < compressCutoff)
                        .OrderBy(c => c.CreatedAt)
                        .ToListAsync();

                    if (conversations.Count == 0) continue;

                    try
                    {
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
                            provider, model, messages, options, HttpContext.RequestAborted, operation: "master-bulk-compress");

                        var summary = response.Text ?? "";

                        db.StageSummaries.Add(new StageSummary
                        {
                            MasterId = master.MasterId,
                            StageName = stage,
                            Summary = summary
                        });

                        db.MasterConversations.RemoveRange(conversations);
                        compressedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "批量压缩失败：师父 {MasterId} 阶段 {Stage}", master.MasterId, stage);
                    }
                }

                var evictCutoff = DateTime.Now.AddDays(-30);
                var profile = await db.ApprenticeProfiles.FirstOrDefaultAsync(p => p.MasterId == master.MasterId);
                if (profile == null)
                {
                    profile = new ApprenticeProfile { MasterId = master.MasterId };
                    db.ApprenticeProfiles.Add(profile);
                }

                foreach (var stage in graduated)
                {
                    var summary = await db.StageSummaries
                        .FirstOrDefaultAsync(s => s.MasterId == master.MasterId && s.StageName == stage);
                    if (summary == null || summary.CreatedAt > evictCutoff) continue;

                    try
                    {
                        var (provider, model) = ResolveProviderAndModel(null, null);
                        var profilePrompt = $"根据以下阶段学习摘要，提取学徒的核心能力画像（基础、学习风格、优势、薄弱点），以JSON格式返回：{{\"foundation\": \"...\", \"learningStyle\": \"...\", \"strengths\": \"...\", \"weaknesses\": \"...\"}}\n\n阶段：{stage}\n摘要：{summary.Summary}";
                        var messages = new List<ChatMessage>
                        {
                            new(ChatRole.System, "你是一位学习评估专家，请客观提取学徒能力画像。"),
                            new(ChatRole.User, profilePrompt)
                        };
                        var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 500);
                        var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                            provider, model, messages, options, HttpContext.RequestAborted, operation: "master-bulk-evict");

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
                            .Where(c => c.MasterId == master.MasterId && c.Stage == stage)
                            .ToListAsync();
                        db.MasterConversations.RemoveRange(remainingConvs);
                        evictedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "批量淘汰失败：师父 {MasterId} 阶段 {Stage}", master.MasterId, stage);
                    }
                }
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("批量数据驱逐完成：压缩 {Compressed} 阶段，淘汰 {Evicted} 阶段", compressedCount, evictedCount);

            return Ok(new MasterEvictResponse
            {
                Success = true,
                Message = $"数据驱逐完成：压缩 {compressedCount} 个阶段，淘汰 {evictedCount} 个阶段",
                CompressedStages = compressedCount,
                EvictedStages = evictedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量数据驱逐失败");
            return StatusCode(500, new MasterEvictResponse { Success = false, Message = $"驱逐失败：{ex.Message}" });
        }
    }
}
