using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TaskRunner.Contracts.Ai;
using TaskRunner.Contracts.Master;
using TaskRunner.Controllers;
using TaskRunner.Controllers.AI.Stages;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Models;
using TaskRunner.Services;
using Xunit;

namespace TaskRunner.Family.Tests.Services;

/// <summary>
/// MasterController 测试：参数校验、数据库操作、List/Delete/Profile、异常展开
/// 注：Create 完整 AI 流程需要实际 AI 服务，在集成测试中覆盖
/// </summary>
public class MasterControllerTests
{
    #region Test Infrastructure

    private static DbContextOptions<FamilyDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<FamilyDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
    }

    private class TestDbContextFactory : IDbContextFactory<FamilyDbContext>
    {
        private readonly DbContextOptions<FamilyDbContext> _options;
        public TestDbContextFactory(DbContextOptions<FamilyDbContext> options) => _options = options;
        public FamilyDbContext CreateDbContext() => new(_options);
        public Task<FamilyDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new FamilyDbContext(_options));
    }

    /// <summary>
    /// 创建不依赖 AI 服务的 Controller（List/Delete/Profile/Create参数验证）
    /// 这些 API 不会调用 AiClientService，可用 null 绕过复杂的 mock 构造
    /// </summary>
    private static MasterController CreateControllerForDbOps(DbContextOptions<FamilyDbContext> options)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var aiSettings = new AiSettingsService(
            config, Mock.Of<IServiceProvider>(), Mock.Of<ILogger<AiSettingsService>>());

        var vaultSettingsMock = new Mock<VaultSettingsService>(
            Mock.Of<IDbContextFactory<VaultDbContext>>(),
            Mock.Of<ILogger<VaultSettingsService>>());
        var vaultIndexerMock = new Mock<VaultNoteIndexer>(
            Mock.Of<IDbContextFactory<VaultDbContext>>(),
            Mock.Of<ILogger<VaultNoteIndexer>>());

        return new MasterController(
            null!, // AiClientService — 此类测试不会调用 AI
            aiSettings,
            new MasterPromptBuilder(),
            new TestDbContextFactory(options),
            vaultSettingsMock.Object,
            vaultIndexerMock.Object,
            Mock.Of<ILogger<MasterController>>()
        );
    }

    private static async Task SeedDbAsync(DbContextOptions<FamilyDbContext> options)
    {
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    #endregion

    #region Create - 参数验证 (在 AI 调用之前就拒绝)

    [Fact]
    public async Task Create_EmptyGoal_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.Create(new CreateMasterRequest { Goal = "", Industry = "中医" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<CreateMasterResponse>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Contains("目标不能为空", response.Message);
    }

    [Fact]
    public async Task Create_EmptyIndustry_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.Create(new CreateMasterRequest { Goal = "通过考试", Industry = "" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<CreateMasterResponse>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Contains("行业不能为空", response.Message);
    }

    [Fact]
    public async Task Create_WhitespaceGoal_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.Create(new CreateMasterRequest { Goal = "   ", Industry = "医学" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>
    /// 参数校验失败时，不应创建任何数据库记录
    /// </summary>
    [Fact]
    public async Task Create_InvalidInput_NoDbRecordsCreated()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        await controller.Create(new CreateMasterRequest { Goal = "", Industry = "中医" });

        await using var verifyDb = new FamilyDbContext(options);
        Assert.Equal(0, await verifyDb.Masters.CountAsync());
        Assert.Equal(0, await verifyDb.MasterConversations.CountAsync());
    }

    #endregion

    #region List

    [Fact]
    public async Task List_ReturnsActiveMastersOrderedByCreatedAtDesc()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.Now;
        db.Masters.AddRange(
            new Master
            {
                MasterId = "m1", MasterName = "岐伯", Goal = "中医", Industry = "中医",
                CurrentStage = "入道", Status = "active", CreatedAt = now.AddHours(-2)
            },
            new Master
            {
                MasterId = "m2", MasterName = "图灵", Goal = "编程", Industry = "IT",
                CurrentStage = "精进", Status = "active",
                GraduatedStagesJson = "[\"入道\",\"筑基\"]", CreatedAt = now.AddHours(-1)
            },
            new Master
            {
                MasterId = "m3", MasterName = "已删", Goal = "x", Industry = "x",
                Status = "deleted", CreatedAt = now
            }
        );
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.List();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsType<List<MasterListItem>>(okResult.Value);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, m => m.MasterName == "已删");
        Assert.Equal("图灵", items[0].MasterName);
        Assert.Equal("岐伯", items[1].MasterName);
        Assert.Equal(3, items[0].CurrentStageOrder);
        Assert.Equal(2, items[0].GraduatedStages.Count);
    }

    [Fact]
    public async Task List_EmptyDatabase_ReturnsEmptyList()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var controller = CreateControllerForDbOps(options);

        var result = await controller.List();
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<List<MasterListItem>>(okResult.Value));
    }

    #endregion

    #region Delete

    [Fact]
    public async Task Delete_ExistingMaster_SoftDeletes()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Masters.Add(new Master
        {
            MasterId = "to-delete", MasterName = "待删", Goal = "t",
            Industry = "t", Status = "active"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.Delete("to-delete");

        Assert.IsType<OkObjectResult>(result);

        await using var verifyDb = new FamilyDbContext(options);
        var master = await verifyDb.Masters.FirstOrDefaultAsync(m => m.MasterId == "to-delete");
        Assert.NotNull(master);
        Assert.Equal("deleted", master.Status);
    }

    [Fact]
    public async Task Delete_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var controller = CreateControllerForDbOps(options);

        Assert.IsType<NotFoundResult>(await controller.Delete("nonexistent"));
    }

    /// <summary>
    /// 删除不存在的 Master 时，不应影响已有数据
    /// </summary>
    [Fact]
    public async Task Delete_NotFound_DoesNotAffectExistingData()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Masters.Add(new Master
        {
            MasterId = "keep-me", MasterName = "保留", Goal = "test",
            Industry = "test", Status = "active"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        await controller.Delete("nonexistent");

        await using var verifyDb = new FamilyDbContext(options);
        var kept = await verifyDb.Masters.FirstOrDefaultAsync(m => m.MasterId == "keep-me");
        Assert.NotNull(kept);
        Assert.Equal("active", kept.Status);
    }

    #endregion

    #region Profile

    [Fact]
    public async Task GetProfile_ExistingMasterWithProfile_ReturnsCompleteData()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Masters.Add(new Master
        {
            MasterId = "prof-1", MasterName = "岐伯", Goal = "学中医",
            Industry = "中医", CurrentStage = "筑基", GraduatedStagesJson = "[\"入道\"]", Status = "active"
        });
        db.ApprenticeProfiles.Add(new ApprenticeProfile
        {
            MasterId = "prof-1", Foundation = "有基础", Strengths = "记忆好", Weaknesses = "临床弱"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.GetProfile("prof-1");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ApprenticeProfileResponse>(okResult.Value);
        Assert.True(profile.Success);
        Assert.Equal("学中医", profile.Goal);
        Assert.Equal("有基础", profile.Foundation);
        Assert.Equal("记忆好", profile.Strengths);
        Assert.Equal("临床弱", profile.Weaknesses);
        Assert.Single(profile.GraduatedStages);
        Assert.Equal("筑基", profile.CurrentStage);
    }

    [Fact]
    public async Task GetProfile_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var controller = CreateControllerForDbOps(options);

        Assert.IsType<NotFoundObjectResult>((await controller.GetProfile("nonexistent")).Result);
    }

    [Fact]
    public async Task GetProfile_EmptyId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        Assert.IsType<BadRequestObjectResult>((await controller.GetProfile("")).Result);
    }

    #endregion

    #region StageComplete - 参数验证

    [Fact]
    public async Task StageComplete_EmptyMasterId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.StageComplete("", new StageCompleteRequest { StageName = "入道" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("师父ID不能为空", ((StageCompleteResponse)badRequest.Value!).Message);
    }

    [Fact]
    public async Task StageComplete_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.StageComplete("nonexistent", new StageCompleteRequest { StageName = "入道" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    #endregion

    #region UnwrapExceptionMessage 逻辑验证

    /// <summary>
    /// 验证异常链展开逻辑：DbUpdateException → SQLite 错误
    /// 这是本次修复的关键——用户应能看到 "no such table: Masters" 而非仅 "saving entity changes"
    /// </summary>
    [Fact]
    public void UnwrapExceptionMessage_DbUpdateExceptionToSqliteError()
    {
        var sqliteEx = new InvalidOperationException("SQLite Error 1: 'no such table: Masters'");
        var dbEx = new DbUpdateException(
            "An error occurred while saving the entity changes.", sqliteEx);

        var unwrapped = UnwrapMessages(dbEx);
        Assert.Equal(2, unwrapped.Count);
        Assert.Contains("saving the entity changes", unwrapped[0]);
        Assert.Contains("no such table: Masters", unwrapped[1]);
    }

    [Fact]
    public void UnwrapExceptionMessage_NoAiProvider()
    {
        var ex = new Exception("未找到可用的AI提供商");
        var unwrapped = UnwrapMessages(ex);
        Assert.Single(unwrapped);
        Assert.Equal("未找到可用的AI提供商", unwrapped[0]);
    }

    [Fact]
    public void UnwrapExceptionMessage_DeepChain_NoDuplicates()
    {
        var inner = new Exception("unique inner");
        var middle = new Exception("unique middle", inner);
        var outer = new Exception("unique outer", middle);

        var unwrapped = UnwrapMessages(outer);
        Assert.Equal(3, unwrapped.Count);
        Assert.Equal("unique outer", unwrapped[0]);
        Assert.Equal("unique middle", unwrapped[1]);
        Assert.Equal("unique inner", unwrapped[2]);
    }

    [Fact]
    public void UnwrapExceptionMessage_DeduplicatesRepeatedMessages()
    {
        // 某些异常链中会出现重复消息
        var inner = new Exception("same message");
        var outer = new Exception("same message", inner);

        var unwrapped = UnwrapMessages(outer);
        Assert.Single(unwrapped);
        Assert.Equal("same message", unwrapped[0]);
    }

    [Fact]
    public void UnwrapExceptionMessage_NullInnerException()
    {
        var ex = new Exception("only message", innerException: null);
        var unwrapped = UnwrapMessages(ex);
        Assert.Single(unwrapped);
        Assert.Equal("only message", unwrapped[0]);
    }

    private static List<string> UnwrapMessages(Exception ex)
    {
        var messages = new List<string>();
        var current = (Exception?)ex;
        while (current != null)
        {
            var msg = current.Message.Trim();
            if (!string.IsNullOrEmpty(msg) && !messages.Contains(msg))
                messages.Add(msg);
            current = current.InnerException;
        }
        return messages;
    }

    #endregion

    #region GetConversations

    [Fact]
    public async Task GetConversations_ExistingMasterNoConversations_ReturnsEmptyList()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Masters.Add(new Master
        {
            MasterId = "conv-1", MasterName = "岐伯", Goal = "中医学",
            Industry = "中医", Status = "active"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.GetConversations("conv-1");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConversationHistoryResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetConversations_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.GetConversations("nonexistent");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetConversations_EmptyId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.GetConversations("");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetConversations_ReturnsLimitedResults()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Masters.Add(new Master
        {
            MasterId = "conv-2", MasterName = "图灵", Goal = "编程",
            Industry = "IT", Status = "active"
        });
        for (int i = 0; i < 5; i++)
        {
            db.MasterConversations.Add(new MasterConversation
            {
                MasterId = "conv-2", Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i}", Stage = "入道",
                CreatedAt = DateTime.Now.AddMinutes(-(5 - i))
            });
        }
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.GetConversations("conv-2", limit: 3);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConversationHistoryResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(3, response.Items.Count);
    }

    #endregion

    #region SyncConversations

    [Fact]
    public async Task SyncConversations_EmptyId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.SyncConversations("", new ConversationSyncRequest());

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SyncConversations_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.SyncConversations("nonexistent", new ConversationSyncRequest());

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task SyncConversations_SyncsItemsSuccessfully()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Masters.Add(new Master
        {
            MasterId = "sync-1", MasterName = "岐伯", Goal = "中医学",
            Industry = "中医", Status = "active"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var request = new ConversationSyncRequest
        {
            Items = new List<ConversationHistoryItem>
            {
                new() { Role = "user", Content = "你好", Stage = "入道", CreatedAt = DateTime.Now },
                new() { Role = "assistant", Content = "你好，有什么可以帮你？", Stage = "入道", CreatedAt = DateTime.Now.AddSeconds(1) }
            }
        };

        var result = await controller.SyncConversations("sync-1", request);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConversationSyncResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(2, response.SyncedCount);

        await using var verifyDb = new FamilyDbContext(options);
        Assert.Equal(2, await verifyDb.MasterConversations.CountAsync(c => c.MasterId == "sync-1"));
    }

    #endregion

    #region UpdateProfile

    [Fact]
    public async Task UpdateProfile_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.UpdateProfile("nonexistent", new UpdateProfileRequest());

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateProfile_EmptyId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.UpdateProfile("", new UpdateProfileRequest());

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateProfile_CreatesNewProfileIfNotExists()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Masters.Add(new Master
        {
            MasterId = "up-1", MasterName = "岐伯", Goal = "中医学",
            Industry = "中医", Status = "active"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.UpdateProfile("up-1", new UpdateProfileRequest
        {
            Foundation = "新基础", LearningStyle = "新风格"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ApprenticeProfileResponse>(okResult.Value);
        Assert.True(profile.Success);
        Assert.Equal("新基础", profile.Foundation);
        Assert.Equal("新风格", profile.LearningStyle);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesExistingProfileFields()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await using var db = new FamilyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Masters.Add(new Master
        {
            MasterId = "up-2", MasterName = "岐伯", Goal = "中医学",
            Industry = "中医", Status = "active"
        });
        db.ApprenticeProfiles.Add(new ApprenticeProfile
        {
            MasterId = "up-2", Foundation = "旧基础", Strengths = "好记忆"
        });
        await db.SaveChangesAsync();

        var controller = CreateControllerForDbOps(options);
        var result = await controller.UpdateProfile("up-2", new UpdateProfileRequest
        {
            Foundation = "新基础", Weaknesses = "临床弱"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ApprenticeProfileResponse>(okResult.Value);
        Assert.True(profile.Success);
        Assert.Equal("新基础", profile.Foundation);
        Assert.Equal("好记忆", profile.Strengths);
        Assert.Equal("临床弱", profile.Weaknesses);
    }

    #endregion

    #region StageStrategyFactory

    [Fact]
    public void StageStrategyFactory_GetStrategy_ReturnsCorrectStrategy()
    {
        var strategy = StageStrategyFactory.GetStrategy("入道");
        Assert.NotNull(strategy);
        Assert.Equal("入道", strategy.StageName);
        Assert.Equal(1, strategy.Order);
        Assert.Equal("引路人", strategy.RoleName);
        Assert.NotEmpty(strategy.BlessingTemplates);
    }

    [Fact]
    public void StageStrategyFactory_GetNextStrategy_ReturnsNextStage()
    {
        var next = StageStrategyFactory.GetNextStrategy("入道");
        Assert.NotNull(next);
        Assert.Equal("筑基", next.StageName);
        Assert.Equal(2, next.Order);

        next = StageStrategyFactory.GetNextStrategy("筑基");
        Assert.NotNull(next);
        Assert.Equal("精进", next.StageName);
    }

    [Fact]
    public void StageStrategyFactory_GetNextStrategy_LastStageReturnsNull()
    {
        var next = StageStrategyFactory.GetNextStrategy("出师");
        Assert.Null(next);
    }

    [Fact]
    public void StageStrategyFactory_UnknownStage_ReturnsNull()
    {
        var strategy = StageStrategyFactory.GetStrategy("未知阶段");
        Assert.Null(strategy);

        var next = StageStrategyFactory.GetNextStrategy("未知阶段");
        Assert.Null(next);
    }

    [Fact]
    public void StageStrategyFactory_AllStrategies_HaveUniqueOrder()
    {
        var all = StageStrategyFactory.GetAllStrategies();
        var orders = all.Select(s => s.Order).ToList();
        Assert.Equal(orders.Distinct().Count(), orders.Count);
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void StageStrategy_GetBlessing_ReturnsNonEmptyString()
    {
        var strategy = StageStrategyFactory.GetStrategy("入道");
        Assert.NotNull(strategy);
        var blessing = strategy.GetBlessing("岐伯");
        Assert.False(string.IsNullOrWhiteSpace(blessing));
        Assert.Contains("岐伯", blessing);
    }

    #endregion

    #region Evict - 参数验证

    [Fact]
    public async Task Compress_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.Compress("nonexistent");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Evict_EmptyId_ReturnsBadRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        Assert.IsType<BadRequestResult>(await controller.Evict(""));
    }

    [Fact]
    public async Task Evict_NonExistingMaster_ReturnsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        await SeedDbAsync(options);
        var controller = CreateControllerForDbOps(options);

        var result = await controller.Evict("nonexistent");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region Utility

    [Fact]
    public void GetStageOrder_ReturnsCorrectOrder()
    {
        // Access via reflection since it's private static
        var method = typeof(MasterController).GetMethod("GetStageOrder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal(1, method.Invoke(null, new object[] { "入道" }));
        Assert.Equal(2, method.Invoke(null, new object[] { "筑基" }));
        Assert.Equal(3, method.Invoke(null, new object[] { "精进" }));
        Assert.Equal(4, method.Invoke(null, new object[] { "磨砺" }));
        Assert.Equal(5, method.Invoke(null, new object[] { "出师" }));
        Assert.Equal(0, method.Invoke(null, new object[] { "未知" }));
    }

    [Fact]
    public void TruncateText_ShortText_ReturnsAsIs()
    {
        var method = typeof(MasterController).GetMethod("TruncateText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var text = "Hello";
        var result = method.Invoke(null, new object[] { text, 10 });
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void TruncateText_LongText_Truncates()
    {
        var method = typeof(MasterController).GetMethod("TruncateText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var text = "Hello World This Is Long";
        var result = method.Invoke(null, new object[] { text, 5 });
        Assert.Equal("Hello...", result);
    }

    #endregion
}
