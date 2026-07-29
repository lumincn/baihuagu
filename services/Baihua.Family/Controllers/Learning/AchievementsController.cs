using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;
using TaskRunner.Contracts.Achievements;

namespace TaskRunner.Controllers;

/// <summary>
/// 成就与赛舟榜 API
/// </summary>
[ApiController]
[Route("api/achievements")]
public partial class AchievementsController : ControllerBase
{
    private readonly LearnerService _learnerService;
    private readonly AchievementEngine _achievementEngine;
    private readonly LeaderboardService _leaderboardService;

    public AchievementsController(
        LearnerService learnerService,
        AchievementEngine achievementEngine,
        LeaderboardService leaderboardService)
    {
        _learnerService = learnerService;
        _achievementEngine = achievementEngine;
        _leaderboardService = leaderboardService;
    }

    // ---- 学习者管理 ----

    [HttpGet("learners")]
    public async Task<ActionResult<List<LearnerDto>>> GetLearners()
    {
        var learners = await _learnerService.GetAllAsync();
        return Ok(learners.Select(l => new LearnerDto
        {
            Id = l.Id,
            Name = l.Name,
            AvatarEmoji = l.AvatarEmoji,
            Color = l.Color,
            IsDefault = l.IsDefault
        }).ToList());
    }

    [HttpPost("learners")]
    public async Task<ActionResult<LearnerDto>> CreateLearner([FromBody] CreateLearnerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "名称不能为空" });

        var learner = await _learnerService.CreateAsync(request.Name.Trim(), request.AvatarEmoji ?? "👤", request.Color ?? "#007bff");
        return Ok(new LearnerDto
        {
            Id = learner.Id,
            Name = learner.Name,
            AvatarEmoji = learner.AvatarEmoji,
            Color = learner.Color,
            IsDefault = learner.IsDefault
        });
    }

    [HttpPost("learners/{id}/default")]
    public async Task<ActionResult> SetDefaultLearner(int id)
    {
        await _learnerService.SetDefaultAsync(id);
        return Ok(new { success = true });
    }

    [HttpDelete("learners/{id}")]
    public async Task<ActionResult> DeleteLearner(int id)
    {
        var success = await _learnerService.DeleteAsync(id);
        if (!success) return NotFound();
        return Ok(new { success = true });
    }

    // ---- 成就 ----

    [HttpGet]
    public async Task<ActionResult<List<AchievementDto>>> GetAchievements([FromQuery] int learnerId)
    {
        var achievements = await _achievementEngine.GetAchievementsAsync(learnerId);
        return Ok(achievements.Select(a => new AchievementDto
        {
            Key = a.Key,
            Title = a.Title,
            Description = a.Description,
            Icon = a.Icon,
            Tier = a.Tier,
            Category = a.Category,
            IsUnlocked = a.IsUnlocked,
            UnlockedAt = a.UnlockedAt
        }).ToList());
    }

    [HttpPost("check")]
    public async Task<ActionResult<List<AchievementDto>>> CheckAchievements([FromQuery] int learnerId)
    {
        var newlyUnlocked = await _achievementEngine.CheckAndUnlockAsync(learnerId);
        return Ok(newlyUnlocked.Select(a => new AchievementDto
        {
            Key = a.Key,
            Title = a.Title,
            Description = a.Description,
            Icon = a.Icon,
            Tier = a.Tier,
            Category = a.Category,
            IsUnlocked = true,
            UnlockedAt = DateTime.UtcNow
        }).ToList());
    }

    // ---- 赛舟榜 ----

    [HttpGet("leaderboard/weekly")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetWeeklyLeaderboard([FromQuery] string? vaultId = null)
    {
        var entries = await _leaderboardService.GetWeeklyLeaderboardAsync(vaultId);
        return Ok(ToDtos(entries));
    }

    [HttpGet("leaderboard/monthly")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetMonthlyLeaderboard([FromQuery] string? vaultId = null)
    {
        var entries = await _leaderboardService.GetMonthlyLeaderboardAsync(vaultId);
        return Ok(ToDtos(entries));
    }

    [HttpGet("leaderboard/alltime")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetAllTimeLeaderboard([FromQuery] string? vaultId = null)
    {
        var entries = await _leaderboardService.GetAllTimeLeaderboardAsync(vaultId);
        return Ok(ToDtos(entries));
    }

    [HttpGet("leaderboard/streak")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetStreakLeaderboard()
    {
        var entries = await _leaderboardService.GetStreakLeaderboardAsync();
        return Ok(ToDtos(entries));
    }

    [HttpGet("leaderboard/accuracy")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetAccuracyLeaderboard([FromQuery] string? vaultId = null)
    {
        var entries = await _leaderboardService.GetAccuracyLeaderboardAsync(vaultId);
        return Ok(ToDtos(entries));
    }

    /// <summary>
    /// 家长看板数据
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDataDto>> GetDashboard([FromQuery] string? vaultId = null)
        => Ok(await HandleGetDashboardAsync(vaultId));
}
