using System.Diagnostics;
using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;
using Baihua.Contracts.OpenClaw;

namespace Baihua.Family.Services;

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
    private readonly IStringLocalizer<SharedResources> _loc;

    private List<ModelProfileDto> BuiltInProfiles = new();

    public OpenClawModelProfileService(ILocalAiConfigService localAiConfig, IStringLocalizer<SharedResources> loc,
        ILogger<OpenClawModelProfileService> logger)
    {
        _localAiConfig = localAiConfig;
        _loc = loc;
        _logger = logger;
        BuiltInProfiles = new()
        {
            new()
            {
                Id = "fast",
                Name = _loc["OpenClaw_Profile_Quick_Name"],
                Description = _loc["OpenClaw_Profile_Quick_Desc"],
                Model = "ollama/qwen2.5:0.5b",
                Provider = "ollama",
                SizeInfo = "671MB",
                SpeedLabel = _loc["OpenClaw_Profile_Quick_Speed"]
            },
            new()
            {
                Id = "balanced",
                Name = _loc["OpenClaw_Profile_Balanced_Name"],
                Description = _loc["OpenClaw_Profile_Balanced_Desc"],
                Model = "ollama/biancang:latest",
                Provider = "ollama",
                SizeInfo = "4.7GB Q4_K_M",
                SpeedLabel = _loc["OpenClaw_Profile_Balanced_Speed"]
            },
            new()
            {
                Id = "powerful",
                Name = _loc["OpenClaw_Profile_Powerful_Name"],
                Description = _loc["OpenClaw_Profile_Powerful_Desc"],
                Model = "ollama/qwen3.6:27b",
                Provider = "ollama",
                SizeInfo = "~17GB",
                SpeedLabel = _loc["OpenClaw_Profile_Powerful_Speed"]
            }
        };
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
