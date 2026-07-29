using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Benchmark;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

/// <summary>
/// 模型基准测试 API
/// </summary>
[ApiController]
[Route("api/benchmark")]
public class ModelBenchmarkController : ControllerBase
{
    private readonly ModelBenchmarkService _benchmarkService;
    private readonly BenchmarkRepository _benchmarkRepo;
    private readonly HardwareInfoService _hardwareInfoService;
    private readonly ILogger<ModelBenchmarkController> _logger;

    public ModelBenchmarkController(
        ModelBenchmarkService benchmarkService,
        BenchmarkRepository benchmarkRepo,
        HardwareInfoService hardwareInfoService,
        ILogger<ModelBenchmarkController> logger)
    {
        _benchmarkService = benchmarkService;
        _benchmarkRepo = benchmarkRepo;
        _hardwareInfoService = hardwareInfoService;
        _logger = logger;
    }

    /// <summary>
    /// 获取内置推荐模型列表
    /// </summary>
    [HttpGet("models")]
    public ActionResult<List<RecommendedBenchmarkModel>> GetRecommendedModels([FromQuery] string? category)
    {
        return Ok(BenchmarkPrompts.GetModelsByCategory(category ?? ""));
    }

    /// <summary>
    /// 获取显存等级推荐表（INT4 / INT8）
    /// </summary>
    [HttpGet("vram-tiers")]
    public ActionResult<VramTierResponse> GetVramTiers([FromQuery] string? category)
    {
        var hardware = _hardwareInfoService.GetHardwareInfo();
        var availableVram = hardware.Gpus
            .Where(g => !g.IsIntegrated)
            .Select(g => g.VramGiB)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(hardware.Memory.TotalGiB / 2.0)
            .Max();

        var tiers = BenchmarkPrompts.GetTiersByCategory(category ?? "");
        var recommendedVramGb = tiers
            .Where(t => t.VramGb <= availableVram)
            .Select(t => (int?)t.VramGb)
            .LastOrDefault();

        foreach (var tier in tiers)
        {
            tier.IsRecommendedForCurrentHardware = tier.VramGb == recommendedVramGb;
        }

        return Ok(new VramTierResponse
        {
            Tiers = tiers,
            AvailableVramGiB = Math.Round(availableVram, 1),
            RecommendedTierVramGb = recommendedVramGb
        });
    }

    /// <summary>
    /// 获取测试提示词列表
    /// </summary>
    [HttpGet("prompts")]
    public ActionResult<List<BenchmarkPrompt>> GetPrompts([FromQuery] string? category)
    {
        return Ok(BenchmarkPrompts.GetPromptsByCategory(category ?? ""));
    }

    /// <summary>
    /// 开始运行基准测试（异步，立即返回）
    /// </summary>
    [HttpPost("run")]
    public IActionResult RunBenchmark([FromBody] RunBenchmarkRequest request)
    {
        // 在后台执行测试
        _ = Task.Run(async () =>
        {
            try
            {
                await _benchmarkService.RunBenchmarkAsync(request.Model, request.PromptIds, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台基准测试任务失败");
            }
        });

        return Accepted(new { message = "测试已启动" });
    }

    /// <summary>
    /// 停止当前运行的基准测试
    /// </summary>
    [HttpPost("stop")]
    public IActionResult StopBenchmark()
    {
        _benchmarkService.StopBenchmark();
        return Ok(new { message = "测试已停止" });
    }

    /// <summary>
    /// 获取当前测试状态
    /// </summary>
    [HttpGet("status")]
    public ActionResult<BenchmarkStatusDto> GetStatus()
    {
        return Ok(_benchmarkService.GetStatus());
    }

    /// <summary>
    /// 获取测试历史
    /// </summary>
    [HttpGet("history")]
    public ActionResult<List<BenchmarkSession>> GetHistory([FromQuery] string? category)
    {
        return Ok(_benchmarkRepo.GetHistory(category));
    }

    /// <summary>
    /// 获取排行榜
    /// </summary>
    [HttpGet("leaderboard")]
    public ActionResult<List<BenchmarkLeaderboardEntry>> GetLeaderboard([FromQuery] string? category)
    {
        return Ok(_benchmarkRepo.GetLeaderboard(category));
    }

    /// <summary>
    /// 删除某条历史记录
    /// </summary>
    [HttpDelete("history/{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        var ok = await _benchmarkRepo.DeleteSessionAsync(sessionId);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// 清空所有历史
    /// </summary>
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory()
    {
        await _benchmarkRepo.ClearHistoryAsync();
        return NoContent();
    }
}
