using TaskRunner.Core.Shared.Notifications;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Data.Entities;
using TaskRunner.Models;
using TaskRunner.Services;
using TaskRunner.Core.Shared.Security;
using TaskRunner.Contracts.Ai;

namespace TaskRunner.Controllers;

/// <summary>
/// AI 配置管理 API - SQLite 加密存储
/// </summary>
[ApiController]
[Route("api/ai/config")]
public partial class AiConfigController : ControllerBase
{
    private readonly AiConfigService _aiConfigService;
    private readonly AiSettingsService _aiSettings;
    private readonly WebUINotificationService _webUINotification;
    private readonly TaskRunner.Services.CapabilityService _capabilityService;
    private readonly ILogger<AiConfigController> _logger;

    public AiConfigController(
        AiConfigService aiConfigService,
        AiSettingsService aiSettings,
        WebUINotificationService webUINotification,
        TaskRunner.Services.CapabilityService capabilityService,
        ILogger<AiConfigController> logger)
    {
        _aiConfigService = aiConfigService;
        _aiSettings = aiSettings;
        _webUINotification = webUINotification;
        _capabilityService = capabilityService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有 AI 提供商配置（不含敏感信息）
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<List<AiProviderViewModel>> GetProviders()
    {
        var providers = _aiConfigService.GetProviders();
        var summaries = _aiConfigService.GetApiKeySummaries();
        
        var result = providers.Select(p =>
        {
            var summary = summaries.FirstOrDefault(s => s.ProviderId == p.Id);
            return new AiProviderViewModel
            {
                Id = p.Id,
                Name = p.Name,
                BaseUrl = p.AiBaseUrl,
                AnthropicBaseUrl = p.AnthropicBaseUrl,
                IsMain = p.IsMain,
                Models = p.GetModelOptions().Select(m => new AiModelViewModel
                {
                    Name = m.Name,
                    IsPaid = m.IsPaid,
                    IsMain = m.IsMain
                }).ToList(),
                HasApiKey = summary?.HasApiKey ?? false,
                KeyMask = summary?.KeyMask,
                Tier = p.Tier
            };
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// 获取 API Key 配置摘要（用于设置页面）
    /// </summary>
    [HttpGet("apikeys")]
    public ActionResult<List<ApiKeySummary>> GetApiKeySummaries()
    {
        return Ok(_aiConfigService.GetApiKeySummaries());
    }

    /// <summary>
    /// 获取单个提供商配置
    /// </summary>
    [HttpGet("providers/{providerId}")]
    public ActionResult<AiProviderViewModel> GetProvider(string providerId)
    {
        var provider = _aiConfigService.GetProvider(providerId);
        if (provider == null)
            return NotFound(new { error = $"Provider '{providerId}' not found" });

        var summary = _aiConfigService.GetApiKeySummaries().FirstOrDefault(s => s.ProviderId == providerId);

        return Ok(new AiProviderViewModel
        {
            Id = provider.Id,
            Name = provider.Name,
            BaseUrl = provider.AiBaseUrl,
            AnthropicBaseUrl = provider.AnthropicBaseUrl,
            IsMain = provider.IsMain,
            Models = provider.GetModelOptions().Select(m => new AiModelViewModel
            {
                Name = m.Name,
                IsPaid = m.IsPaid,
                IsMain = m.IsMain
            }).ToList(),
            HasApiKey = summary?.HasApiKey ?? false,
            KeyMask = summary?.KeyMask,
            Tier = provider.Tier
        });
    }

}
