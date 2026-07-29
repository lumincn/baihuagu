using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Models;
using TaskRunner.Services;

namespace TaskRunner.Controllers
{
    public partial class AIController
    {
        [HttpPost("chat")]
        public async Task<ActionResult<TaskRunner.Contracts.Ai.ChatResponse>> Chat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "消息不能为空" });
            }

            try
            {
                var (provider, model) = ResolveProviderAndModel(request.ProviderId, request.Model);

                // 构建消息列表（使用三层记忆系统）
                var messages = await BuildMessagesWithMemoryAsync(
                    request.History, provider.Id, model, request.Message, request.SessionId, HttpContext.RequestAborted);
                // RAG 增强
                messages = await _ragService.EnrichMessagesWithVaultContextAsync(messages);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await CallAiApiAsync(messages, model, provider.Id, enableTools: request.EnableTools ?? true, ct: HttpContext.RequestAborted);
                stopwatch.Stop();

                var sourceInfo = $"> 📌 **来源**: AI 对话  \n" +
                    $"> 🤖 **模型**: {model}  \n" +
                    $"> 🏢 **提供商**: {provider.Name}  \n" +
                    $"> ⏰ **时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  \n" +
                    $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \n\n";

                return Ok(new TaskRunner.Contracts.Ai.ChatResponse
                {
                    Success = true,
                    Message = "回复成功",
                    Reply = sourceInfo + result
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
    }
}
