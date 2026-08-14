using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Core.Models;
using Baihua.Core.Security;

namespace Baihua.Core.Services;

/// <summary>
/// AI 配置管理服务 - 使用 SQLite + EF Core 加密存储 API Key
/// 
/// 加密方案：
/// - AES-256-GCM + 机器指纹派生密钥（默认）
/// - 兼容 Data Protection 旧数据
/// </summary>
public class AiConfigService
{
    private readonly IDbContextFactory<AIDbContext> _dbContextFactory;
    private readonly ApiKeyProtectionService _protectionService;
    private readonly ILogger<AiConfigService> _logger;

    public AiConfigService(
        IDbContextFactory<AIDbContext> dbContextFactory,
        ApiKeyProtectionService protectionService,
        ILogger<AiConfigService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _protectionService = protectionService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有启用的 AI 提供商（用于前端显示，不含密钥）
    /// </summary>
    public List<AiProviderConfig> GetProviders()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbProviders = dbContext.AiProviderSettings
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();
        return dbProviders.Select(MapToProviderConfig).ToList();
    }

    /// <summary>
    /// 获取 API Key 配置摘要（用于设置页面显示）
    /// </summary>
    public List<ApiKeySummary> GetApiKeySummaries()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbProviders = dbContext.AiProviderSettings
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();
        var result = new List<ApiKeySummary>();

        foreach (var provider in dbProviders)
        {
            var hasApiKey = !string.IsNullOrEmpty(provider.EncryptedApiKey);
            string? keyMask = null;
            EncryptionScheme? scheme = null;

            if (hasApiKey)
            {
                scheme = ApiKeyProtectionService.DetectScheme(provider.EncryptedApiKey!);
                try
                {
                    var decrypted = _protectionService.Decrypt(provider.EncryptedApiKey!);
                    keyMask = ApiKeyProtectionService.Mask(decrypted);
                }
                catch
                {
                    keyMask = "***error***";
                }
            }

            result.Add(new ApiKeySummary
            {
                ProviderId = provider.ProviderId,
                ProviderName = provider.ProviderName,
                HasApiKey = hasApiKey,
                KeyMask = keyMask,
                Scheme = scheme
            });
        }

