using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Shared.Security;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services;

/// <summary>
/// 全量备份恢复服务
/// </summary>
public class RestoreService
{
    private readonly VaultSettingsService _vaultSettings;
    private readonly IDbContextFactory<VaultDbContext> _vaultDbContextFactory;
    private readonly IDbContextFactory<FamilyDbContext> _familyDbContextFactory;
    private readonly IDbContextFactory<AIDbContext> _aiDbContextFactory;
    private readonly ApiKeyProtectionService _apiKeyProtection;
    private readonly DataEncryptionService _dataEncryption;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(
        VaultSettingsService vaultSettings,
        IDbContextFactory<VaultDbContext> vaultDbContextFactory,
        IDbContextFactory<FamilyDbContext> familyDbContextFactory,
        IDbContextFactory<AIDbContext> aiDbContextFactory,
        ApiKeyProtectionService apiKeyProtection,
        DataEncryptionService dataEncryption,
        ILogger<RestoreService> logger)
    {
        _vaultSettings = vaultSettings;
        _vaultDbContextFactory = vaultDbContextFactory;
        _familyDbContextFactory = familyDbContextFactory;
        _aiDbContextFactory = aiDbContextFactory;
        _apiKeyProtection = apiKeyProtection;
        _dataEncryption = dataEncryption;
        _logger = logger;
    }

    /// <summary>
    /// 恢复全量备份
    /// </summary>
    public async Task<FullRestoreResult> RestoreFullBackupAsync(
        string backupPath,
        string? password = null,
        string? vaultRootPathOverride = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            return new FullRestoreResult { Success = false, Error = "备份文件不存在" };
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"dn_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ZipFile.ExtractToDirectory(backupPath, tempDir);

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new FullRestoreResult { Success = false, Error = "无效的备份文件：缺少 manifest.json" };
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);
            if (manifest == null || manifest.Version < 2)
            {
                return new FullRestoreResult { Success = false, Error = "不支持的备份格式版本" };
            }

            if (manifest.HasPassword && string.IsNullOrEmpty(password))
            {
                return new FullRestoreResult { Success = false, Error = "此备份有密码保护，请输入密码" };
            }

            var vaultRootPath = !string.IsNullOrEmpty(vaultRootPathOverride)
                ? vaultRootPathOverride
                : _vaultSettings.VaultRootPathPreference;

            var dbResult = await RestoreDatabaseAsync(tempDir, password, vaultRootPath, overwrite, cancellationToken);
            if (!dbResult)
            {
                return new FullRestoreResult { Success = false, Error = "恢复数据库失败" };
            }

            cancellationToken.ThrowIfCancellationRequested();

            RestoreConfigFiles(tempDir);
            await RestoreVaultFilesAsync(tempDir, vaultRootPath, overwrite, cancellationToken);

            _logger.LogInformation("全量备份恢复成功：{Path}", backupPath);

