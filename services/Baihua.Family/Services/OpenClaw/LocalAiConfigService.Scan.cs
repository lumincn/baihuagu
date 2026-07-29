using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;

namespace TaskRunner.Services;

public partial class LocalAiConfigService
{
    #region Scan Local Models

    public async Task<List<OpenClawLocalModelDto>> ScanLocalModelsAsync(string provider)
    {
        var config = await GetLocalAiConfigAsync();

        if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            return await ScanOllamaModelsAsync(config.Ollama);
        }
        if (provider.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            return await ScanLmStudioModelsAsync(config.LmStudio);
        }
        if (provider.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
        {
            return await ScanLlamaCppModelsAsync(config.LlamaCpp);
        }

        return new List<OpenClawLocalModelDto>();
    }

    private async Task<List<OpenClawLocalModelDto>> ScanOllamaModelsAsync(OpenClawLocalProviderConfigDto? config)
    {
        if (config == null || !config.Enabled || string.IsNullOrWhiteSpace(config.BaseUrl))
            return new List<OpenClawLocalModelDto>();

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{config.BaseUrl.TrimEnd('/')}/api/tags");
            if (!response.IsSuccessStatusCode) return new List<OpenClawLocalModelDto>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var result = new List<OpenClawLocalModelDto>();
            if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in models.EnumerateArray())
                {
                    var id = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id)) continue;
                    result.Add(new OpenClawLocalModelDto
                    {
                        Id = id,
                        Name = id,
                        ApiType = config.ApiType,
                    });
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "扫描 Ollama 模型失败");
            return new List<OpenClawLocalModelDto>();
        }
    }

    private async Task<List<OpenClawLocalModelDto>> ScanLmStudioModelsAsync(OpenClawLocalProviderConfigDto? config)
    {
        if (config == null || !config.Enabled || string.IsNullOrWhiteSpace(config.BaseUrl))
            return new List<OpenClawLocalModelDto>();

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{config.BaseUrl.TrimEnd('/')}/v1/models");
            if (!response.IsSuccessStatusCode) return new List<OpenClawLocalModelDto>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var result = new List<OpenClawLocalModelDto>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id)) continue;
                    result.Add(new OpenClawLocalModelDto
                    {
                        Id = id,
                        Name = id,
                        ApiType = config.ApiType,
                    });
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "扫描 LM Studio 模型失败");
            return new List<OpenClawLocalModelDto>();
        }
    }

    private async Task<List<OpenClawLocalModelDto>> ScanLlamaCppModelsAsync(OpenClawLlamaCppConfigDto? config)
    {
        if (config == null || !config.Enabled)
            return new List<OpenClawLocalModelDto>();

        // 先检测服务是否运行
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{config.BaseUrl.TrimEnd('/')}/v1/models");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var result = new List<OpenClawLocalModelDto>();
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(id)) continue;
                        result.Add(new OpenClawLocalModelDto
                        {
                            Id = id,
                            Name = id,
                            ApiType = config.ApiType,
                        });
                    }
                }
                return result;
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "探测 llama.cpp 运行模型失败"); }

        // 服务未运行，返回配置中的模型（前端会提示用户先启动）
        if (File.Exists(config.ModelPath))
        {
            var modelName = Path.GetFileNameWithoutExtension(config.ModelPath);
            var modelId = modelName.Replace(".", "-").ToLowerInvariant();
            return new List<OpenClawLocalModelDto>
            {
                new OpenClawLocalModelDto
                {
                    Id = modelId,
                    Name = $"{modelName} (需启动服务)",
                    ApiType = config.ApiType,
                    ContextWindow = config.ContextSize,
                }
            };
        }

        return new List<OpenClawLocalModelDto>();
    }

    #endregion

}
