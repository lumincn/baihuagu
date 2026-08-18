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
    private readonly VaultSettingsService _vaultSettings;
    private readonly LocalModelSettingsService _localModelSettings;
    private readonly ILogger<StartupOrchestratorHostedService> _logger;

    public StartupOrchestratorHostedService(
        IDbContextFactory<FamilyDbContext> familyDbContextFactory,
        IDbContextFactory<VaultDbContext> vaultDbContextFactory,
        VaultSettingsService vaultSettings,
        LocalModelSettingsService localModelSettings,
        ILogger<StartupOrchestratorHostedService> logger)
    {
        _familyDbContextFactory = familyDbContextFactory;
        _vaultDbContextFactory = vaultDbContextFactory;
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
        // PostgreSQL：EnsureCreated 生成完整 schema（一服务一数据库，无迁移历史）
        TryEnsureCreated("Family", () =>
        {
            using var familyDb = _familyDbContextFactory.CreateDbContext();
            familyDb.Database.EnsureCreated();
        });

        TryEnsureCreated("Vault", () =>
        {
            using var vaultDb = _vaultDbContextFactory.CreateDbContext();
            vaultDb.Database.EnsureCreated();
        });

        // AI 库 schema 与 API Key 加密密钥迁移完全归 AI 服务独占（一服务一数据库）：
        // Family 不再等待/检查 ai.db，也不再参与 key 迁移（Family 不持有/不解密 key）。
    }

    private void TryEnsureCreated(string domainName, Action action)
    {
        try
        {
            action();
            _logger.LogDebug("{Domain} 数据库初始化完成", domainName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Domain} 数据库初始化失败: {Message}", domainName, ex.Message);
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
