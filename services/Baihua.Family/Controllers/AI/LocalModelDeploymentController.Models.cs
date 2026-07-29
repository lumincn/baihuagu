using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class LocalModelDeploymentController
{
    #region Running Model Management

    /// <summary>
    /// 获取运行中的模型列表
    /// </summary>
    [HttpGet("running")]
    public async Task<ActionResult<List<RunningModelDto>>> GetRunningModels([FromQuery] bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _deploymentService.GetRunningModelsAsync(forceRefresh, cancellationToken);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取运行中模型失败");
            return StatusCode(500, new { error = "获取运行中模型失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取指定工具中可用的模型列表（已下载）
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult<List<string>>> GetAvailableModels([FromQuery] string toolId, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return BadRequest(new { error = "toolId 不能为空" });

            var models = await _deploymentService.GetAvailableModelsAsync(toolId, cancellationToken);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取可用模型列表失败");
            return StatusCode(500, new { error = "获取可用模型列表失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 加载模型到内存
    /// </summary>
    [HttpPost("running/load")]
    public async Task<ActionResult<dynamic>> LoadModel([FromBody] LoadModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ModelName))
                return BadRequest(new { error = "ToolId 和 ModelName 不能为空" });

            var success = await _deploymentService.LoadModelAsync(request.ToolId, request.ModelName, request.KeepAliveMinutes, cancellationToken);
            if (success)
                return Ok(new { success = true, message = $"模型 {request.ModelName} 已加载" });

            return StatusCode(500, new { error = "加载失败", message = "请检查工具是否运行" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载模型失败");
            return StatusCode(500, new { error = "加载失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 卸载模型释放内存
    /// </summary>
    [HttpPost("running/unload")]
    public async Task<ActionResult<dynamic>> UnloadModel([FromBody] UnloadModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ModelName))
                return BadRequest(new { error = "ToolId 和 ModelName 不能为空" });

            var success = await _deploymentService.UnloadModelAsync(request.ToolId, request.ModelName, cancellationToken);
            if (success)
                return Ok(new { success = true, message = $"模型 {request.ModelName} 已卸载" });

            return StatusCode(500, new { error = "卸载失败", message = "请检查工具是否运行" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "卸载模型失败");
            return StatusCode(500, new { error = "卸载失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 启动 llama.cpp 服务
    /// </summary>
    [HttpPost("llamacpp/start")]
    public async Task<ActionResult<LocalAiServiceStatusDto>> StartLlamaCpp(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _deploymentService.StartLlamaCppAsync(cancellationToken);
            if (status.IsRunning)
                return Ok(status);
            return StatusCode(500, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 llama.cpp 失败");
            return StatusCode(500, new LocalAiServiceStatusDto { Provider = "llamacpp", Message = ex.Message });
        }
    }

    /// <summary>
    /// 停止 llama.cpp 服务
    /// </summary>
    [HttpPost("llamacpp/stop")]
    public async Task<ActionResult> StopLlamaCpp(CancellationToken cancellationToken)
    {
        try
        {
            var success = await _deploymentService.StopLlamaCppAsync(cancellationToken);
            if (success)
                return Ok(new { success = true, message = "llama.cpp 已停止" });
            return StatusCode(500, new { error = "停止失败", message = "请检查进程是否存活" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 llama.cpp 失败");
            return StatusCode(500, new { error = "停止失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取所有已下载模型（聚合所有工具）
    /// </summary>
    [HttpGet("downloaded")]
    public async Task<ActionResult<List<DownloadedModelDto>>> GetDownloadedModels(CancellationToken cancellationToken)
    {
        try
        {
            var models = await _deploymentService.GetDownloadedModelsAsync(cancellationToken);
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取已下载模型列表失败");
            return StatusCode(500, new { error = "获取已下载模型列表失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 删除本地模型
    /// </summary>
    [HttpPost("delete")]
    public async Task<ActionResult> DeleteModel([FromBody] DeleteModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ModelName))
                return BadRequest(new { error = "ToolId 和 ModelName 不能为空" });

            var success = await _deploymentService.DeleteModelAsync(request.ToolId, request.ModelName, cancellationToken);
            if (success)
                return Ok(new { success = true, message = $"模型 {request.ModelName} 已删除" });

            return StatusCode(500, new { error = "删除失败", message = "请检查工具是否运行或模型是否存在" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除模型失败");
            return StatusCode(500, new { error = "删除失败", message = ex.Message });
        }
    }

    /// <summary>
    /// 获取模型详情
    /// </summary>
    [HttpPost("details")]
    public async Task<ActionResult<ModelDetailsDto?>> GetModelDetails([FromBody] ModelDetailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ModelName))
                return BadRequest(new { error = "ToolId 和 ModelName 不能为空" });

            var details = await _deploymentService.GetModelDetailsAsync(request.ToolId, request.ModelName, cancellationToken);
            if (details != null)
                return Ok(details);

            return NotFound(new { error = "模型详情未找到", message = "该工具暂不支持查看模型详情" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取模型详情失败");
            return StatusCode(500, new { error = "获取模型详情失败", message = ex.Message });
        }
    }

    #endregion
}
