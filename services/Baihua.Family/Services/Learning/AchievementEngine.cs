using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services;

/// <summary>
/// 成就引擎：检查并颁发成就
/// </summary>
public class AchievementEngine
{
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;
    private readonly ILogger<AchievementEngine> _logger;

    public AchievementEngine(IDbContextFactory<FamilyDbContext> dbFactory, ILogger<AchievementEngine> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// 成就定义列表
    /// </summary>
    public static readonly List<AchievementDef> Definitions = new()
    {
        new("first_step", "👶", "第一步", "完成首次卡片学习", "bronze", "study"),
        new("streak_3", "🔥", "三日不断", "连续学习 3 天", "bronze", "study"),
        new("streak_7", "🔥", "周周坚持", "连续学习 7 天", "silver", "study"),
        new("streak_30", "🔥", "月月不辍", "连续学习 30 天", "gold", "study"),
        new("cards_10", "📚", "十题小试", "累计学习 10 张卡片", "bronze", "study"),
        new("cards_50", "📚", "半百精进", "累计学习 50 张卡片", "silver", "study"),
        new("cards_100", "📚", "百题大关", "累计学习 100 张卡片", "gold", "study"),
        new("cards_500", "📚", "学富五车", "累计学习 500 张卡片", "diamond", "study"),
        new("creator_1", "✏️", "初出茅庐", "首次家长出题", "bronze", "creation"),
        new("creator_10", "✏️", "出题能手", "累计出题 10 道", "silver", "creation"),
        new("explorer_1", "🤖", "初识岐黄", "首次使用 AI 对话", "bronze", "exploration"),
        new("explorer_10", "🤖", "问道十次", "累计使用 AI 对话 10 次", "silver", "exploration"),
        new("accuracy_80", "🎯", "百发百中", "单日正确率达到 80%", "gold", "study"),
        new("early_bird", "🌅", "闻鸡起舞", "早上 6 点前完成学习", "bronze", "study"),
    };

    /// <summary>
    /// 记录学习活动并检查成就
    /// </summary>
    public async Task RecordActivityAsync(int learnerId, string vaultId, string activityType, string? cardId = null, string? result = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        db.StudyActivities.Add(new StudyActivity
        {
            LearnerId = learnerId,
            VaultId = vaultId,
            ActivityType = activityType,
            CardId = cardId,
            Result = result,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // 异步检查成就（不阻塞主流程）
        _ = Task.Run(async () => await CheckAndUnlockAsync(learnerId));
    }

    /// <summary>
    /// 检查并解锁成就
    /// </summary>
    public async Task<List<AchievementDef>> CheckAndUnlockAsync(int learnerId)
    {
        var newlyUnlocked = new List<AchievementDef>();
        using var db = await _dbFactory.CreateDbContextAsync();

        // 使用事务包裹读取-判断-写入，防止并发重复解锁
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // 已解锁的成就 Key（在事务内重新查询，确保一致性）
            var unlockedKeys = await db.Achievements
                .Where(a => a.LearnerId == learnerId)
                .Select(a => a.Key)
                .ToHashSetAsync();

            // 统计指标
            var totalStudy = await db.StudyActivities
                .CountAsync(a => a.LearnerId == learnerId && a.ActivityType == "study");
            var totalCreate = await db.StudyActivities
                .CountAsync(a => a.LearnerId == learnerId && a.ActivityType == "create_card");
            var totalChat = await db.StudyActivities
                .CountAsync(a => a.LearnerId == learnerId && a.ActivityType == "chat");

            // streak 计算
            var streak = await CalculateStreakAsync(db, learnerId);

            // 今日正确率
            var todayAccuracy = await CalculateTodayAccuracyAsync(db, learnerId);

            // 是否早鸟
            var isEarlyBird = await db.StudyActivities
                .AnyAsync(a => a.LearnerId == learnerId && a.CreatedAt.Hour < 6);

            // 检查每个成就
            foreach (var def in Definitions)
            {
                if (unlockedKeys.Contains(def.Key)) continue;

                bool shouldUnlock = def.Key switch
                {
                    "first_step" => totalStudy >= 1,
                    "streak_3" => streak >= 3,
                    "streak_7" => streak >= 7,
                    "streak_30" => streak >= 30,
                    "cards_10" => totalStudy >= 10,
                    "cards_50" => totalStudy >= 50,
                    "cards_100" => totalStudy >= 100,
                    "cards_500" => totalStudy >= 500,
                    "creator_1" => totalCreate >= 1,
                    "creator_10" => totalCreate >= 10,
                    "explorer_1" => totalChat >= 1,
                    "explorer_10" => totalChat >= 10,
                    "accuracy_80" => todayAccuracy >= 0.8,
                    "early_bird" => isEarlyBird,
                    _ => false
                };

                if (shouldUnlock)
                {
                    db.Achievements.Add(new Achievement
                    {
                        LearnerId = learnerId,
                        Key = def.Key,
                        Title = def.Title,
                        Description = def.Description,
                        Icon = def.Icon,
                        Tier = def.Tier,
                        Category = def.Category,
                        UnlockedAt = DateTime.UtcNow
                    });
                    newlyUnlocked.Add(def);
                    _logger.LogInformation("学习者 {LearnerId} 解锁成就: {Title}", learnerId, def.Title);
                }
            }

            if (newlyUnlocked.Count > 0)
            {
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return newlyUnlocked;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "成就检查事务失败: LearnerId={LearnerId}", learnerId);
            throw;
        }
    }

    /// <summary>
    /// 获取学习者成就列表
    /// </summary>
    public async Task<List<AchievementViewModel>> GetAchievementsAsync(int learnerId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var unlocked = await db.Achievements
            .Where(a => a.LearnerId == learnerId)
            .ToListAsync();

        var unlockedKeys = unlocked.Select(a => a.Key).ToHashSet();

        return Definitions.Select(def => new AchievementViewModel
        {
            Key = def.Key,
            Title = def.Title,
            Description = def.Description,
            Icon = def.Icon,
            Tier = def.Tier,
            Category = def.Category,
            IsUnlocked = unlockedKeys.Contains(def.Key),
            UnlockedAt = unlocked.FirstOrDefault(a => a.Key == def.Key)?.UnlockedAt
        }).ToList();
    }

    private async Task<int> CalculateStreakAsync(FamilyDbContext db, int learnerId)
    {
        // 按天统计学习次数
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

    private async Task<double> CalculateTodayAccuracyAsync(FamilyDbContext db, int learnerId)
    {
        var today = DateTime.UtcNow.Date;
        var records = await db.StudyActivities
            .Where(a => a.LearnerId == learnerId && a.ActivityType == "study"
                        && a.CreatedAt.Date == today && a.Result != null)
            .ToListAsync();

        if (records.Count == 0) return 0;
        var rememberCount = records.Count(r => r.Result == "remember");
        return (double)rememberCount / records.Count;
    }
}
