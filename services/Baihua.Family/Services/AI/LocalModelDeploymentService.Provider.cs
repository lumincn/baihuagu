using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Data.Entities;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LocalModelDeploymentService
{
        #region Provider Auto-Configuration

        private void ConfigureOllamaProvider(ModelEntry model)
        {
            const string providerId = "ollama";
            const string defaultProviderName = "本地 Ollama";
            const string baseUrl = "http://localhost:11434/v1";

            var existing = _aiConfigService.GetProvider(providerId);
            List<AiModelConfig> models;
            bool isMain;
            string providerName;

            if (existing != null)
            {
                providerName = existing.Name;
                models = existing.GetModelOptions().Select(m => new AiModelConfig
                {
                    Name = m.Name,
                    IsPaid = m.IsPaid,
                    IsMain = false
                }).ToList();
                isMain = existing.IsMain;

                if (!models.Any(m => m.Name.Equals(model.OllamaModelName, StringComparison.OrdinalIgnoreCase)))
                {
                    models.Add(new AiModelConfig
                    {
                        Name = model.OllamaModelName,
                        IsPaid = false,
                        IsMain = models.Count == 0
                    });
                }
            }
            else
            {
                providerName = defaultProviderName;
                models = new List<AiModelConfig>
                {
                    new()
                    {
                        Name = model.OllamaModelName,
                        IsPaid = false,
                        IsMain = true
                    }
                };
                isMain = false;
            }

            var setting = new AiProviderSetting
            {
                ProviderId = providerId,
                ProviderName = providerName,
                BaseUrl = baseUrl,
                IsMain = isMain,
                ModelsJson = AiConfigService.SerializeModels(models),
                SortOrder = 0,
                IsEnabled = true
            };

            _aiConfigService.SaveProvider(setting, "");
            _logger.LogInformation("已自动配置 Ollama Provider，新增模型: {Model}", model.OllamaModelName);
        }

        private void ConfigureLmStudioProvider(ModelEntry model)
        {
            const string providerId = "lmstudio";
            const string defaultProviderName = "本地 LM Studio";
            const string baseUrl = "http://localhost:1234/v1";

            var existing = _aiConfigService.GetProvider(providerId);
            List<AiModelConfig> models;
            bool isMain;
            string providerName;

            if (existing != null)
            {
                providerName = existing.Name;
                models = existing.GetModelOptions().Select(m => new AiModelConfig
                {
                    Name = m.Name,
                    IsPaid = m.IsPaid,
                    IsMain = false
                }).ToList();
                isMain = existing.IsMain;

                if (!models.Any(m => m.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    models.Add(new AiModelConfig
                    {
                        Name = model.Name,
                        IsPaid = false,
                        IsMain = models.Count == 0
                    });
                }
            }
            else
            {
                providerName = defaultProviderName;
                models = new List<AiModelConfig>
                {
                    new()
                    {
                        Name = model.Name,
                        IsPaid = false,
                        IsMain = true
                    }
                };
                isMain = false;
            }

            var setting = new AiProviderSetting
            {
                ProviderId = providerId,
                ProviderName = providerName,
                BaseUrl = baseUrl,
                IsMain = isMain,
                ModelsJson = AiConfigService.SerializeModels(models),
                SortOrder = 0,
                IsEnabled = true
            };

            _aiConfigService.SaveProvider(setting, "");
            _logger.LogInformation("已自动配置 LM Studio Provider，新增模型: {Model}", model.Name);
        }

        private void RemoveModelFromProviderConfig(string toolId, string modelName)
        {
            try
            {
                var providerId = toolId.ToLowerInvariant();
                var provider = _aiConfigService.GetProvider(providerId);
                if (provider == null) return;

                var modelOptions = provider.GetModelOptions();
                var originalCount = modelOptions.Count;
                var filtered = modelOptions
                    .Where(m => !m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (filtered.Count == originalCount) return;

                var setting = new AiProviderSetting
                {
                    ProviderId = provider.Id,
                    ProviderName = provider.Name,
                    BaseUrl = provider.AiBaseUrl,
                    IsMain = provider.IsMain,
                    ModelsJson = System.Text.Json.JsonSerializer.Serialize(filtered),
                    IsEnabled = true,
                };

                _aiConfigService.SaveProvider(setting, plainApiKey: null);
                _aiSettings.ClearAiProvidersCache();
                _logger.LogInformation("已从 AI Provider {ProviderId} 的配置中移除模型 {ModelName}", providerId, modelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 AI Provider 配置中移除模型 {ModelName} 失败", modelName);
            }
        }

        #endregion

}
