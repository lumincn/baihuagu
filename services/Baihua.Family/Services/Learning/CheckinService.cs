using Microsoft.EntityFrameworkCore;
using Baihua.Data;
using Baihua.Core.Time;

namespace Baihua.Family.Services;

/// <summary>
/// FAM-21 学习打卡服务：今日学习清单 + 家庭连续打卡 + 最近 7 天日历。
/// 打卡 = 当天（北京时间自然日）有 StudyActivity 记录，自动判定，无需手动按钮。
/// </summary>
public class CheckinService
{
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;
    private readonly ITimeProvider _timeProvider;

    public CheckinService(IDbContextFactory<FamilyDbContext> dbFactory, ITimeProvider timeProvider)
    {
        _dbFactory = dbFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>北京时间今日</summary>
    private DateTime BeijingToday => _timeProvider.Today;

    /// <summary>UTC 转北京时间日期</summary>
    private static DateTime ToBeijingDate(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, SystemTimeProvider.BeijingTz).Date;
    }

    /// <summary>
    /// 获取学习打卡数据（FAM-21）
    /// </summary>
    /// <param name="vaultId">知识库 ID（可空=全部）</param>
    public async Task<CheckinData> GetCheckinDataAsync(string? vaultId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var today = BeijingToday;

        var activityQuery = db.StudyActivities.AsQueryable();
        if (!string.IsNullOrEmpty(vaultId))
            activityQuery = activityQuery.Where(a => a.VaultId == vaultId);

        var activities = (await activityQuery.ToListAsync())
            .Select(a => new
            {
                a.LearnerId,
                a.ActivityType,
                a.CardId,
                a.Result,
                Date = ToBeijingDate(a.CreatedAt),
                a.CreatedAt
            })
            .ToList();

        var learners = await db.LearnerProfiles.ToListAsync();
        var learnerMap = learners.ToDictionary(l => l.Id);

        // SQLite 读出的 DateTime.Kind=Unspecified，统一转北京时间存储（测试与展示口径一致）
        DateTime ToBeijingLocal(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, SystemTimeProvider.BeijingTz);
        }

        // ===== AC1：今日学习清单（按 Learner 分组）=====
        var todayActivities = activities.Where(a => a.Date == today).ToList();
        var records = new List<CheckinRecord>();
        foreach (var act in todayActivities.OrderBy(a => a.CreatedAt))
        {
            var learner = learnerMap.GetValueOrDefault(act.LearnerId);
            var beijingTime = ToBeijingLocal(act.CreatedAt);
            records.Add(new CheckinRecord
            {
                LearnerName = learner?.Name ?? "",
                Content = ResolveContent(act.CardId, act.ActivityType),
                Time = beijingTime,
                IsCompleted = true, // 有学习记录 = 自动已打卡（AC2）
                Source = ResolveSource(act.ActivityType),
                StartTime = beijingTime,
                EndTime = beijingTime,
                CardCount = 1,
                Accuracy = ResolveAccuracy(act.Result)
            });
        }

        // 按 Learner 分组排序：Learner 名升序 + 时间倒序
        records = records
            .OrderBy(r => r.LearnerName)
            .ThenByDescending(r => r.Time)
            .ToList();

        // ===== AC2/AC4：家庭连续打卡（锚点算法：今天有→today；今天无但昨天有→yesterday；否则 0）=====
        var beijingDates = activities.Select(a => a.Date).ToList();
        var familyStreak = CalculateFamilyStreak(beijingDates, today);

        // ===== AC4：最近 7 天打卡日历（7 格，恰好 1 格 IsToday）=====
        var last7Days = new List<CheckinCalendarDay>();
        var checkedSet = new HashSet<DateTime>(beijingDates);
        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            last7Days.Add(new CheckinCalendarDay
            {
                Date = date,
                IsChecked = checkedSet.Contains(date),
                IsToday = date == today
            });
        }

        return new CheckinData
        {
            FamilyStreak = familyStreak,
            TodayRecords = records,
            Last7Days = last7Days
        };
    }

    /// <summary>
    /// 家庭维度连续打卡：任意成员有学习行为即算当天（北京时间自然日）。
    /// 锚点：今天有→today；今天无但昨天有→yesterday；否则 0。从锚点往回数连续天数。
    /// </summary>
    private static int CalculateFamilyStreak(List<DateTime> beijingDates, DateTime today)
    {
        var dateSet = new HashSet<DateTime>(beijingDates);
        if (dateSet.Count == 0) return 0;

        DateTime anchor;
        if (dateSet.Contains(today)) anchor = today;
        else if (dateSet.Contains(today.AddDays(-1))) anchor = today.AddDays(-1);
        else return 0;

        int streak = 0;
        while (dateSet.Contains(anchor.AddDays(-streak)))
            streak++;
        return streak;
    }

    /// <summary>学习内容名称（AC1）：优先卡片 ID，缺失时按活动类型描述</summary>
    private static string ResolveContent(string? cardId, string activityType)
    {
        if (!string.IsNullOrWhiteSpace(cardId))
            return $"卡片 {cardId}";
        return activityType switch
        {
            "study" => "每日卡片学习",
            "create_card" => "创作学习卡片",
            "generate_cards" => "批量生成卡片",
            "chat" => "AI 对话学习",
            _ => "学习活动"
        };
    }

    /// <summary>来源标签（AC3 可追溯）：按 ActivityType 映射（StudyActivity 无 session 级字段）</summary>
    private static string ResolveSource(string activityType)
    {
        return activityType switch
        {
            "study" => "每日卡片",
            "review" => "复习模式",
            "create_card" or "generate_cards" => "自由学习",
            _ => "自由学习"
        };
    }

    /// <summary>正确率推断（AC3）：remember=100 / hard=50 / forgot=0</summary>
    private static double ResolveAccuracy(string? result)
    {
        return result switch
        {
            "remember" => 100,
            "hard" => 50,
            _ => 0
        };
    }
}
