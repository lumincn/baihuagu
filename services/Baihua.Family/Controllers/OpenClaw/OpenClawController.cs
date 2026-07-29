using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class OpenClawController : ControllerBase
{
    private readonly IOpenClawTaskService _taskService;
    private readonly ILocalAiConfigService _localAiConfig;
    private readonly IOpenClawModelProfileService _modelProfile;

    public OpenClawController(IOpenClawTaskService taskService, ILocalAiConfigService localAiConfig, IOpenClawModelProfileService modelProfile)
    {
        _taskService = taskService;
        _localAiConfig = localAiConfig;
        _modelProfile = modelProfile;
    }

    [HttpPost("tasks")]
    public async Task<ActionResult<OpenClawTaskDto>> CreateTask([FromBody] CreateOpenClawTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt 不能为空" });
        return await HandleCreateTaskAsync(request);
    }

    [HttpGet("tasks")]
    public async Task<ActionResult<List<OpenClawTaskDto>>> GetTasks([FromQuery] int limit = 100)
    {
        var tasks = await _taskService.GetTasksAsync(limit);
        return Ok(tasks);
    }

    [HttpGet("tasks/{id:int}")]
    public async Task<ActionResult<OpenClawTaskDto>> GetTask(int id)
        => await HandleGetTaskAsync(id);

    [HttpGet("tasks/{id:int}/report")]
    public async Task<ActionResult<string>> GetReport(int id)
        => await HandleGetReportAsync(id);

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> DeleteTask(int id)
        => await HandleDeleteTaskAsync(id);

    [HttpPost("tasks/{id:int}/cancel")]
    public async Task<IActionResult> CancelTask(int id)
        => await HandleCancelTaskAsync(id);

    [HttpGet("local-ai-config")]
    public async Task<ActionResult<OpenClawLocalAiConfigDto>> GetLocalAiConfig()
    {
        var config = await _localAiConfig.GetLocalAiConfigAsync();
        return Ok(config);
    }

    [HttpPost("local-ai-config")]
    public async Task<IActionResult> SaveLocalAiConfig([FromBody] SaveOpenClawLocalAiConfigRequest request)
        => await HandleSaveLocalAiConfigAsync(request);

    [HttpGet("local-ai-models")]
    public async Task<ActionResult<List<OpenClawLocalModelDto>>> ScanLocalModels([FromQuery] string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return BadRequest(new { error = "provider 参数不能为空" });
        return await HandleScanLocalModelsAsync(provider);
    }

    [HttpPost("local-ai-detect")]
    public async Task<ActionResult<LocalAiServiceStatusDto>> DetectAndStartLocalAi([FromBody] DetectLocalAiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { error = "provider 不能为空" });
        return await HandleDetectAndStartLocalAiAsync(request.Provider);
    }

    [HttpGet("default-model")]
    public async Task<ActionResult<OpenClawDefaultModelDto>> GetDefaultModel()
    {
        var result = await _modelProfile.GetDefaultModelAsync();
        return Ok(result);
    }

    [HttpPost("default-model")]
    public async Task<IActionResult> SetDefaultModel([FromBody] SetOpenClawDefaultModelRequest request)
        => await HandleSetDefaultModelAsync(request.Model);

    [HttpPost("sync-local-models")]
    public async Task<IActionResult> SyncLocalModels([FromBody] SyncLocalModelsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { error = "provider 不能为空" });
        return await HandleSyncLocalModelsAsync(request.Provider);
    }

    [HttpGet("model-profiles")]
    public async Task<ActionResult<ModelProfileListDto>> GetModelProfiles()
    {
        var result = await _modelProfile.GetModelProfilesAsync();
        return Ok(result);
    }

    [HttpPost("model-profiles")]
    public async Task<IActionResult> SetModelProfile([FromBody] SetModelProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Profile))
            return BadRequest(new { error = "profile 不能为空" });
        return await HandleSetModelProfileAsync(request.Profile);
    }
}
