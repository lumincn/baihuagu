using System.Diagnostics;
using TaskRunner.Contracts.OpenClaw;

namespace TaskRunner.Services;

public interface IOpenClawModelProfileService
{
    Task<OpenClawDefaultModelDto> GetDefaultModelAsync();
    Task<bool> SetDefaultModelAsync(string model);
    Task<ModelProfileListDto> GetModelProfilesAsync();
    Task<bool> SetModelProfileAsync(string profileId);
}

public class OpenClawModelProfileService : IOpenClawModelProfileService
{
    private readonly ILocalAiConfigService _localAiConfig;
    private readonly ILogger<OpenClawModelProfileService> _logger;

    private static readonly List<ModelProfileDto> BuiltInProfiles = new()
    {
        new()
        {
            Id = "fast",
            Name = "快速",
            Description = "671MB 超轻量模型，响应极快，适合简单问答和日常查询",
            Model = "ollama/qwen2.5:0.5b",
            Provider = "ollama",
            SizeInfo = "671MB",
            SpeedLabel = "⚡ 极快"
        },
        new()
        {
            Id = "balanced",
            Name = "平衡",
            Description = "4.7GB 量化模型，在知识库内容上表现均衡，推荐日常使用",
            Model = "ollama/biancang:latest",
            Provider = "ollama",
            SizeInfo = "4.7GB Q4_K_M",
            SpeedLabel = "🚀 快"
        },
        new()
        {
            Id = "powerful",
            Name = "强力",
            Description = "27B 大参数模型，推理能力强，适合复杂辨证分析和深度问答",
            Model = "ollama/qwen3.6:27b",
            Provider = "ollama",
            SizeInfo = "~17GB",
            SpeedLabel = "🐢 较慢"
        }
    };

    public OpenClawModelProfileService(ILocalAiConfigService localAiConfig, ILogger<OpenClawModelProfileService> logger)
    {
        _localAiConfig = localAiConfig;
        _logger = logger;
    }

    public async Task<OpenClawDefaultModelDto> GetDefaultModelAsync()
    {
        var result = new OpenClawDefaultModelDto();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "openclaw",
                Arguments = "config get agents.defaults.model.primary",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    var val = stdout.Trim();
                    if (!val.Contains("Config path not found"))
                        result.CurrentModel = val;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取 OpenClaw 默认模型失败");
        }

        // 收集可用模型
        try
        {
            var config = await _localAiConfig.GetLocalAiConfigAsync();
            if (config.Ollama?.Enabled == true)
            {
                foreach (var m in config.Ollama.Models)
                    result.AvailableModels.Add($"ollama/{m.Id}");
            }
            if (config.LmStudio?.Enabled == true)
            {
                foreach (var m in config.LmStudio.Models)
                    result.AvailableModels.Add($"lmstudio/{m.Id}");
            }
            if (config.LlamaCpp?.Enabled == true)
            {
                var modelName = Path.GetFileNameWithoutExtension(config.LlamaCpp.ModelPath);
                var modelId = modelName.Replace(".", "-").ToLowerInvariant();
                result.AvailableModels.Add($"llamacpp/{modelId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集可用模型失败");
        }

        return result;
    }

    public async Task<bool> SetDefaultModelAsync(string model)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "openclaw",
                Arguments = $"config set agents.defaults.model.primary \"{model.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("设置默认模型失败: {Stderr}", stderr);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置 OpenClaw 默认模型失败");
            return false;
        }
    }

    public async Task<ModelProfileListDto> GetModelProfilesAsync()
    {
        var result = new ModelProfileListDto
        {
            Profiles = BuiltInProfiles
        };

        try
        {
            var defaultModel = await GetDefaultModelAsync();
            if (!string.IsNullOrWhiteSpace(defaultModel.CurrentModel))
            {
                var profile = BuiltInProfiles.FirstOrDefault(p =>
                    p.Model.Equals(defaultModel.CurrentModel, StringComparison.OrdinalIgnoreCase));
                result.CurrentProfile = profile?.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取当前 profile 失败");
        }

        return result;
    }

    public async Task<bool> SetModelProfileAsync(string profileId)
    {
        var profile = BuiltInProfiles.FirstOrDefault(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            _logger.LogWarning("未知模型配置: {ProfileId}", profileId);
            return false;
        }

        return await SetDefaultModelAsync(profile.Model);
    }
}
