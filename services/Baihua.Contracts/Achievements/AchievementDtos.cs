namespace Baihua.Contracts.Achievements;

/// <summary>FAM-21 学习打卡数据</summary>
public class CheckinDataDto
{
    /// <summary>家庭维度连续打卡天数（从今天往前连续有记录的天数，北京时间）</summary>
    public int FamilyStreak { get; set; }

    /// <summary>今日学习清单（按 Learner 分组）</summary>
    public List<CheckinRecordDto> TodayRecords { get; set; } = new();

    /// <summary>最近 7 天打卡日历（7 格：日期 + 是否打卡 + 是否今天）</summary>
    public List<CheckinCalendarDayDto> Last7Days { get; set; } = new();
}

/// <summary>今日学习记录条目（AC1/AC3）</summary>
public class CheckinRecordDto
{
    public string LearnerName { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime? Time { get; set; }
    public bool IsCompleted { get; set; }

    /// <summary>来源标签（每日卡片/自由学习/复习模式）</summary>
    public string Source { get; set; } = "";

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int CardCount { get; set; }
    public double Accuracy { get; set; }
}

/// <summary>打卡日历格子（AC4）</summary>
public class CheckinCalendarDayDto
{
    public DateTime? Date { get; set; }
    public bool IsChecked { get; set; }
    public bool IsToday { get; set; }
}

public class LearnerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string Color { get; set; } = "";
    public bool IsDefault { get; set; }
}

public class CreateLearnerRequest
{
    public string Name { get; set; } = "";
    public string? AvatarEmoji { get; set; }
    public string? Color { get; set; }
}

public class AchievementDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Tier { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
}

public class LeaderboardEntryDto
{
    public int LearnerId { get; set; }
    public string LearnerName { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string Color { get; set; } = "";
    public int CardsStudied { get; set; }
    public double Accuracy { get; set; }
    public int Score { get; set; }
    public int Streak { get; set; }
    public int Rank { get; set; }
}

/// <summary>FAM-22 "和自己比"结果 DTO：本周 vs 上周</summary>
public class WeeklyCompareResultDto
{
    public int WeekTotal { get; set; }
    public int LastWeekTotal { get; set; }
    public int Delta { get; set; }
    public double Percent { get; set; }
    public string Arrow { get; set; } = "";
}

/// <summary>FAM-22 排行榜设置 DTO</summary>
public class LeaderboardSettingsDto
{
    /// <summary>全家排行 Tab 是否开启（默认 false）</summary>
    public bool AllFamilyTabEnabled { get; set; }
}

public class DashboardDataDto
{
    public List<FamilyMemberStatDto> FamilyStats { get; set; } = new();
    public List<DailyTrendDto> WeeklyTrend { get; set; } = new();
    public List<RecentAchievementDto> RecentAchievements { get; set; } = new();
    public ResultDistributionDto ResultDistribution { get; set; } = new();

    // ===== FAM-20 家长看板 v2（家庭日报）=====
    /// <summary>家庭维度连续打卡天数（任意成员有学习行为即算当天，北京时间自然日）</summary>
    public int FamilyStreak { get; set; }

    /// <summary>今日完成的卡片/任务数（北京时间）</summary>
    public int TodayCompleted { get; set; }

    /// <summary>昨日完成的卡片/任务数（北京时间）</summary>
    public int YesterdayCompleted { get; set; }

    /// <summary>趋势箭头：up=今天&gt;昨天 / down=今天&lt;昨天 / flat=持平 / ""=无数据（页面显示 --）</summary>
    public string TrendArrow { get; set; } = "";

    /// <summary>今日三件事条目（谁 + 做了什么）</summary>
    public List<TodayActivityItemDto> TodayActivities { get; set; } = new();

    /// <summary>最新成就（最多 3 个，按解锁时间倒序）</summary>
    public List<RecentAchievementDto> LatestAchievements { get; set; } = new();

    /// <summary>成长时间线（最近 30 天，时间倒序）</summary>
    public List<GrowthTimelineItemDto> GrowthTimeline { get; set; } = new();

    /// <summary>时间线分页大小（FAM-20-AC4 契约：每页 20 条）</summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>今日三件事条目：Learner 名 + 学习内容描述</summary>
public class TodayActivityItemDto
{
    public string LearnerName { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>成长时间线条目：日期 + Learner 名 + 事件描述</summary>
public class GrowthTimelineItemDto
{
    public DateTime? Date { get; set; }
    public string LearnerName { get; set; } = "";
    public string Description { get; set; } = "";
}

public class FamilyMemberStatDto
{
    public int LearnerId { get; set; }
    public string Name { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string Color { get; set; } = "";
    public int WeekTotal { get; set; }
    public double Accuracy { get; set; }
    public int Streak { get; set; }
    public int TotalCards { get; set; }
}

public class DailyTrendDto
{
    public string Date { get; set; } = "";
    public int Count { get; set; }
}

public class RecentAchievementDto
{
    public string LearnerName { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Tier { get; set; } = "";
    public DateTime UnlockedAt { get; set; }
}

public class ResultDistributionDto
{
    public int Remember { get; set; }
    public int Hard { get; set; }
    public int Forgot { get; set; }
}
