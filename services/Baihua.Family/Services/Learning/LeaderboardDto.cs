namespace TaskRunner.Services;

public class DashboardData
{
    public List<FamilyMemberStat> FamilyStats { get; set; } = new();
    public List<DailyTrend> WeeklyTrend { get; set; } = new();
    public List<RecentAchievement> RecentAchievements { get; set; } = new();
    public ResultDistribution ResultDistribution { get; set; } = new();
}

public class FamilyMemberStat
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

public class DailyTrend
{
    public string Date { get; set; } = "";
    public int Count { get; set; }
}

public class RecentAchievement
{
    public string LearnerName { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Tier { get; set; } = "";
    public DateTime UnlockedAt { get; set; }
}

public class ResultDistribution
{
    public int Remember { get; set; }
    public int Hard { get; set; }
    public int Forgot { get; set; }
}

public class LeaderboardEntry
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
