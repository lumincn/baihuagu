using System.Security.Cryptography;
using System.Text.Json;
using TaskRunner.Contracts.Anki;
using TaskRunner.Helpers;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services;

public partial class DailyCardService
{
    private HashSet<string> GetTodayStudiedIds(string vaultId)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var today = DateTime.UtcNow.Date;
            var ids = db.StudyActivities
                .Where(a => a.VaultId == vaultId && a.ActivityType == "study" && a.CreatedAt.Date == today && a.CardId != null)
                .Select(a => a.CardId!)
                .ToHashSet();
            if (ids.Count > 0) return ids;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "从 SQLite 获取今日已学失败，回退文件系统");
        }

        try
        {
            var studyDir = _cardRepo.GetStudyDir(vaultId);
            if (string.IsNullOrEmpty(studyDir)) return new HashSet<string>();

            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var file = Path.Combine(studyDir, $"daily-{today}.json");
            if (!File.Exists(file)) return new HashSet<string>();

            var daily = ReadDailyRecord(file);
            return new HashSet<string>(daily.Answers.Keys);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "从文件读取今日已学ID失败"); return new HashSet<string>(); }
    }

    private int CalculateStreakFromDb(string vaultId)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var dates = db.StudyActivities
                .Where(a => a.VaultId == vaultId && a.ActivityType == "study")
                .Select(a => a.CreatedAt.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

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
        catch
        {
            return 0;
        }
    }

    private int CalculateStreakFromFiles(string vaultId)
    {
        var studyDir = _cardRepo.GetStudyDir(vaultId);
        if (string.IsNullOrEmpty(studyDir) || !Directory.Exists(studyDir)) return 0;

        int streak = 0;
        for (int i = 0; ; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            var file = Path.Combine(studyDir, $"daily-{date:yyyy-MM-dd}.json");
            if (!File.Exists(file))
            {
                if (i > 0) break;
                continue;
            }

            var daily = ReadDailyRecord(file);
            if (daily.Completed > 0)
                streak++;
            else if (i > 0)
                break;
        }
        return streak;
    }

    private DailyRecord ReadDailyRecord(string file)
    {
        if (!File.Exists(file)) return new DailyRecord();
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<DailyRecord>(json) ?? new DailyRecord();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "读取每日记录失败: {File}", file); return new DailyRecord(); }
    }

    private void WriteDailyRecord(string file, DailyRecord record)
    {
        var json = JsonSerializer.Serialize(record, JsonHelper.IndentedUnicode);
        File.WriteAllText(file, json);
    }

    /// <summary>
    /// 更新卡片复习状态（艾宾浩斯调度）
    /// </summary>
    private async Task UpdateReviewStateAsync(string vaultId, string cardId, string result)
    {
        try
        {
            var learner = await _learnerService.GetDefaultAsync();
            var learnerId = learner?.Id ?? 0;
            if (learnerId == 0) return;

            using var db = await _dbFactory.CreateDbContextAsync();
            var state = await db.CardReviewStates
                .FirstOrDefaultAsync(r => r.LearnerId == learnerId && r.VaultId == vaultId && r.CardId == cardId);

            if (state == null)
            {
                state = new CardReviewState
                {
                    LearnerId = learnerId,
                    VaultId = vaultId,
                    CardId = cardId,
                    IntervalDays = 1,
                    NextReviewDate = DateTime.UtcNow.Date.AddDays(1),
                    ConsecutiveRemember = 0,
                    TotalReviews = 1,
                    LastResult = result
                };
                db.CardReviewStates.Add(state);
            }
            else
            {
                state.TotalReviews++;
                state.LastResult = result;

                switch (result?.ToLower())
                {
                    case "remember":
                        state.ConsecutiveRemember++;
                        var idx = Math.Min(state.ConsecutiveRemember - 1, _intervalSequence.Length - 1);
                        state.IntervalDays = _intervalSequence[idx];
                        break;
                    case "hard":
                        // 间隔不变，连续记得次数不变
                        break;
                    case "forgot":
                        state.ConsecutiveRemember = 0;
                        state.IntervalDays = 1;
                        break;
                }

                state.NextReviewDate = DateTime.UtcNow.Date.AddDays(state.IntervalDays);
                db.CardReviewStates.Update(state);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "更新复习状态失败（不影响主流程）");
        }
    }
}
