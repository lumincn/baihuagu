using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Services.Todo;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Baihua.Family.Tests.Services;

/// <summary>
/// 个人待办清单服务测试（单用户、极简：标题 + 完成状态）。
/// </summary>
public class TodoServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly TodoService _service;
    private readonly IDbContextFactory<FamilyDbContext> _factory;

    public TodoServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<FamilyDbContext>()
            .UseSqlite(_conn).Options;
        using (var ctx = new FamilyDbContext(options)) ctx.Database.EnsureCreated();
        _factory = new TestDbFactory(options);
        _service = new TodoService(_factory);
    }

    public void Dispose() => _conn.Dispose();

    // ============ 创建 ============

    [Fact]
    public async Task Create_TrimsAndPersists()
    {
        var item = await _service.CreateAsync("  买牛奶  ");

        Assert.NotNull(item);
        Assert.Equal("买牛奶", item!.Title);
        Assert.False(item.IsDone);
        Assert.Null(item.CompletedAt);

        using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.TodoItems.CountAsync());
    }

    [Fact]
    public async Task Create_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(await _service.CreateAsync(""));
        Assert.Null(await _service.CreateAsync("   "));
        Assert.Null(await _service.CreateAsync(null!));
    }

    [Fact]
    public async Task Create_TooLong_ReturnsNull()
    {
        var tooLong = new string('长', 201);
        Assert.Null(await _service.CreateAsync(tooLong));
    }

    // ============ 查询 ============

    [Fact]
    public async Task GetAll_ReturnsInCreationOrder()
    {
        var a = await _service.CreateAsync("第一项");
        var b = await _service.CreateAsync("第二项");

        var items = await _service.GetAllAsync();

        Assert.Equal(2, items.Count);
        Assert.Equal("第一项", items[0].Title);
        Assert.Equal("第二项", items[1].Title);
        Assert.True(items[0].Id < items[1].Id);
    }

    // ============ 更新 ============

    [Fact]
    public async Task Update_ToggleDone_SetsAndClearsCompletedAt()
    {
        var created = (await _service.CreateAsync("锻炼"))!;

        var done = await _service.UpdateAsync(created.Id, null, true);
        Assert.NotNull(done);
        Assert.True(done!.IsDone);
        Assert.NotNull(done.CompletedAt);

        var undone = await _service.UpdateAsync(created.Id, null, false);
        Assert.NotNull(undone);
        Assert.False(undone!.IsDone);
        Assert.Null(undone.CompletedAt);
    }

    [Fact]
    public async Task Update_Rename_TrimsTitle()
    {
        var created = (await _service.CreateAsync("旧标题"))!;

        var renamed = await _service.UpdateAsync(created.Id, "  新标题  ", null);

        Assert.NotNull(renamed);
        Assert.Equal("新标题", renamed!.Title);
        Assert.False(renamed.IsDone);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNull()
    {
        Assert.Null(await _service.UpdateAsync(9999, "不存在", null));
        Assert.Null(await _service.UpdateAsync(9999, null, true));
    }

    // ============ 删除 ============

    [Fact]
    public async Task Delete_Existing_RemovesAndReturnsTrue()
    {
        var created = (await _service.CreateAsync("要删除的"))!;

        var deleted = await _service.DeleteAsync(created.Id);

        Assert.True(deleted);
        using var db = _factory.CreateDbContext();
        Assert.Equal(0, await db.TodoItems.CountAsync());
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsFalse()
    {
        Assert.False(await _service.DeleteAsync(9999));
    }

    private sealed class TestDbFactory(DbContextOptions<FamilyDbContext> options) : IDbContextFactory<FamilyDbContext>
    {
        public FamilyDbContext CreateDbContext() => new(options);

        public Task<FamilyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
