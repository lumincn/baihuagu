using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Contracts.Ai;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Core.Shared.Security;

namespace TaskRunner.Controllers;

/// <summary>
/// Embedding 模型配置 API（一级·固本模型，用于 RAG 向量检索）
/// </summary>
[ApiController]
[Route("api/embedding/config")]
public class EmbeddingConfigController : ControllerBase
{
    private readonly IDbContextFactory<AIDbContext> _dbContextFactory;
    private readonly ApiKeyProtectionService _protectionService;
    private readonly ILogger<EmbeddingConfigController> _logger;

    public EmbeddingConfigController(
        IDbContextFactory<AIDbContext> dbContextFactory,
        ApiKeyProtectionService protectionService,
        ILogger<EmbeddingConfigController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _protectionService = protectionService;
        _logger = logger;
    }

    /// <summary>
    /// 获取 Embedding 配置（单条，系统只允许一条）
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<EmbeddingConfigDto>> GetConfig()
    {
        await using var db = _dbContextFactory.CreateDbContext();
        var config = await db.EmbeddingConfigs.OrderBy(e => e.Id).FirstOrDefaultAsync();
        if (config == null)
        {
            // 返回默认配置
            return Ok(new EmbeddingConfigDto
            {
                ProviderId = "ollama",
                Model = "nomic-embed-text",
                BaseUrl = "http://localhost:11434/v1",
                IsEnabled = true,
                Dimensions = null
            });
        }

        string? keyMask = null;
        if (!string.IsNullOrEmpty(config.EncryptedApiKey))
        {
            try
            {
                var decrypted = _protectionService.Decrypt(config.EncryptedApiKey);
                keyMask = MaskKey(decrypted);
            }
            catch
            {
                keyMask = "***error***";
            }
        }

        return Ok(new EmbeddingConfigDto
        {
            Id = config.Id,
            ProviderId = config.ProviderId,
            Model = config.Model,
            BaseUrl = config.BaseUrl,
            IsEnabled = config.IsEnabled,
            Dimensions = config.Dimensions,
            KeyMask = keyMask
        });
    }

    /// <summary>
    /// 保存或更新 Embedding 配置
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> SaveConfig([FromBody] SaveEmbeddingConfigRequest request)
    {
        try
        {
            await using var db = _dbContextFactory.CreateDbContext();
            var existing = await db.EmbeddingConfigs.OrderBy(e => e.Id).FirstOrDefaultAsync();

            string? encryptedKey = null;
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                encryptedKey = _protectionService.Encrypt(request.ApiKey.Trim());
            }

            if (existing != null)
            {
                // 保留旧密钥（如果未提供新密钥）
                if (string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    encryptedKey = existing.EncryptedApiKey;
                }

                existing.ProviderId = request.ProviderId.Trim();
                existing.Model = request.Model.Trim();
                existing.BaseUrl = request.BaseUrl.Trim();
                existing.EncryptedApiKey = encryptedKey;
                existing.IsEnabled = request.IsEnabled;
                existing.Dimensions = request.Dimensions;
                existing.UpdatedAt = DateTime.UtcNow;
                db.EmbeddingConfigs.Update(existing);
            }
            else
            {
                var config = new EmbeddingConfig
                {
                    ProviderId = request.ProviderId.Trim(),
                    Model = request.Model.Trim(),
                    BaseUrl = request.BaseUrl.Trim(),
                    EncryptedApiKey = encryptedKey,
                    IsEnabled = request.IsEnabled,
                    Dimensions = request.Dimensions,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.EmbeddingConfigs.Add(config);
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Embedding 配置已保存: Provider={Provider}, Model={Model}", request.ProviderId, request.Model);
            return Ok(new { success = true, message = "Embedding 配置已保存" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 Embedding 配置失败");
            return StatusCode(500, new { error = $"保存失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 获取解密后的 Embedding API Key（仅限后端内部调用）
    /// </summary>
    [HttpGet("apikey")]
    public async Task<ActionResult<string>> GetApiKey()
    {
        await using var db = _dbContextFactory.CreateDbContext();
        var config = await db.EmbeddingConfigs.OrderBy(e => e.Id).FirstOrDefaultAsync();
        if (config == null || string.IsNullOrEmpty(config.EncryptedApiKey))
            return Ok("");

        try
        {
            var decrypted = _protectionService.Decrypt(config.EncryptedApiKey);
            return Ok(decrypted ?? "");
        }
        catch
        {
            return Ok("");
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 8)
            return "***";
        return key[..4] + "..." + key[^4..];
    }
}