        return result;
    }

    /// <summary>
    /// 获取指定 Provider 的有效 API Key（唯一来源：SQLite 加密存储）
    /// 注意：不再支持环境变量或配置文件中的 API Key
    /// </summary>
    public string GetApiKey(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return "";

        var id = providerId.Trim();

        // 仅从 SQLite 解密获取（API Key 的唯一存储位置）
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbProvider = dbContext.AiProviderSettings
            .FirstOrDefault(p => p.ProviderId == id);
        
        if (!string.IsNullOrEmpty(dbProvider?.EncryptedApiKey))
        {
            var decrypted = _protectionService.Decrypt(dbProvider.EncryptedApiKey);
            if (!string.IsNullOrEmpty(decrypted))
            {
                _logger.LogDebug("使用 SQLite 存储的 API Key");
                return decrypted;
            }
        }

        _logger.LogWarning("未找到 Provider {ProviderId} 的 API Key，请在 WebUI 的 AI配置 页面配置", id);
        return "";
    }

    /// <summary>
    /// 保存 Provider 配置（API Key 自动加密）
    /// </summary>
    public void SaveProvider(AiProviderSetting setting, string? plainApiKey = null)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        // 加密 API Key（如果提供）
        string? encryptedKey = null;
        if (!string.IsNullOrWhiteSpace(plainApiKey))
        {
            encryptedKey = _protectionService.Encrypt(plainApiKey.Trim());
        }

        // 获取现有配置以保留 API Key（如果未提供新 Key）
        var existing = dbContext.AiProviderSettings
            .FirstOrDefault(p => p.ProviderId == setting.ProviderId);
        
        // null = 不修改（保留旧 key）；"" = 清空 key
        if (plainApiKey == null && existing != null)
        {
            encryptedKey = existing.EncryptedApiKey;
        }

        setting.EncryptedApiKey = encryptedKey;

        // 如果设为主提供商，先取消其他提供商的主标记
        if (setting.IsMain)
        {
            var otherMainProviders = dbContext.AiProviderSettings
                .Where(p => p.ProviderId != setting.ProviderId && p.IsMain)
                .ToList();
            foreach (var p in otherMainProviders)
            {
                p.IsMain = false;
            }
        }

        if (existing != null)
        {
            // 更新现有配置
            existing.ProviderName = setting.ProviderName;
            existing.BaseUrl = setting.BaseUrl;
            existing.AnthropicBaseUrl = setting.AnthropicBaseUrl;
            existing.EncryptedApiKey = setting.EncryptedApiKey;
            existing.IsMain = setting.IsMain;
            existing.ModelsJson = setting.ModelsJson;
            existing.SortOrder = setting.SortOrder;
            existing.IsEnabled = setting.IsEnabled;
            existing.Tier = setting.Tier;
            dbContext.AiProviderSettings.Update(existing);
        }
        else
        {
            // 添加新配置
            dbContext.AiProviderSettings.Add(setting);
        }
        
        dbContext.SaveChanges();
        
        _logger.LogInformation("已保存 Provider 配置: {ProviderId}, API Key: {HasKey}", 
            setting.ProviderId, !string.IsNullOrEmpty(encryptedKey));
    }

    /// <summary>
    /// 删除 Provider 配置
    /// </summary>
    public bool DeleteProvider(string providerId)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var provider = dbContext.AiProviderSettings
            .FirstOrDefault(p => p.ProviderId == providerId);
        
        if (provider != null)
        {
            dbContext.AiProviderSettings.Remove(provider);
            dbContext.SaveChanges();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取单个 Provider 配置
    /// </summary>
    public AiProviderConfig? GetProvider(string providerId)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbProvider = dbContext.AiProviderSettings
            .FirstOrDefault(p => p.ProviderId == providerId);
        return dbProvider != null ? MapToProviderConfig(dbProvider) : null;
    }

    /// <summary>
    /// 获取主 Provider
    /// </summary>
    public AiProviderConfig? GetMainProvider()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var provider = dbContext.AiProviderSettings
            .Where(p => p.IsMain && p.IsEnabled)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefault();
        
        if (provider != null)
            return MapToProviderConfig(provider);
        
        // 如果没有主提供商，返回第一个启用的提供商
        var first = dbContext.AiProviderSettings
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefault();
        
        return first != null ? MapToProviderConfig(first) : null;
    }

    /// <summary>
    /// 验证 API Key 格式
    /// </summary>
    public bool ValidateApiKeyFormat(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        // 检查是否包含控制字符（如换行符）
        if (apiKey.Any(char.IsControl))
            return false;

        // 至少 10 个字符
        var trimmed = apiKey.Trim();
        if (trimmed.Length < 10)
            return false;

        // 支持多种格式：sk-xxx, sk-xxx-xxx, 纯字符串等
        return true;
    }

    /// <summary>
    /// 序列化模型列表为 JSON
    /// </summary>
    public static string SerializeModels(List<AiModelConfig> models)
    {
        if (models == null || models.Count == 0)
            return "[]";
        return JsonSerializer.Serialize(models);
    }

    /// <summary>
    /// 映射数据库实体到配置对象
    /// </summary>
    private AiProviderConfig MapToProviderConfig(AiProviderSetting setting)
    {
        var anthropicBaseUrl = setting.AnthropicBaseUrl;
        return new AiProviderConfig
        {
            Id = setting.ProviderId,
            Name = setting.ProviderName,
            AiBaseUrl = setting.BaseUrl,
            AnthropicBaseUrl = anthropicBaseUrl,
            IsMain = setting.IsMain,
            Models = ParseModels(setting.ModelsJson),
            Tier = (Baihua.Contracts.Ai.AiModelTier)setting.Tier
        };
    }

    /// <summary>
    /// 解析模型 JSON
    /// </summary>
    private List<AiModelConfig> ParseModels(string? modelsJson)
    {
        if (string.IsNullOrWhiteSpace(modelsJson))
            return new List<AiModelConfig>();

        try
        {
            return JsonSerializer.Deserialize<List<AiModelConfig>>(modelsJson) ?? new List<AiModelConfig>();
        }
        catch
        {
            return new List<AiModelConfig>();
        }
    }
}


