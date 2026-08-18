using Baihua.Core.Models;
using Baihua.Core.Services;
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
    private readonly MigrationService _migrationService;
    private readonly ILogger<StartupOrchestratorHostedService> _logger;

    public StartupOrchestratorHostedService(
        IDbContextFactory<FamilyDbContext> familyDbContextFactory,
        IDbContextFactory<VaultDbContext> vaultDbContextFactory,
        IDbContextFactory<AIDbContext> aiDbContextFactory,
        VaultSettingsService vaultSettings,
        LocalModelSettingsService localModelSettings,
        MigrationService migrationService,
        ILogger<StartupOrchestratorHostedService> logger)
    {
        _familyDbContextFactory = familyDbContextFactory;
        _vaultDbContextFactory = vaultDbContextFactory;
        _aiDbContextFactory = aiDbContextFactory;
        _vaultSettings = vaultSettings;
        _localModelSettings = localModelSettings;
        _migrationService = migrationService;
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

        // AI 库 schema 由 AI 服务独占迁移（两进程并发 Migrate 会因 BEGIN EXCLUSIVE 自锁卡死）。
        // Family 只等待其就绪（表存在即视为已迁移），绝不迁移/EnsureCreated AI 库；
        // 就绪后顺便执行 API Key 加密密钥迁移（保证 .baihua-key 与 AI 服务一致）。
        TryWaitForAiDatabase();
        TryMigrateApiKeys();
    }

    /// <summary>等待 AI 服务完成 ai.db 迁移（轮询只读检查关键表，最多 30s）。</summary>
    private void TryWaitForAiDatabase()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var aiDb = _aiDbContextFactory.CreateDbContext();
                var tables = aiDb.Database.SqlQueryRaw<string>(
                        "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('AiProviderSettings','__EFMigrationsHistory')")
                    .ToList();
                if (tables.Count >= 2)
                {
                    _logger.LogInformation("AI 数据库 schema 已就绪（由 AI 服务迁移）");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "检查 AI 数据库 schema 失败（AI 服务可能尚未创建库）");
            }
            Thread.Sleep(2000);
        }
        _logger.LogWarning("等待 AI 服务迁移超时（30s）：AI 配置读取将回退到 appsettings，AI 服务就绪后自动恢复");
    }

    /// <summary>API Key 加密密钥迁移（ai.db schema 就绪后执行；与 AI 服务共用同一固定密钥）。</summary>
    private void TryMigrateApiKeys()
    {
        try
        {
            using var aiDb = _aiDbContextFactory.CreateDbContext();
            _migrationService.MigrateApiKeysIfNeeded(aiDb);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API Key 加密密钥迁移失败（不影响启动）");
        }
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
