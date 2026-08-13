using Microsoft.EntityFrameworkCore;
using Baihua.Data;
using Baihua.Data.Entities;

namespace Baihua.Family.Services.Todo;

/// <summary>
/// 个人待办事项服务（单用户、极简：标题 + 完成状态）。
/// </summary>
public class TodoService
{
    private readonly IDbContextFactory<FamilyDbContext> _dbFactory;

    public TodoService(IDbContextFactory<FamilyDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>获取全部待办（按创建顺序）</summary>
    public async Task<List<TodoItem>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TodoItems.OrderBy(t => t.Id).ToListAsync(ct);
    }

    /// <summary>创建待办。标题空白或超长时返回 null（由控制器转 400）</summary>
    public async Task<TodoItem?> CreateAsync(string title, CancellationToken ct = default)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > 200)
            return null;

        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = new TodoItem { Title = trimmed };
        db.TodoItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>
    /// 更新待办（标题/完成状态至少传一项，调用方保证标题已校验）。
    /// 不存在时返回 null。
    /// </summary>
    public async Task<TodoItem?> UpdateAsync(int id, string? title, bool? isDone, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.TodoItems.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (item == null)
            return null;

        if (title != null)
        {
            item.Title = title.Trim();
        }

        if (isDone.HasValue && item.IsDone != isDone.Value)
        {
            item.IsDone = isDone.Value;
            item.CompletedAt = isDone.Value ? DateTime.UtcNow : null;
        }

        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>删除待办。不存在时返回 false</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.TodoItems.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (item == null)
            return false;

        db.TodoItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
