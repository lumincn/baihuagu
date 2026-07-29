using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Baihua.Contracts.Ai;
using Baihua.Contracts.Master;
using Baihua.Family.Controllers.AI.Stages;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

/// <summary>
/// 师父阶段推进逻辑
/// </summary>
public partial class MasterController
{
    /// <summary>
    /// 阶段完成 — 生成摘要、祝福语、纠正点，推进到下一阶段。
    /// 各个阶段的行为通过 <see cref="StageStrategyFactory"/> 中的策略类定制。
    /// </summary>
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

            // 使用策略模式获取阶段信息
            var stageStrategy = StageStrategyFactory.GetStrategy(request.StageName);
            if (stageStrategy == null)
                return BadRequest(new StageCompleteResponse { Success = false, Message = $"未知的阶段：{request.StageName}" });

            var nextStrategy = StageStrategyFactory.GetNextStrategy(request.StageName);
            var nextStageName = nextStrategy?.StageName ?? "";

            var (provider, model) = ResolveProviderAndModel(null, null);

            // AI 生成阶段摘要
            var summaryPrompt = $"请为学徒在「{request.StageName}」阶段的学习生成一份简洁摘要（200字以内），包括：已掌握的知识点、仍需加强的方面、对下一阶段的建议。";
            var summaryMessages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一位学习评估专家，请简洁客观地总结学习成果。"),
                new(ChatRole.User, summaryPrompt)
            };

            var options = AiClientService.BuildChatOptions(temperature: 0.3f, maxOutputTokens: 500);
            var summaryResponse = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, summaryMessages, options, HttpContext.RequestAborted, operation: "master-stage-summary");

            var summary = summaryResponse.Text ?? "";

            // 使用策略获取祝福语
            var blessing = stageStrategy.GetBlessing(master.MasterName);

            // AI 生成纠正点
            var correctionsPrompt = $"请指出学徒在「{request.StageName}」阶段学习中需要重点纠正的2-3个关键问题（100字以内），若无则回复'无'。";
            var correctionsMessages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一位严格的学习督导，只指出最关键的纠正点。"),
                new(ChatRole.User, correctionsPrompt)
            };
            var correctionsResponse = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, model, correctionsMessages, options, HttpContext.RequestAborted, operation: "master-stage-corrections");
            var keyCorrections = correctionsResponse.Text ?? "";

            // 更新 Master 的阶段信息
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

            // 过渡到下一阶段时重置 vault focus 状态
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
                Summary = summary,
                Blessing = blessing,
                KeyCorrections = keyCorrections
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "阶段完成处理失败");
            return StatusCode(500, new StageCompleteResponse { Success = false, Message = $"阶段完成处理失败：{ex.Message}" });
        }
    }
}
