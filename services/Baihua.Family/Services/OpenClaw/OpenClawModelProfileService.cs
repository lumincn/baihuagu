using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;
using Baihua.Contracts.OpenClaw;
using Baihua.AI.Provider;

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
    private readonly AiConfigService _aiConfig;
    private readonly ILogger<OpenClawModelProfileService> _logger;
    private readonly IStringLocalizer<SharedResources> _loc;

    private List<ModelProfileDto> BuiltInProfiles = new();

    public OpenClawModelProfileService(ILocalAiConfigService localAiConfig, AiConfigService aiConfig,
        IStringLocalizer<SharedResources> loc, ILogger<OpenClawModelProfileService> logger)
    {
        _localAiConfig = localAiConfig;
        _aiConfig = aiConfig;
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // =====================
        // 主数据源：直接读取 ~/.openclaw/openclaw.json（不依赖 openclaw CLI / Node 版本）
        // =====================
        var configPath = GetOpenClawConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                await using var stream = File.OpenRead(configPath);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                // 1) 默认模型 agents.defaults.model.primary
                if (root.TryGetProperty("agents", out var agents)
                    && agents.TryGetProperty("defaults", out var defs)
                    && defs.TryGetProperty("model", out var modelNode)
                    && modelNode.TryGetProperty("primary", out var primary)
                    && primary.GetString() is { Length: > 0 } primaryVal)
                {
                    result.CurrentModel = primaryVal;
                    // 同时作为可用模型
                    seen.Add(primaryVal);
                    result.AvailableModels.Add(primaryVal);
                }

                // 2) models.providers.<id>.models[] — 已注册的 provider/model 组合
                if (root.TryGetProperty("models", out var models)
                    && models.TryGetProperty("providers", out var providers)
                    && providers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pProp in providers.EnumerateObject())
                    {
                        var providerId = pProp.Name;
                        if (string.IsNullOrWhiteSpace(providerId)) continue;
                        var pNode = pProp.Value;

                        // 优先 models 数组
                        if (pNode.TryGetProperty("models", out var pModels)
                            && pModels.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in pModels.EnumerateArray())
                            {
                                var id = m.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                                if (string.IsNullOrWhiteSpace(id))
                                    id = m.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                                if (string.IsNullOrWhiteSpace(id)) continue;

                                var key = $"{providerId}/{id}";
                                if (seen.Add(key)) result.AvailableModels.Add(key);
                            }
                        }

                        // 兜底：如果 provider 有 defaultModel / model 字段，也加进去
                        foreach (var field in new[] { "defaultModel", "model", "default" })
                        {
                            if (pNode.TryGetProperty(field, out var dm) && dm.GetString() is { Length: > 0 } dmVal)
                            {
                                var key = $"{providerId}/{dmVal}";
                                if (seen.Add(key)) result.AvailableModels.Add(key);
                            }
                        }
                    }
                }

                // 3) agents.list[].model —— 单个 agent 单独指定的模型
                if (root.TryGetProperty("agents", out var agents2)
                    && agents2.TryGetProperty("list", out var list)
                    && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in list.EnumerateArray())
                    {
                        if (a.TryGetProperty("model", out var am) && am.GetString() is { Length: > 0 } amVal)
                        {
                            if (seen.Add(amVal)) result.AvailableModels.Add(amVal);
                        }
                    }
                }

                // 4) agents.defaults.models 的字典 key
                if (root.TryGetProperty("agents", out var agents3)
                    && agents3.TryGetProperty("defaults", out var defs3)
                    && defs3.TryGetProperty("models", out var modelsDict)
                    && modelsDict.ValueKind == JsonValueKind.Object)
                {
                    foreach (var mProp in modelsDict.EnumerateObject())
                    {
                        var key = mProp.Name;
                        if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                            result.AvailableModels.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 {ConfigPath} 解析失败，回退到 CLI 与 AI 设置兜底", configPath);
            }
        }

        // =====================
        // 兜底 1：openclaw CLI 拿 default model（CLI 可用时生效；Node 版本不匹配会被之前 try/catch 吞掉，但保留逻辑）
        // =====================
        if (string.IsNullOrWhiteSpace(result.CurrentModel))
        {
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
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    {
                        var val = stdout.Trim();
                        if (!val.Contains("Config path not found"))
                        {
                            result.CurrentModel = val;
                            if (seen.Add(val)) result.AvailableModels.Add(val);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "openclaw CLI 获取默认模型失败（可忽略，已用文件方式）");
            }
        }

        // =====================
        // 兜底 2：百花 AiConfigService / LocalAiConfigService（云端 Provider + 本地 Ollama/LM Studio/llama.cpp）
        // =====================
        try
        {
            // 2.1) 云端 AI provider 中已启用且配置了模型列表的部分
            try
            {
                var cloudProviders = _aiConfig.GetProviders();
                foreach (var provider in cloudProviders)
                {
                    if (string.IsNullOrWhiteSpace(provider.Id)) continue;
                    var models = provider.GetModelOptions();
                    if (models.Count == 0)
                    {
                        var mainModel = provider.GetMainModel();
                        if (!string.IsNullOrWhiteSpace(mainModel))
                        {
                            var key = $"{provider.Id}/{mainModel}";
                            if (seen.Add(key)) result.AvailableModels.Add(key);
                        }
                        continue;
                    }
                    foreach (var m in models)
                    {
                        if (string.IsNullOrWhiteSpace(m.Name)) continue;
                        var key = $"{provider.Id}/{m.Name}";
                        if (seen.Add(key)) result.AvailableModels.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "收集云端可用模型失败（百花 AiConfigService），继续收集本地模型");
            }

            // 2.2) 本地 AI 配置：Ollama / LM Studio / llama.cpp
            var localConfig = await _localAiConfig.GetLocalAiConfigAsync();
            if (localConfig.Ollama?.Enabled == true)
            {
                foreach (var m in localConfig.Ollama.Models)
                {
                    var key = $"ollama/{m.Id}";
                    if (seen.Add(key)) result.AvailableModels.Add(key);
                }
            }
            if (localConfig.LmStudio?.Enabled == true)
            {
                foreach (var m in localConfig.LmStudio.Models)
                {
                    var key = $"lmstudio/{m.Id}";
                    if (seen.Add(key)) result.AvailableModels.Add(key);
                }
            }
            if (localConfig.LlamaCpp?.Enabled == true && !string.IsNullOrWhiteSpace(localConfig.LlamaCpp.ModelPath))
            {
                var modelName = Path.GetFileNameWithoutExtension(localConfig.LlamaCpp.ModelPath);
                var modelId = modelName.Replace(".", "-").ToLowerInvariant();
                var key = $"llamacpp/{modelId}";
                if (seen.Add(key)) result.AvailableModels.Add(key);
            }
            if (localConfig.OpenVino?.Enabled == true && !string.IsNullOrWhiteSpace(localConfig.OpenVino.ModelPath))
            {
                // 与 Scan 侧保持一致：目录本身 + 子目录多模型识别，id 与 server 的 model_id() 对齐
                foreach (var m in await _localAiConfig.ScanLocalModelsAsync("openvino"))
                {
                    var key = $"openvino/{m.Id}";
                    if (seen.Add(key)) result.AvailableModels.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集百花侧可用模型失败");
        }

        return result;
    }

    /// <summary>
    /// 定位 openclaw.json 路径。跨平台约定：
    ///   Windows: %USERPROFILE%\.openclaw\openclaw.json
    ///   Linux/macOS: ~/.openclaw/openclaw.json
    /// </summary>
    private static string GetOpenClawConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) home = "~";
        return Path.Combine(home, ".openclaw", "openclaw.json");
    }

    public async Task<bool> SetDefaultModelAsync(string model)
    {
        // 方案 1：优先 openclaw CLI（Node 版本满足时正规生效）
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
            if (process != null)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                    return true;
                _logger.LogWarning("openclaw CLI 设置默认模型失败: {Stderr}，回退到直接写 openclaw.json", stderr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "openclaw CLI 不可用（通常是 Node 版本过低），尝试直接写配置文件");
        }

        // 方案 2：直接修改 ~/.openclaw/openclaw.json 中的 agents.defaults.model.primary
        //         优点：不依赖 Node / CLI；缺点：OpenClaw gateway 若正在运行且有内存缓存，可能需要用户手动 reload
        var configPath = GetOpenClawConfigPath();
        if (!File.Exists(configPath))
        {
            _logger.LogError("设置默认模型失败：{ConfigPath} 不存在，且 CLI 亦不可用", configPath);
            return false;
        }
        try
        {
            // 备份
            var backupPath = configPath + ".bak-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(configPath, backupPath, overwrite: true);

            // 读 JSON 并在内存修改
            JsonNode root;
            await using (var readStream = File.OpenRead(configPath))
            {
                root = (await JsonNode.ParseAsync(readStream)) ?? new JsonObject();
            }

            // 构造/确保路径存在: agents -> defaults -> model -> primary
            JsonObject agentsObj;
            if (root["agents"] is JsonObject a) agentsObj = a;
            else { agentsObj = new JsonObject(); root["agents"] = agentsObj; }

            JsonObject defaultsObj;
            if (agentsObj["defaults"] is JsonObject d) defaultsObj = d;
            else { defaultsObj = new JsonObject(); agentsObj["defaults"] = defaultsObj; }

            JsonObject modelObj;
            if (defaultsObj["model"] is JsonObject m) modelObj = m;
            else { modelObj = new JsonObject(); defaultsObj["model"] = modelObj; }

            modelObj["primary"] = model;

            // 同时把模型加进 agents.defaults.models 字典（避免之后 UI 不显示 alias）
            if (defaultsObj["models"] is not JsonObject modelsDict)
            {
                modelsDict = new JsonObject();
                defaultsObj["models"] = modelsDict;
            }
            if (modelsDict[model] == null)
            {
                // alias = 最后一段（如 deepseek/deepseek-v4-flash → Deepseek V4 Flash）
                var lastSlash = model.LastIndexOf('/');
                var rawName = lastSlash >= 0 ? model.Substring(lastSlash + 1) : model;
                var alias = string.Join(' ', rawName.Split(new[] {'-', '_', '.'}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant()));
                modelsDict[model] = new JsonObject { ["alias"] = alias };
            }

            // 写回
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            await using var writeStream = new FileStream(configPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(writeStream, root, writeOptions);
            _logger.LogInformation("已直接更新 {ConfigPath}：agents.defaults.model.primary = {Model}", configPath, model);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "直接写 {ConfigPath} 设置默认模型失败", configPath);
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
