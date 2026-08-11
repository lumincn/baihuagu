using Baihua.Contracts.Assistant;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers;

/// <summary>
/// AI 数字助理 API：设置（开关）、每日兴趣分析、活动概览
/// </summary>
[ApiController]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private readonly AssistantService _assistant;
    private readonly UserActivityService _activities;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(
        AssistantService assistant,
        UserActivityService activities,
        ILogger<AssistantController> logger)
    {
        _assistant = assistant;
        _activities = activities;
        _logger = logger;
    }

    /// <summary>读取助理设置</summary>
    [HttpGet("settings")]
    public ActionResult<AssistantSettingsDto> GetSettings() => Ok(_assistant.GetSettings());

    /// <summary>保存助理设置（开关等）</summary>
    [HttpPost("settings")]
    public IActionResult SaveSettings([FromBody] AssistantSettingsDto settings)
    {
        _assistant.SaveSettings(settings);
        return Ok();
    }

    /// <summary>今日分析结果（未分析返回 null）</summary>
    [HttpGet("analysis/today")]
    public ActionResult<AssistantAnalysisDto?> GetTodayAnalysis()
    {
        return Ok(_assistant.GetAnalysis(DateTime.Today));
    }

    /// <summary>手动触发今日分析</summary>
    [HttpPost("analysis/run")]
    public async Task<ActionResult<AssistantAnalysisDto>> RunAnalysis(CancellationToken ct)
    {
        try
        {
            return Ok(await _assistant.AnalyzeAsync(force: true, ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动触发助理分析失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>最近分析历史（含兴趣主题的）</summary>
    [HttpGet("analysis/history")]
    public ActionResult<List<AssistantAnalysisDto>> GetHistory([FromQuery] int days = 14)
    {
        return Ok(_assistant.GetHistory(days));
    }

    /// <summary>今日活动记录（最多 100 条）</summary>
    [HttpGet("activities/today")]
    public ActionResult<List<UserActivityDto>> GetTodayActivities()
    {
        var list = _activities.GetActivities(DateTime.Today);
        return Ok(list.TakeLast(100).ToList());
    }

    /// <summary>最近活动量统计（页面图表）</summary>
    [HttpGet("activities/counts")]
    public ActionResult<Dictionary<string, int>> GetActivityCounts([FromQuery] int days = 14)
    {
        return Ok(_activities.GetRecentActivityCounts(days));
    }
}
