using Microsoft.EntityFrameworkCore;
using Baihua.Data;
using Baihua.AI.Provider;

namespace Baihua.Family.Services;

public class StartupOrchestratorHostedService : IHostedService
{
    private readonly IDbContextFactory<FamilyDbContext> _familyDbContextFactory;
    private readonly IDbContextFactory<VaultDbContext> _vaultDbContextFactory;
    private readonly IDbContextFactory<AIDbContext> _aiDbContextFactory;
    private readonly VaultSettingsService _vaultSettings;
    private readonly LocalModelSettingsService _localModelSettings;
    private readonly ILogger<StartupOrchestratorHostedService> _logger;

    public StartupOrchestratorHostedService(
        IDbContextFactory<FamilyDbContext> familyDbContextFactory,
        IDbContextFactory<VaultDbContext> vaultDbContextFactory,
        IDbContextFactory<AIDbContext> aiDbContextFactory,
        VaultSettingsService vaultSettings,
        LocalModelSettingsService localModelSettings,
        ILogger<StartupOrchestratorHostedService> logger)
    {
        _familyDbContextFactory = familyDbContextFactory;
        _vaultDbContextFactory = vaultDbContextFactory;
        _aiDbContextFactory = aiDbContextFactory;
        _vaultSettings = vaultSettings;
        _localModelSettings = localModelSettings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            LoadFromDatabase();
            _localModelSettings.LoadLocalModelConfigFromFile();
            TrySyncVaultsOnStartup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动编排部分失败，已记录错误但继续启动应用");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void LoadFromDatabase()
    {
        TryMigrateDatabase("Family", () =>
        {
            using var familyDb = _familyDbContextFactory.CreateDbContext();
            MigrateDatabase(familyDb, "Family");
        }, () =>
        {
            using var familyDb = _familyDbContextFactory.CreateDbContext();
            familyDb.Database.EnsureCreated();
        });

        TryMigrateDatabase("Vault", () =>
        {
            using var vaultDb = _vaultDbContextFactory.CreateDbContext();
            MigrateDatabase(vaultDb, "Vault");
        }, () =>
        {
            using var vaultDb = _vaultDbContextFactory.CreateDbContext();
            vaultDb.Database.EnsureCreated();
        });

        TryMigrateDatabase("AI", () =>
        {
            using var aiDb = _aiDbContextFactory.CreateDbContext();
            MigrateDatabase(aiDb, "AI");
        }, () =>
        {
            using var aiDb = _aiDbContextFactory.CreateDbContext();
            aiDb.Database.EnsureCreated();
        });
    }

    private void TryMigrateDatabase(string domainName, Action migrateAction, Action ensureCreatedFallback)
    {
        try
        {
            migrateAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Domain} Migrate 失败，改用 EnsureCreated 补偿", domainName);
            try { ensureCreatedFallback(); _logger.LogInformation("{Domain} EnsureCreated 完成"); }
            catch (Exception ex2) { _logger.LogError(ex2, "{Domain} EnsureCreated 也失败"); }
        }
    }

    private void MigrateDatabase(DbContext dbContext, string domainName)
    {
        try
        {
            // 清空 SQLite 连接池，避免同进程先前打开的连接持有读锁导致 BEGIN EXCLUSIVE 自锁
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            dbContext.Database.Migrate();
            Baihua.Core.Data.SqliteSetup.EnableWal(dbContext, _logger);
            _logger.LogDebug("{Domain} migrate completed successfully", domainName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Domain} 数据库迁移失败: {Message}", domainName, ex.Message);
            throw;
        }
    }

    private void TrySyncVaultsOnStartup()
    {
        var rootPath = _vaultSettings.VaultRootPathPreference;
        if (string.IsNullOrWhiteSpace(rootPath)) return;
        if (!Directory.Exists(rootPath)) return;

        try
        {
            var (added, removed) = _vaultSettings.SyncVaultsWithFilesystem(rootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时自动同步知识库失败");
        }
    }
}
