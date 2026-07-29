namespace TaskRunner.Contracts.Achievements;

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

public class DashboardDataDto
{
    public List<FamilyMemberStatDto> FamilyStats { get; set; } = new();
    public List<DailyTrendDto> WeeklyTrend { get; set; } = new();
    public List<RecentAchievementDto> RecentAchievements { get; set; } = new();
    public ResultDistributionDto ResultDistribution { get; set; } = new();
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