            return new FullRestoreResult
            {
                Success = true,
                SourcePlatform = manifest.SourcePlatform,
                SourceOS = manifest.SourceOS,
                RestoredAt = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("恢复全量备份已取消");
            return new FullRestoreResult { Success = false, Error = "恢复已取消" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复全量备份失败");
            return new FullRestoreResult { Success = false, Error = ex.Message };
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "清理临时目录失败: {TempDir}", tempDir); }
            }
        }
    }

    private async Task<bool> RestoreDatabaseAsync(string tempDir, string? password, string vaultRootPath, bool overwrite, CancellationToken cancellationToken)
    {
        var dbDir = Path.Combine(tempDir, "db");
        if (!Directory.Exists(dbDir)) return true;

        using var db = _vaultDbContextFactory.CreateDbContext();
        using var familyDb = _familyDbContextFactory.CreateDbContext();
        using var aiDb = _aiDbContextFactory.CreateDbContext();

        cancellationToken.ThrowIfCancellationRequested();

        // Vaults
        var vaultsPath = Path.Combine(dbDir, "vaults.json");
        if (File.Exists(vaultsPath))
        {
            var json = await File.ReadAllTextAsync(vaultsPath, cancellationToken);
            var vaultsData = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (vaultsData != null)
            {
                if (overwrite) db.Vaults.RemoveRange(db.Vaults);

                foreach (var v in vaultsData)
                {
                    var path = v.GetProperty("Path").GetString() ?? "";
                    var relativePath = v.TryGetProperty("RelativePath", out var rp) ? rp.GetString() : null;
                    var finalPath = !string.IsNullOrEmpty(relativePath) && !string.IsNullOrEmpty(vaultRootPath)
                        ? Path.Combine(vaultRootPath, relativePath)
                        : BackupPathHelper.RemapPath(path);

                    var vault = new Vault
                    {
                        VaultId = v.GetProperty("VaultId").GetString() ?? Guid.NewGuid().ToString(),
                        Name = v.GetProperty("Name").GetString() ?? "",
                        Path = finalPath,
                        IsActive = v.TryGetProperty("IsActive", out var ia) && ia.GetBoolean(),
                        CreatedAt = v.GetProperty("CreatedAt").GetDateTime(),
                        UpdatedAt = v.GetProperty("UpdatedAt").GetDateTime()
                    };

                    if (!db.Vaults.Any(x => x.VaultId == vault.VaultId))
                    {
                        db.Vaults.Add(vault);
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // AiProviderSettings
        var providersPath = Path.Combine(dbDir, "ai_providers.json");
        if (File.Exists(providersPath))
        {
            var json = await File.ReadAllTextAsync(providersPath, cancellationToken);
            var providersData = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (providersData != null)
            {
                if (overwrite) aiDb.AiProviderSettings.RemoveRange(aiDb.AiProviderSettings);

                foreach (var p in providersData)
                {
                    var protectedApiKey = p.GetProperty("ProtectedApiKey").GetString() ?? "";
                    var keyProtection = p.TryGetProperty("KeyProtection", out var kp) ? kp.GetString() : "Plaintext";

                    string plainApiKey = "";
                    if (protectedApiKey.StartsWith("PLAINTEXT:"))
                    {
                        plainApiKey = protectedApiKey["PLAINTEXT:".Length..];
                    }
                    else if (keyProtection == "MachineKey")
                    {
                        plainApiKey = _apiKeyProtection.Decrypt(protectedApiKey);
                    }
                    else if (keyProtection == "BackupPassword" && !string.IsNullOrEmpty(password))
                    {
                        try
                        {
                            plainApiKey = _dataEncryption.Decrypt(protectedApiKey, password);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "解密 API Key 失败，可能密码错误");
                        }
                    }

                    var encryptedApiKey = !string.IsNullOrEmpty(plainApiKey)
                        ? _apiKeyProtection.Encrypt(plainApiKey)
                        : "";

                    var provider = new AiProviderSetting
                    {
                        ProviderId = p.GetProperty("ProviderId").GetString() ?? Guid.NewGuid().ToString(),
                        ProviderName = p.GetProperty("ProviderName").GetString() ?? "",
                        BaseUrl = p.GetProperty("BaseUrl").GetString() ?? "",
                        EncryptedApiKey = encryptedApiKey,
                        ModelsJson = p.GetProperty("ModelsJson").GetString() ?? "[]",
                        IsMain = p.TryGetProperty("IsMain", out var im) && im.GetBoolean(),
                        IsEnabled = p.TryGetProperty("IsEnabled", out _) || !p.TryGetProperty("IsEnabled", out _),
                        SortOrder = p.TryGetProperty("SortOrder", out var so) ? so.GetInt32() : 0,
                        CreatedAt = p.GetProperty("CreatedAt").GetDateTime(),
                        UpdatedAt = p.GetProperty("UpdatedAt").GetDateTime()
                    };

                    if (!aiDb.AiProviderSettings.Any(x => x.ProviderId == provider.ProviderId))
                    {
                        aiDb.AiProviderSettings.Add(provider);
                    }
                }
                await aiDb.SaveChangesAsync(cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Tasks
        var tasksPath = Path.Combine(dbDir, "tasks.json");
        if (File.Exists(tasksPath))
        {
            var json = await File.ReadAllTextAsync(tasksPath, cancellationToken);
            var tasksData = JsonSerializer.Deserialize<List<TaskEntity>>(json);
            if (tasksData != null)
            {
                if (overwrite) familyDb.Tasks.RemoveRange(familyDb.Tasks);

                foreach (var task in tasksData)
                {
                    if (!familyDb.Tasks.Any(t => t.TaskId == task.TaskId))
                    {
                        familyDb.Tasks.Add(task);
                    }
                }
                await familyDb.SaveChangesAsync(cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // AuthorizedDevices
        var devicesPath = Path.Combine(dbDir, "devices.json");
        if (File.Exists(devicesPath))
        {
            var json = await File.ReadAllTextAsync(devicesPath, cancellationToken);
            var devicesData = JsonSerializer.Deserialize<List<AuthorizedDevice>>(json);
            if (devicesData != null)
            {
                foreach (var device in devicesData)
                {
                    if (!familyDb.AuthorizedDevices.Any(d => d.DeviceId == device.DeviceId))
                    {
                        device.Status = "PendingReauth";
                        familyDb.AuthorizedDevices.Add(device);
                    }
                }
                await familyDb.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    private void RestoreConfigFiles(string tempDir)
    {
        var configDir = Path.Combine(tempDir, "config");
        if (!Directory.Exists(configDir)) return;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var webuiSrc = Path.Combine(configDir, "webui_settings.json");
        if (File.Exists(webuiSrc))
        {
            File.Copy(webuiSrc, Path.Combine(baseDir, "webui.settings.json"), overwrite: true);
        }

        var userPrefsSrc = Path.Combine(configDir, "user_preferences.json");
        if (File.Exists(userPrefsSrc))
        {
            var dataDir = Path.Combine(baseDir, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            File.Copy(userPrefsSrc, Path.Combine(dataDir, "user_preferences.json"), overwrite: true);
        }
    }

    private async Task RestoreVaultFilesAsync(string tempDir, string vaultRootPath, bool overwrite, CancellationToken cancellationToken)
    {
        var vaultsSrcDir = Path.Combine(tempDir, "vaults");
        if (!Directory.Exists(vaultsSrcDir)) return;

        using var db = _vaultDbContextFactory.CreateDbContext();
        var dbVaults = await db.Vaults.ToListAsync(cancellationToken);

        foreach (var vaultDir in Directory.GetDirectories(vaultsSrcDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vaultName = Path.GetFileName(vaultDir);

            var dbVault = dbVaults.FirstOrDefault(v =>
                string.Equals(v.Name, vaultName, StringComparison.OrdinalIgnoreCase));

            if (dbVault == null || string.IsNullOrEmpty(dbVault.Path))
            {
                _logger.LogWarning("跳过无数据库记录的知识库目录：{Name}", vaultName);
                continue;
            }

            if (!Directory.Exists(dbVault.Path))
            {
                Directory.CreateDirectory(dbVault.Path);
            }

            var notesSrc = Path.Combine(vaultDir, "notes");
            if (Directory.Exists(notesSrc))
            {
                var notesDest = Path.Combine(dbVault.Path, "notes");
                if (!Directory.Exists(notesDest)) Directory.CreateDirectory(notesDest);
                BackupPathHelper.CopyDirectory(notesSrc, notesDest, overwrite, cancellationToken);
            }

            var cardsSrc = Path.Combine(vaultDir, "cards");
            if (Directory.Exists(cardsSrc))
            {
                var cardsDest = Path.Combine(dbVault.Path, "cards");
                if (!Directory.Exists(cardsDest)) Directory.CreateDirectory(cardsDest);
                BackupPathHelper.CopyDirectory(cardsSrc, cardsDest, overwrite, cancellationToken);
            }

            var imagesSrc = Path.Combine(vaultDir, "images");
            if (Directory.Exists(imagesSrc))
            {
                var imagesDest = Path.Combine(dbVault.Path, "images");
                if (!Directory.Exists(imagesDest)) Directory.CreateDirectory(imagesDest);
                BackupPathHelper.CopyDirectory(imagesSrc, imagesDest, overwrite, cancellationToken);
            }
        }
    }
}
