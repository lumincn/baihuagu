using TaskRunner.Core.Shared;
using TaskRunner.Services;
using System.Text.Json;
using TaskRunner.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Models;
using TaskRunner.Contracts.Scene;
using TaskRunner.Contracts.Tasks;
using TaskRunner.Contracts.Vaults;

namespace TaskRunner.Controllers
{
    public partial class TasksController : ControllerBase
    {
        /// <summary>
        /// 重试失败/超时的 AI 查询任务，可指定新的超时时间
        /// </summary>
        [HttpPost("{taskId}/retry")]
        public async Task<ActionResult<AiTaskResponse>> RetryAiTask(string taskId, [FromBody] RetryAiTaskRequest? retryRequest = null)
            => await HandleRetryAiTaskAsync(taskId, retryRequest);

        [HttpPost("ai-query")]
        public async Task<ActionResult<AiTaskResponse>> CreateAiTask([FromBody] AiTaskRequest request)
            => await HandleCreateAiTaskAsync(request);
    }
}
