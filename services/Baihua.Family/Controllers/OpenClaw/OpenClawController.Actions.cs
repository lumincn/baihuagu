using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class OpenClawController : ControllerBase
{
    private async Task<ActionResult<OpenClawTaskDto>> HandleCreateTaskAsync(CreateOpenClawTaskRequest request)
    {
        var task = await _taskService.CreateTaskAsync(request.Prompt.Trim());
        return Ok(task);
    }

    private async Task<ActionResult<OpenClawTaskDto>> HandleGetTaskAsync(int id)
    {
        var task = await _taskService.GetTaskAsync(id);
        if (task == null)
            return NotFound(new { error = "任务不存在" });
        return Ok(task);
    }

    private async Task<ActionResult<string>> HandleGetReportAsync(int id)
    {
        var content = await _taskService.GetReportContentAsync(id);
        if (content == null)
            return NotFound(new { error = "报告不存在或尚未生成" });
        return Ok(content);
    }

    private async Task<IActionResult> HandleDeleteTaskAsync(int id)
    {
        var result = await _taskService.DeleteTaskAsync(id);
        if (!result)
            return NotFound(new { error = "任务不存在" });
        return NoContent();
    }

    private async Task<IActionResult> HandleCancelTaskAsync(int id)
    {
        var result = await _taskService.CancelTaskAsync(id);
        if (!result)
            return BadRequest(new { error = "任务不存在或已结束" });
        return Ok(new { success = true, message = "任务已取消" });
    }

    private async Task<IActionResult> HandleSaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
    {
        var success = await _localAiConfig.SaveLocalAiConfigAsync(request);
        if (!success)
            return BadRequest(new { error = "保存配置失败" });
        return Ok(new { success = true });
    }

    private async Task<ActionResult<List<OpenClawLocalModelDto>>> HandleScanLocalModelsAsync(string provider)
    {
        var models = await _localAiConfig.ScanLocalModelsAsync(provider);
        return Ok(models);
    }

    private async Task<ActionResult<LocalAiServiceStatusDto>> HandleDetectAndStartLocalAiAsync(string provider)
    {
        var result = await _localAiConfig.DetectAndStartLocalAiAsync(provider);
        return Ok(result);
    }

    private async Task<IActionResult> HandleSetDefaultModelAsync(string model)
    {
        var success = await _modelProfile.SetDefaultModelAsync(model);
        if (!success)
            return BadRequest(new { error = "设置默认模型失败" });
        return Ok(new { success = true });
    }

    private async Task<IActionResult> HandleSyncLocalModelsAsync(string provider)
    {
        var success = await _localAiConfig.SyncLocalModelsToOpenClawAsync(provider);
        if (!success)
            return BadRequest(new { error = $"同步 {provider} 模型到 OpenClaw 失败" });
        return Ok(new { success = true, message = $"{provider} 模型已同步到 OpenClaw" });
    }

    private async Task<IActionResult> HandleSetModelProfileAsync(string profile)
    {
        var success = await _modelProfile.SetModelProfileAsync(profile);
        if (!success)
            return BadRequest(new { error = $"设置模型配置 {profile} 失败" });
        return Ok(new { success = true, message = $"已切换到 {profile} 配置" });
    }
}
