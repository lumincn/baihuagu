using Baihua.Contracts.ComputePool;
using Baihua.Family.Services.ComputePool;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers.ComputePool;

/// <summary>
/// 算力池管理端点（管理 API，WebUI /compute 页用）。
/// </summary>
[ApiController]
[Route("api/compute-pool")]
public class ComputePoolController : ControllerBase
{
    private readonly ComputePoolService _poolService;
    private readonly ILogger<ComputePoolController> _logger;

    public ComputePoolController(ComputePoolService poolService, ILogger<ComputePoolController> logger)
    {
        _poolService = poolService;
        _logger = logger;
    }

    /// <summary>算力池总览（本机 + 各对端节点与模型）。</summary>
    [HttpGet]
    public async Task<ActionResult<ComputePoolViewDto>> GetPool(CancellationToken ct)
    {
        return Ok(await _poolService.GetPoolViewAsync(ct));
    }

    /// <summary>立即刷新对端能力（可选，正常每 60s 自动刷新）。</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        await _poolService.RefreshAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>选用某个节点+模型为本机主 AI 提供方。</summary>
    [HttpPost("select")]
    public IActionResult Select([FromBody] SelectComputeModelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServerId) || string.IsNullOrWhiteSpace(request.ModelName))
            return BadRequest(new { error = "缺少 serverId 或 modelName" });

        var ok = _poolService.SelectModel(request.ServerId.Trim(), request.ModelName.Trim(), out var error);
        if (!ok)
            return BadRequest(new { error });
        return Ok(new { success = true, message = $"已选用 {request.ModelName}" });
    }

    /// <summary>跨机测速：在指定节点（本机或对端）运行该模型的快速 benchmark。</summary>
    [HttpPost("benchmark")]
    public async Task<ActionResult<BenchmarkRunResultDto>> Benchmark([FromBody] SelectComputeModelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ServerId) || string.IsNullOrWhiteSpace(request.ModelName))
            return BadRequest(new { error = "缺少 serverId 或 modelName" });

        var result = await _poolService.RunPeerBenchmarkAsync(request.ServerId.Trim(), request.ModelName.Trim(), ct);
        return result != null ? Ok(result) : Ok(new BenchmarkRunResultDto { Success = false, Error = "测速失败（节点不可达或未就绪）", ModelName = request.ModelName.Trim() });
    }

    /// <summary>从对端拉取模型（模型商店）。</summary>
    [HttpPost("pull-model")]
    public async Task<IActionResult> PullModel([FromBody] PullModelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ServerId) || string.IsNullOrWhiteSpace(request.ModelName))
            return BadRequest(new { error = "缺少 serverId 或 modelName" });

        var (ok, error) = await _poolService.PullPeerModelAsync(request.ServerId.Trim(), request.ModelName.Trim(), ct);
        return ok ? Ok(new { success = true, message = $"已拉取 {request.ModelName}" }) : BadRequest(new { error });
    }
}
