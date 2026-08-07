using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Achievements;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

/// <summary>
/// FAM-21 学习打卡 API
/// </summary>
[ApiController]
[Route("api/checkin")]
public class CheckinController : ControllerBase
{
    private readonly CheckinService _checkinService;

    public CheckinController(CheckinService checkinService)
    {
        _checkinService = checkinService;
    }

    /// <summary>
    /// 学习打卡数据：今日清单 + 连续打卡 + 最近 7 天日历
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CheckinDataDto>> GetCheckinData([FromQuery] string? vaultId = null)
    {
        var data = await _checkinService.GetCheckinDataAsync(vaultId);
        return Ok(new CheckinDataDto
        {
            FamilyStreak = data.FamilyStreak,
            TodayRecords = data.TodayRecords.Select(r => new CheckinRecordDto
            {
                LearnerName = r.LearnerName,
                Content = r.Content,
                Time = r.Time,
                IsCompleted = r.IsCompleted,
                Source = r.Source,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                CardCount = r.CardCount,
                Accuracy = Math.Round(r.Accuracy, 1)
            }).ToList(),
            Last7Days = data.Last7Days.Select(d => new CheckinCalendarDayDto
            {
                Date = d.Date,
                IsChecked = d.IsChecked,
                IsToday = d.IsToday
            }).ToList()
        });
    }
}
