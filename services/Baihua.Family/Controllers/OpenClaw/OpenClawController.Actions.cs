using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.OpenClaw;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

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
            return NotFound(new { error = _loc["OpenClaw_TaskNotFound"] });
        return Ok(task);
    }

    private async Task<ActionResult<string>> HandleGetReportAsync(int id)
    {
        var content = await _taskService.GetReportContentAsync(id);
        if (content == null)
            return NotFound(new { error = _loc["OpenClaw_ReportNotExists"] });
        return Ok(content);
    }

    private async Task<IActionResult> HandleDeleteTaskAsync(int id)
    {
        var result = await _taskService.DeleteTaskAsync(id);
        if (!result)
            return NotFound(new { error = _loc["OpenClaw_TaskNotFound"] });
        return NoContent();
    }

    private async Task<IActionResult> HandleCancelTaskAsync(int id)
    {
        var result = await _taskService.CancelTaskAsync(id);
        if (!result)
            return BadRequest(new { error = _loc["OpenClaw_TaskNotFoundOrDone"] });
        return Ok(new { success = true, message = _loc["OpenClaw_TaskCancelled"] });
    }

    private async Task<IActionResult> HandleSaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
    {
        var success = await _localAiConfig.SaveLocalAiConfigAsync(request);
        if (!success)
            return BadRequest(new { error = _loc["OpenClaw_SaveConfigFailed"] });
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
            return BadRequest(new { error = _loc["OpenClaw_SetDefaultModelFailed"] });
        return Ok(new { success = true });
    }

    private async Task<IActionResult> HandleSyncLocalModelsAsync(string provider)
    {
        var success = await _localAiConfig.SyncLocalModelsToOpenClawAsync(provider);
        if (!success)
            return BadRequest(new { error = string.Format(_loc["OpenClaw_SyncFailed"], provider) });
        return Ok(new { success = true, message = string.Format(_loc["OpenClaw_SyncSuccess"], provider) });
    }

    private async Task<IActionResult> HandleSetModelProfileAsync(string profile)
    {
        var success = await _modelProfile.SetModelProfileAsync(profile);
        if (!success)
            return BadRequest(new { error = string.Format(_loc["OpenClaw_SetProfileFailed"], profile) });
        return Ok(new { success = true, message = string.Format(_loc["OpenClaw_ProfileSwitched"], profile) });
    }
}
