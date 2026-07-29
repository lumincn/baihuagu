using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;

namespace TaskRunner.Services;

/// <summary>
/// 家庭赛舟榜服务
/// </summary>


public class LeaderboardService
{
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;

    public LeaderboardService(IDbContextFactory<FamilyDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// 获取周排行榜
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetWeeklyLeaderboardAsync(string? vaultId = null)
    {
        var startOfWeek = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
        return await GetLeaderboardAsync(startOfWeek, vaultId);
    }

    /// <summary>
    /// 获取月排行榜
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetMonthlyLeaderboardAsync(string? vaultId = null)
    {
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await GetLeaderboardAsync(startOfMonth, vaultId);
    }

    /// <summary>
    /// 获取总排行榜
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetAllTimeLeaderboardAsync(string? vaultId = null)
    {
        return await GetLeaderboardAsync(DateTime.MinValue, vaultId);
    }

    /// <summary>
    /// 获取 streak 排行榜
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetStreakLeaderboardAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learners = await db.LearnerProfiles.ToListAsync();
        var result = new List<LeaderboardEntry>();

        foreach (var learner in learners)
        {
            var streak = await CalculateStreakAsync(db, learner.Id);
            result.Add(new LeaderboardEntry
            {
                LearnerId = learner.Id,
                LearnerName = learner.Name,
                AvatarEmoji = learner.AvatarEmoji,
                Color = learner.Color,
                Streak = streak,
                Score = streak * 10 // streak 换算成分数
            });
        }

        return result.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>
    /// 获取正确率排行榜（今日）
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetAccuracyLeaderboardAsync(string? vaultId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learners = await db.LearnerProfiles.ToListAsync();
        var today = DateTime.UtcNow.Date;
        var result = new List<LeaderboardEntry>();

        foreach (var learner in learners)
        {
            var query = db.StudyActivities.Where(a => a.LearnerId == learner.Id
                && a.ActivityType == "study"
                && a.CreatedAt.Date == today
                && a.Result != null);
            if (!string.IsNullOrEmpty(vaultId))
                query = query.Where(a => a.VaultId == vaultId);

            var records = await query.ToListAsync();
            var total = records.Count;
            var remembered = records.Count(r => r.Result == "remember");
            var accuracy = total > 0 ? (double)remembered / total : 0;

            result.Add(new LeaderboardEntry
            {
                LearnerId = learner.Id,
                LearnerName = learner.Name,
                AvatarEmoji = learner.AvatarEmoji,
                Color = learner.Color,
                CardsStudied = total,
                Accuracy = accuracy * 100,
                Score = (int)(accuracy * 100)
            });
        }

        return result.OrderByDescending(r => r.Accuracy).ThenByDescending(r => r.CardsStudied).ToList();
    }

    private async Task<List<LeaderboardEntry>> GetLeaderboardAsync(DateTime since, string? vaultId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learners = await db.LearnerProfiles.ToListAsync();
        var result = new List<LeaderboardEntry>();

        foreach (var learner in learners)
        {
            var query = db.StudyActivities.Where(a => a.LearnerId == learner.Id
                && a.ActivityType == "study"
                && a.CreatedAt >= since);
            if (!string.IsNullOrEmpty(vaultId))
                query = query.Where(a => a.VaultId == vaultId);

            var records = await query.ToListAsync();
            var total = records.Count;
            var remembered = records.Count(r => r.Result == "remember");
            var accuracy = total > 0 ? (double)remembered / total : 0;

            result.Add(new LeaderboardEntry
            {
                LearnerId = learner.Id,
                LearnerName = learner.Name,
                AvatarEmoji = learner.AvatarEmoji,
                Color = learner.Color,
                CardsStudied = total,
                Accuracy = accuracy * 100,
                Score = total + (int)(accuracy * 20), // 学习数量 + 正确率加成
                Streak = await CalculateStreakAsync(db, learner.Id)
            });
        }

        return result.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>
    /// 获取家长看板数据
    /// </summary>
    public async Task<DashboardData> GetDashboardAsync(string? vaultId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learners = await db.LearnerProfiles.ToListAsync();
        var today = DateTime.UtcNow.Date;
        var weekAgo = today.AddDays(-6);

        var familyStats = new List<FamilyMemberStat>();
        var weeklyTrend = new List<DailyTrend>();
        var recentAchievements = new List<RecentAchievement>();

        // 每周趋势（初始化 7 天为 0）
        for (int i = 6; i >= 0; i--)
        {
            weeklyTrend.Add(new DailyTrend { Date = today.AddDays(-i).ToString("MM-dd"), Count = 0 });
        }

        foreach (var learner in learners)
        {
            var query = db.StudyActivities.Where(a => a.LearnerId == learner.Id && a.ActivityType == "study");
            if (!string.IsNullOrEmpty(vaultId)) query = query.Where(a => a.VaultId == vaultId);

            var activities = await query.ToListAsync();
            var weekActivities = activities.Where(a => a.CreatedAt.Date >= weekAgo).ToList();

            var total = activities.Count;
            var weekTotal = weekActivities.Count;
            var remembered = weekActivities.Count(r => r.Result == "remember");
            var accuracy = weekTotal > 0 ? (double)remembered / weekTotal * 100 : 0;
            var streak = await CalculateStreakAsync(db, learner.Id);

            familyStats.Add(new FamilyMemberStat
            {
                LearnerId = learner.Id,
                Name = learner.Name,
                AvatarEmoji = learner.AvatarEmoji,
                Color = learner.Color,
                WeekTotal = weekTotal,
                Accuracy = accuracy,
                Streak = streak,
                TotalCards = total
            });

            // 累加每周趋势
            foreach (var act in weekActivities)
            {
                var dateStr = act.CreatedAt.Date.ToString("MM-dd");
                var day = weeklyTrend.FirstOrDefault(d => d.Date == dateStr);
                if (day != null) day.Count++;
            }
        }

        // 最近解锁的成就
        var achievements = await db.Achievements
            .Where(a => a.UnlockedAt >= weekAgo)
            .OrderByDescending(a => a.UnlockedAt)
            .Take(10)
            .ToListAsync();

        foreach (var ach in achievements)
        {
            var learner = learners.FirstOrDefault(l => l.Id == ach.LearnerId);
            recentAchievements.Add(new RecentAchievement
            {
                LearnerName = learner?.Name ?? "",
                AvatarEmoji = learner?.AvatarEmoji ?? "",
                Title = ach.Title,
                Icon = ach.Icon,
                Tier = ach.Tier,
                UnlockedAt = ach.UnlockedAt
            });
        }

        // 答题结果分布
        var allWeekActivities = db.StudyActivities.Where(a => a.ActivityType == "study" && a.CreatedAt.Date >= weekAgo);
        if (!string.IsNullOrEmpty(vaultId)) allWeekActivities = allWeekActivities.Where(a => a.VaultId == vaultId);
        var weekResults = await allWeekActivities.ToListAsync();

        return new DashboardData
        {
            FamilyStats = familyStats,
            WeeklyTrend = weeklyTrend,
            RecentAchievements = recentAchievements,
            ResultDistribution = new ResultDistribution
            {
                Remember = weekResults.Count(r => r.Result == "remember"),
                Hard = weekResults.Count(r => r.Result == "hard"),
                Forgot = weekResults.Count(r => r.Result == "forgot")
            }
        };
    }

    private async Task<int> CalculateStreakAsync(FamilyDbContext db, int learnerId)
    {
        var dates = await db.StudyActivities
            .Where(a => a.LearnerId == learnerId && a.ActivityType == "study")
            .Select(a => a.CreatedAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        int streak = 0;
        var today = DateTime.UtcNow.Date;
        for (int i = 0; i < dates.Count; i++)
        {
            var expected = today.AddDays(-i);
            if (dates[i] == expected || (i == 0 && dates[i] == expected.AddDays(-1)))
            {
                streak++;
            }
            else
            {
                break;
            }
        }
        return streak;
    }
}
