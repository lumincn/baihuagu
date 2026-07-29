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
    public async Task<bool> RecordAnswerAsync(string vaultId, string cardId, string result)
    {
        var fileOk = WriteFileRecord(vaultId, cardId, result);
        var dbOk = await WriteDbRecordAsync(vaultId, cardId, result);
        await UpdateReviewStateAsync(vaultId, cardId, result);
        return fileOk || dbOk;
    }

    private bool WriteFileRecord(string vaultId, string cardId, string result)
    {
        try
        {
            var studyDir = _cardRepo.GetStudyDir(vaultId);
            if (string.IsNullOrEmpty(studyDir)) return false;

            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var dailyFile = Path.Combine(studyDir, $"daily-{today}.json");

            // 按 vault+date 加内存锁，防止并发覆盖
            var lockKey = $"{vaultId}:{today}";
            var fileLock = _fileLocks.GetOrAdd(lockKey, _ => new object());
            lock (fileLock)
            {
                var daily = ReadDailyRecord(dailyFile);
                if (!daily.Answers.ContainsKey(cardId))
                {
                    daily.Answers[cardId] = result;
                    daily.Completed++;
                }
                else
                {
                    daily.Answers[cardId] = result;
                }

                WriteDailyRecord(dailyFile, daily);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "文件系统记录学习结果失败（非关键）");
            return false;
        }
    }

    private async Task<bool> WriteDbRecordAsync(string vaultId, string cardId, string result)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var learner = await _learnerService.GetDefaultAsync();
            var learnerId = learner?.Id ?? 0;
            if (learnerId == 0)
            {
                // 没有学习者时自动创建一个默认学习者
                var newLearner = await _learnerService.CreateAsync("默认学习者");
                learnerId = newLearner.Id;
            }

            db.StudyActivities.Add(new StudyActivity
            {
                LearnerId = learnerId,
                VaultId = vaultId,
                ActivityType = "study",
                CardId = cardId,
                Result = result,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite 记录学习结果失败");
            return false;
        }
    }

    /// <summary>
    /// 获取今日学习进度（优先 SQLite，回退文件系统）
    /// </summary>
    public DailyProgress GetTodayProgress(string vaultId)
    {
        try
        {
            var progress = GetTodayProgressFromDb(vaultId);
            if (progress != null) return progress;
            return GetTodayProgressFromFiles(vaultId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取今日进度失败");
            return new DailyProgress();
        }
    }

    private DailyProgress? GetTodayProgressFromDb(string vaultId)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var today = DateTime.UtcNow.Date;
            var todayCount = db.StudyActivities
                .Count(a => a.VaultId == vaultId && a.ActivityType == "study" && a.CreatedAt.Date == today);

            var cardsPath = _cardRepo.ResolveCardsPath(vaultId);
            var totalCards = 0;
            if (!string.IsNullOrEmpty(cardsPath) && Directory.Exists(cardsPath))
            {
                totalCards = _cardRepo.LoadAllCards(cardsPath).Count;
            }

            var streak = CalculateStreakFromDb(vaultId);

            return new DailyProgress
            {
                Completed = todayCount,
                Target = Math.Min(10, Math.Max(3, totalCards > 0 ? totalCards / 10 : 5)),
                TotalCards = totalCards,
                Streak = streak
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "从 SQLite 获取今日进度失败，回退到文件系统");
            return null;
        }
    }

    private DailyProgress GetTodayProgressFromFiles(string vaultId)
    {
        var studyDir = _cardRepo.GetStudyDir(vaultId);
        if (string.IsNullOrEmpty(studyDir)) return new DailyProgress();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var dailyFile = Path.Combine(studyDir, $"daily-{today}.json");
        var daily = ReadDailyRecord(dailyFile);

        var cardsPath = _cardRepo.ResolveCardsPath(vaultId);
        var totalCards = 0;
        if (!string.IsNullOrEmpty(cardsPath) && Directory.Exists(cardsPath))
        {
            totalCards = _cardRepo.LoadAllCards(cardsPath).Count;
        }

        var streak = CalculateStreakFromFiles(vaultId);

        return new DailyProgress
        {
            Completed = daily.Completed,
            Target = Math.Min(10, Math.Max(3, totalCards > 0 ? totalCards / 10 : 5)),
            TotalCards = totalCards,
            Streak = streak
        };
    }
}
