using Baihua.Core.Notifications;
using Microsoft.AspNetCore.Mvc;
using Baihua.Data.Entities;
using Baihua.Family.Models;
using Baihua.Family.Services;
using Baihua.Core.Security;
using Baihua.Contracts.Ai;

namespace Baihua.Family.Controllers;

public partial class AiConfigController
{
    /// <summary>
    /// 获取预设的知名 AI 提供商列表
    /// </summary>
    [HttpGet("presets")]
    public ActionResult<List<AiProviderPreset>> GetPresets()
    {
        var presets = new List<AiProviderPreset>
        {
            new()
            {
                Id = "zhipu",
                Name = _loc["AiConfig_PresetZhipu"],
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Models = new()
                {
                    new() { Name = "glm-4-plus", IsPaid = true, IsMain = true },
                    new() { Name = "glm-4-flash", IsPaid = false, IsMain = false },
                    new() { Name = "glm-4-air", IsPaid = true, IsMain = false },
                    new() { Name = "glm-4-long", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "volcano",
                Name = _loc["AiConfig_PresetVolcano"],
                BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
                Models = new()
                {
                    new() { Name = "doubao-seed-1-6-251015", IsPaid = true, IsMain = true },
                    new() { Name = "doubao-1-5-pro-256k-250815", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-r1-250528", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-v3-250528", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "aliyun",
                Name = _loc["AiConfig_PresetAliyun"],
                BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                Models = new()
                {
                    new() { Name = "qwen3.7-plus", IsPaid = true, IsMain = true },
                    new() { Name = "qwen3.7-max", IsPaid = true, IsMain = false },
                    new() { Name = "qwen3.7-flash", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-v3", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-r1", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "deepseek",
                Name = _loc["AiConfig_PresetDeepSeek"],
                BaseUrl = "https://api.deepseek.com",
                AnthropicBaseUrl = "https://api.deepseek.com/anthropic",
                Models = new()
                {
                    new() { Name = "deepseek-v4-pro", IsPaid = true, IsMain = true },
                    new() { Name = "deepseek-v4-flash", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "kimi",
                Name = _loc["AiConfig_PresetKimi"],
                BaseUrl = "https://api.moonshot.cn/v1",
                Models = new()
                {
                    new() { Name = "kimi-k3", IsPaid = true, IsMain = true },
                    new() { Name = "kimi-k2.7-code", IsPaid = true, IsMain = false },
                    new() { Name = "kimi-k2.6", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "ollama",
                Name = _loc["AiConfig_PresetLocalOllama"],
                BaseUrl = "http://localhost:11434/v1",
                Tier = AiModelTier.Tier2_Local,
                Models = new()
                {
                    new() { Name = "qwen3:14b", IsPaid = false, IsMain = true },
                    new() { Name = "deepseek-r1:14b", IsPaid = false, IsMain = false },
                    new() { Name = "llama3.2:latest", IsPaid = false, IsMain = false }
                }
            },
            new()
            {
                Id = "lmstudio",
                Name = _loc["AiConfig_PresetLocalLmStudio"],
                BaseUrl = "http://localhost:1234/v1",
                Tier = AiModelTier.Tier2_Local,
                Models = new()
                {
                    new() { Name = "loaded-model", IsPaid = false, IsMain = true }
                }
            }
        };

        // 根据机器能力过滤本地 Provider 预设
        if (!_capabilityService.CanUse(Baihua.Family.Services.LocalComputeFeature.AiConfigLocalProviderPresets))
        {
            presets = presets.Where(p =>
                !p.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase) &&
                !p.Id.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Ok(presets);
    }
}

// View Models
public class AiProviderViewModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? AnthropicBaseUrl { get; set; }
    public bool IsMain { get; set; }
    public List<AiModelViewModel> Models { get; set; } = new();
    public bool HasApiKey { get; set; }
    public string? KeyMask { get; set; }
    public Baihua.Contracts.Ai.AiModelTier Tier { get; set; }
}

public class AiModelViewModel
{
    public string Name { get; set; } = "";
    public bool IsPaid { get; set; }
    public bool IsMain { get; set; }
}


