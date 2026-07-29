using TaskRunner.Core.Shared.Notifications;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Data.Entities;
using TaskRunner.Models;
using TaskRunner.Services;
using TaskRunner.Core.Shared.Security;
using TaskRunner.Contracts.Ai;
using TaskRunner.Contracts.Pairing;

namespace TaskRunner.Controllers;

public partial class AiConfigController
{
    /// <summary>
    /// 创建或更新 AI 提供商配置
    /// </summary>
    [HttpPost("providers")]
    public ActionResult SaveProvider([FromBody] SaveAiProviderRequest request)
    {
        try
        {
            // 验证 API Key 格式
            if (!string.IsNullOrEmpty(request.ApiKey) && !_aiConfigService.ValidateApiKeyFormat(request.ApiKey))
            {
                return BadRequest(new { error = "API Key 格式无效" });
            }

            // 构建模型 JSON
            var modelsJson = AiConfigService.SerializeModels(
                request.Models?.Select(m => new AiModelConfig
                {
                    Name = m.Name,
                    IsPaid = m.IsPaid,
                    IsMain = m.IsMain
                }).ToList() ?? new List<AiModelConfig>());

            var setting = new AiProviderSetting
            {
                ProviderId = request.Id.Trim(),
                ProviderName = request.Name.Trim(),
                BaseUrl = request.BaseUrl.Trim(),
                AnthropicBaseUrl = request.AnthropicBaseUrl?.Trim(),
                IsMain = request.IsMain,
                ModelsJson = modelsJson,
                SortOrder = request.SortOrder,
                IsEnabled = true,
                Tier = (int)request.Tier
            };

            _aiConfigService.SaveProvider(setting, request.ApiKey);
            
            // 清除缓存，确保新配置立即生效
            _aiSettings.ClearAiProvidersCache();
            
            // 通知 WebUI 刷新全局状态
            _ = _webUINotification.NotifyAIStatusChangedAsync();
            
            _logger.LogInformation("已保存 AI 提供商配置: {ProviderId}", request.Id);
            return Ok(new { success = true, message = "配置已保存" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 AI 配置失败");
            return StatusCode(500, new { error = $"保存失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 更新 API Key（独立接口，方便单独修改密钥）
    /// </summary>
    [HttpPost("providers/{providerId}/apikey")]
    public ActionResult UpdateApiKey(string providerId, [FromBody] UpdateApiKeyRequest request)
    {
        try
        {
            if (!_aiConfigService.ValidateApiKeyFormat(request.ApiKey))
            {
                return BadRequest(new { error = "API Key 格式无效" });
            }

            // 获取现有配置
            var existing = _aiConfigService.GetProvider(providerId);
            if (existing == null)
            {
                return NotFound(new { error = $"Provider '{providerId}' not found" });
            }

            // 保留其他配置，只更新 API Key
            var setting = new AiProviderSetting
            {
                ProviderId = existing.Id,
                ProviderName = existing.Name,
                BaseUrl = existing.AiBaseUrl,
                AnthropicBaseUrl = existing.AnthropicBaseUrl?.Trim(),
                IsMain = existing.IsMain,
                ModelsJson = AiConfigService.SerializeModels(existing.GetModelOptions()),
                SortOrder = 0,
                IsEnabled = true
            };

            _aiConfigService.SaveProvider(setting, request.ApiKey);

            // 通知 WebUI 刷新全局状态
            _ = _webUINotification.NotifyAIStatusChangedAsync();

            _logger.LogInformation("已更新 API Key: {ProviderId}", providerId);
            return Ok(new { success = true, message = "API Key 已更新" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 API Key 失败");
            return StatusCode(500, new { error = $"更新失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 删除 AI 提供商配置
    /// </summary>
    [HttpDelete("providers/{providerId}")]
    public ActionResult DeleteProvider(string providerId)
    {
        try
        {
            if (_aiConfigService.DeleteProvider(providerId))
            {
                // 清除缓存，确保配置变更立即生效
                _aiSettings.ClearAiProvidersCache();

                // 通知 WebUI 刷新全局状态
                _ = _webUINotification.NotifyAIStatusChangedAsync();
                
                _logger.LogInformation("已删除 AI 提供商配置: {ProviderId}", providerId);
                return Ok(new { success = true, message = "配置已删除" });
            }
            return NotFound(new { error = $"Provider '{providerId}' not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 AI 配置失败");
            return StatusCode(500, new { error = $"删除失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 获取主 AI 提供商的 API Key（明文，用于二维码扫描）
    /// </summary>
    [HttpGet("main-apikey")]
    public ActionResult<AiKeyQRResponse> GetMainApiKey()
    {
        try
        {
            var mainProvider = _aiConfigService.GetMainProvider();
            if (mainProvider == null)
            {
                return NotFound(new { error = "未配置主 AI 提供商" });
            }

            var apiKey = _aiConfigService.GetApiKey(mainProvider.Id);
            if (string.IsNullOrEmpty(apiKey))
            {
                return NotFound(new { error = "主 AI 提供商未设置 API Key" });
            }

            return Ok(new AiKeyQRResponse
            {
                ProviderId = mainProvider.Id,
                ProviderName = mainProvider.Name,
                ApiKey = apiKey
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取主 AI API Key 失败");
            return StatusCode(500, new { error = $"获取失败: {ex.Message}" });
        }
    }
}
