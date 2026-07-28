using TaskRunner.Core.Shared.Notifications;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Data.Entities;
using TaskRunner.Models;
using TaskRunner.Services;
using TaskRunner.Core.Shared.Security;
using TaskRunner.Contracts.Ai;

namespace TaskRunner.Controllers;

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
                Name = "智谱 AI (GLM)",
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
                Name = "火山引擎方舟 (Volcano Ark)",
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
                Name = "阿里云百炼 (Aliyun Bailian)",
                BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                Models = new()
                {
                    new() { Name = "qwen3-plus", IsPaid = true, IsMain = true },
                    new() { Name = "qwen3-max", IsPaid = true, IsMain = false },
                    new() { Name = "qwen3-turbo", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-v3", IsPaid = true, IsMain = false },
                    new() { Name = "deepseek-r1", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "deepseek",
                Name = "DeepSeek (官方)",
                BaseUrl = "https://api.deepseek.com/v1",
                Models = new()
                {
                    new() { Name = "deepseek-chat", IsPaid = true, IsMain = true },
                    new() { Name = "deepseek-reasoner", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "anthropic",
                Name = "Anthropic (Claude)",
                BaseUrl = "https://api.anthropic.com/v1",
                Models = new()
                {
                    new() { Name = "claude-sonnet-4-20250514", IsPaid = true, IsMain = true },
                    new() { Name = "claude-haiku-3-5-20241022", IsPaid = true, IsMain = false },
                    new() { Name = "claude-opus-4-20250514", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "openai",
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Models = new()
                {
                    new() { Name = "gpt-4.1", IsPaid = true, IsMain = true },
                    new() { Name = "gpt-4.1-mini", IsPaid = true, IsMain = false },
                    new() { Name = "o4-mini", IsPaid = true, IsMain = false },
                    new() { Name = "gpt-4o", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "google",
                Name = "Google Gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                Models = new()
                {
                    new() { Name = "gemini-2.5-pro", IsPaid = true, IsMain = true },
                    new() { Name = "gemini-2.5-flash", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "kimi",
                Name = "Kimi (月之暗面 Moonshot)",
                BaseUrl = "https://api.moonshot.cn/v1",
                Models = new()
                {
                    new() { Name = "moonshot-v1-8k", IsPaid = true, IsMain = false },
                    new() { Name = "moonshot-v1-32k", IsPaid = true, IsMain = true },
                    new() { Name = "moonshot-v1-128k", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "azure",
                Name = "Azure OpenAI",
                BaseUrl = "https://{your-resource}.openai.azure.com/openai/deployments/{deployment-id}",
                Models = new()
                {
                    new() { Name = "gpt-4o", IsPaid = true, IsMain = true },
                    new() { Name = "gpt-4.1", IsPaid = true, IsMain = false },
                    new() { Name = "gpt-35-turbo", IsPaid = true, IsMain = false }
                }
            },
            new()
            {
                Id = "ollama",
                Name = "本地 Ollama",
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
                Name = "本地 LM Studio",
                BaseUrl = "http://localhost:1234/v1",
                Tier = AiModelTier.Tier2_Local,
                Models = new()
                {
                    new() { Name = "loaded-model", IsPaid = false, IsMain = true }
                }
            }
        };

        // 根据机器能力过滤本地 Provider 预设
        if (!_capabilityService.CanUse(TaskRunner.Services.LocalComputeFeature.AiConfigLocalProviderPresets))
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
    public TaskRunner.Contracts.Ai.AiModelTier Tier { get; set; }
}

public class AiModelViewModel
{
    public string Name { get; set; } = "";
    public bool IsPaid { get; set; }
    public bool IsMain { get; set; }
}


