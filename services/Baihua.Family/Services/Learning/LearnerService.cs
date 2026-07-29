using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services;

/// <summary>
/// 学习者档案管理服务
/// </summary>
public class LearnerService
{
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;
    private readonly ILogger<LearnerService> _logger;

    public LearnerService(IDbContextFactory<FamilyDbContext> dbFactory, ILogger<LearnerService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<LearnerProfile>> GetAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.LearnerProfiles.OrderBy(l => l.Id).ToListAsync();
    }

    public async Task<LearnerProfile?> GetDefaultAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.LearnerProfiles.FirstOrDefaultAsync(l => l.IsDefault)
               ?? await db.LearnerProfiles.FirstOrDefaultAsync();
    }

    public async Task<LearnerProfile?> GetByIdAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.LearnerProfiles.FindAsync(id);
    }

    public async Task<LearnerProfile> CreateAsync(string name, string avatarEmoji = "👤", string color = "#007bff")
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learner = new LearnerProfile
        {
            Name = name.Trim(),
            AvatarEmoji = avatarEmoji,
            Color = color,
            IsDefault = !await db.LearnerProfiles.AnyAsync()
        };
        db.LearnerProfiles.Add(learner);
        await db.SaveChangesAsync();
        _logger.LogInformation("创建学习者: {Name}", name);
        return learner;
    }

    public async Task<bool> SetDefaultAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var all = await db.LearnerProfiles.ToListAsync();
        foreach (var l in all) l.IsDefault = (l.Id == id);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var learner = await db.LearnerProfiles.FindAsync(id);
        if (learner == null) return false;
        db.LearnerProfiles.Remove(learner);
        await db.SaveChangesAsync();
        return true;
    }
}
