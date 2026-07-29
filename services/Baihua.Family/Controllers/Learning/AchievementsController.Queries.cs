using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Achievements;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class AchievementsController : ControllerBase
{
    private async Task<DashboardDataDto> HandleGetDashboardAsync(string? vaultId)
    {
        var data = await _leaderboardService.GetDashboardAsync(vaultId);
        return new DashboardDataDto
        {
            FamilyStats = data.FamilyStats.Select(s => new FamilyMemberStatDto
            {
                LearnerId = s.LearnerId,
                Name = s.Name,
                AvatarEmoji = s.AvatarEmoji,
                Color = s.Color,
                WeekTotal = s.WeekTotal,
                Accuracy = Math.Round(s.Accuracy, 0),
                Streak = s.Streak,
                TotalCards = s.TotalCards
            }).ToList(),
            WeeklyTrend = data.WeeklyTrend.Select(t => new DailyTrendDto { Date = t.Date, Count = t.Count }).ToList(),
            RecentAchievements = data.RecentAchievements.Select(a => new RecentAchievementDto
            {
                LearnerName = a.LearnerName,
                AvatarEmoji = a.AvatarEmoji,
                Title = a.Title,
                Icon = a.Icon,
                Tier = a.Tier,
                UnlockedAt = a.UnlockedAt
            }).ToList(),
            ResultDistribution = new ResultDistributionDto
            {
                Remember = data.ResultDistribution.Remember,
                Hard = data.ResultDistribution.Hard,
                Forgot = data.ResultDistribution.Forgot
            }
        };
    }

    private static List<LeaderboardEntryDto> ToDtos(List<LeaderboardEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++) entries[i].Rank = i + 1;
        return entries.Select(e => new LeaderboardEntryDto
        {
            LearnerId = e.LearnerId,
            LearnerName = e.LearnerName,
            AvatarEmoji = e.AvatarEmoji,
            Color = e.Color,
            CardsStudied = e.CardsStudied,
            Accuracy = Math.Round(e.Accuracy, 1),
            Score = e.Score,
            Streak = e.Streak,
            Rank = e.Rank
        }).ToList();
    }
}
