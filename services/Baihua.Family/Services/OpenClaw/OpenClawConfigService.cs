using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;

namespace TaskRunner.Services;

/// <summary>
/// OpenClaw 配置服务：读写 openclaw.json 和 llamacpp-config.json
/// </summary>
public class OpenClawConfigService(ILogger<OpenClawConfigService> logger)
{
    private static string GetOpenClawConfigPath()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(home, ".openclaw", "openclaw.json");
    }

    private static string GetLlamaCppConfigPath()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(home, ".openclaw", "llamacpp-config.json");
    }

    public async Task<OpenClawLocalAiConfigDto> GetLocalAiConfigAsync()
    {
        var path = GetOpenClawConfigPath();
        var result = new OpenClawLocalAiConfigDto();

        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("models", out var models) &&
                models.TryGetProperty("providers", out var providers))
            {
                if (providers.TryGetProperty("ollama", out var ollama))
                    result.Ollama = ParseProviderConfig(ollama);
                if (providers.TryGetProperty("lmstudio", out var lmstudio))
                    result.LmStudio = ParseProviderConfig(lmstudio);
                if (providers.TryGetProperty("llamacpp", out var llamacpp))
                    result.LlamaCpp = ParseLlamaCppConfig(llamacpp);
            }
        }

        var llamaCppPath = GetLlamaCppConfigPath();
        if (File.Exists(llamaCppPath))
        {
            var json = await File.ReadAllTextAsync(llamaCppPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cfg = result.LlamaCpp ?? new OpenClawLlamaCppConfigDto();
            if (root.TryGetProperty("enabled", out var enabled))
                cfg.Enabled = enabled.GetBoolean();
            if (root.TryGetProperty("binaryPath", out var binaryPath))
                cfg.BinaryPath = binaryPath.GetString() ?? "";
            if (root.TryGetProperty("modelPath", out var modelPath))
                cfg.ModelPath = modelPath.GetString() ?? "";
            if (root.TryGetProperty("baseUrl", out var baseUrl))
                cfg.BaseUrl = baseUrl.GetString() ?? "http://localhost:8080";
            if (root.TryGetProperty("port", out var port) && port.ValueKind == JsonValueKind.Number)
                cfg.Port = port.GetInt32();
            if (root.TryGetProperty("nGpuLayers", out var ngl) && ngl.ValueKind == JsonValueKind.Number)
                cfg.NGpuLayers = ngl.GetInt32();
            if (root.TryGetProperty("contextSize", out var ctx) && ctx.ValueKind == JsonValueKind.Number)
                cfg.ContextSize = ctx.GetInt32();
            if (root.TryGetProperty("extraArgs", out var extraArgs))
                cfg.ExtraArgs = extraArgs.GetString() ?? "";
            if (root.TryGetProperty("threads", out var threads) && threads.ValueKind == JsonValueKind.Number)
                cfg.Threads = threads.GetInt32();
            if (root.TryGetProperty("batchSize", out var batchSize) && batchSize.ValueKind == JsonValueKind.Number)
                cfg.BatchSize = batchSize.GetInt32();
            if (root.TryGetProperty("cacheTypeK", out var cacheTypeK))
                cfg.CacheTypeK = cacheTypeK.GetString() ?? "";
            if (root.TryGetProperty("cacheTypeV", out var cacheTypeV))
                cfg.CacheTypeV = cacheTypeV.GetString() ?? "";
            if (root.TryGetProperty("useContBatching", out var useContBatching) && useContBatching.ValueKind == JsonValueKind.True)
                cfg.UseContBatching = useContBatching.GetBoolean();
            result.LlamaCpp = cfg;
        }

        return result;
    }

    public async Task<bool> SaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
    {
        if (request.Ollama != null)
        {
            if (request.Ollama.Enabled && !string.IsNullOrWhiteSpace(request.Ollama.BaseUrl))
            {
                var providerJson = BuildProviderJson(request.Ollama).ToJsonString(JsonHelper.Compact);
                if (!await RunOpenClawConfigSetAsync("models.providers.ollama", providerJson))
                    return false;
            }
            else
            {
                await RunOpenClawConfigUnsetAsync("models.providers.ollama");
            }
        }

        if (request.LmStudio != null)
        {
            if (request.LmStudio.Enabled && !string.IsNullOrWhiteSpace(request.LmStudio.BaseUrl))
            {
                var providerJson = BuildProviderJson(request.LmStudio).ToJsonString(JsonHelper.Compact);
                if (!await RunOpenClawConfigSetAsync("models.providers.lmstudio", providerJson))
                    return false;
            }
            else
            {
                await RunOpenClawConfigUnsetAsync("models.providers.lmstudio");
            }
        }

        if (request.LlamaCpp != null)
        {
            var llamaCppPath = GetLlamaCppConfigPath();
            var llamaCppConfig = new JsonObject
            {
                ["enabled"] = request.LlamaCpp.Enabled,
                ["binaryPath"] = request.LlamaCpp.BinaryPath,
                ["modelPath"] = request.LlamaCpp.ModelPath,
                ["baseUrl"] = request.LlamaCpp.BaseUrl,
                ["port"] = request.LlamaCpp.Port,
                ["nGpuLayers"] = request.LlamaCpp.NGpuLayers,
                ["contextSize"] = request.LlamaCpp.ContextSize,
                ["apiType"] = request.LlamaCpp.ApiType,
                ["extraArgs"] = request.LlamaCpp.ExtraArgs,
                ["threads"] = request.LlamaCpp.Threads,
                ["batchSize"] = request.LlamaCpp.BatchSize,
                ["cacheTypeK"] = request.LlamaCpp.CacheTypeK,
                ["cacheTypeV"] = request.LlamaCpp.CacheTypeV,
                ["useContBatching"] = request.LlamaCpp.UseContBatching,
            };
            await File.WriteAllTextAsync(llamaCppPath, llamaCppConfig.ToJsonString(JsonHelper.Indented));

            if (request.LlamaCpp.Enabled && !string.IsNullOrWhiteSpace(request.LlamaCpp.ModelPath))
            {
                var providerJson = BuildLlamaCppProviderJson(request.LlamaCpp).ToJsonString(JsonHelper.Compact);
                if (!await RunOpenClawConfigSetAsync("models.providers.llamacpp", providerJson))
                    return false;
            }
            else
            {
                await RunOpenClawConfigUnsetAsync("models.providers.llamacpp");
            }
        }

        return true;
    }

    public async Task<bool> RunOpenClawConfigSetAsync(string path, string jsonValue)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "openclaw",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("config");
            startInfo.ArgumentList.Add("set");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add(jsonValue);
            startInfo.ArgumentList.Add("--strict-json");
            startInfo.ArgumentList.Add("--merge");

            using var process = Process.Start(startInfo);
            if (process == null) return false;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                logger.LogWarning("openclaw config set 失败 ({Path}): {Stderr}", path, stderr);
                return false;
            }
            logger.LogInformation("openclaw config set 成功: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "openclaw config set 异常 ({Path})", path);
            return false;
        }
    }

    public async Task<bool> RunOpenClawConfigUnsetAsync(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "openclaw",
                Arguments = $"config unset {path}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "openclaw config unset 异常 ({Path})", path);
            return false;
        }
    }

    private static OpenClawLlamaCppConfigDto ParseLlamaCppConfig(JsonElement element)
    {
        var config = new OpenClawLlamaCppConfigDto();
        if (element.TryGetProperty("baseUrl", out var baseUrl))
            config.BaseUrl = baseUrl.GetString() ?? "";
        if (element.TryGetProperty("modelPath", out var modelPath))
            config.ModelPath = modelPath.GetString() ?? "";
        if (element.TryGetProperty("binaryPath", out var binaryPath))
            config.BinaryPath = binaryPath.GetString() ?? "";
        if (element.TryGetProperty("enabled", out var enabled))
            config.Enabled = enabled.GetBoolean();
        if (element.TryGetProperty("nGpuLayers", out var ngl) && ngl.ValueKind == JsonValueKind.Number)
            config.NGpuLayers = ngl.GetInt32();
        if (element.TryGetProperty("contextSize", out var ctx) && ctx.ValueKind == JsonValueKind.Number)
            config.ContextSize = ctx.GetInt32();
        if (element.TryGetProperty("port", out var port) && port.ValueKind == JsonValueKind.Number)
            config.Port = port.GetInt32();
        if (element.TryGetProperty("apiType", out var apiType))
            config.ApiType = apiType.GetString() ?? "";
        if (element.TryGetProperty("extraArgs", out var extraArgs))
            config.ExtraArgs = extraArgs.GetString() ?? "";
        if (element.TryGetProperty("threads", out var threads) && threads.ValueKind == JsonValueKind.Number)
            config.Threads = threads.GetInt32();
        if (element.TryGetProperty("batchSize", out var batchSize) && batchSize.ValueKind == JsonValueKind.Number)
            config.BatchSize = batchSize.GetInt32();
        if (element.TryGetProperty("cacheTypeK", out var cacheTypeK))
            config.CacheTypeK = cacheTypeK.GetString() ?? "";
        if (element.TryGetProperty("cacheTypeV", out var cacheTypeV))
            config.CacheTypeV = cacheTypeV.GetString() ?? "";
        if (element.TryGetProperty("useContBatching", out var useContBatching))
            config.UseContBatching = useContBatching.GetBoolean();
        return config;
    }

    private static OpenClawLocalProviderConfigDto ParseProviderConfig(JsonElement element)
    {
        var config = new OpenClawLocalProviderConfigDto();
        if (element.TryGetProperty("baseUrl", out var baseUrl))
            config.BaseUrl = baseUrl.GetString() ?? "";
        if (element.TryGetProperty("enabled", out var enabled))
            config.Enabled = enabled.GetBoolean();
        if (element.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                var model = new OpenClawLocalModelDto();
                if (m.TryGetProperty("id", out var id))
                    model.Id = id.GetString() ?? "";
                if (m.TryGetProperty("name", out var name))
                    model.Name = name.GetString() ?? "";

                config.Models.Add(model);
            }
        }
        return config;
    }

    public static JsonObject BuildLlamaCppProviderJson(OpenClawLlamaCppConfigDto config)
    {
        return new JsonObject
        {
            ["baseUrl"] = config.BaseUrl,
            ["modelPath"] = config.ModelPath,
            ["binaryPath"] = config.BinaryPath,
            ["enabled"] = config.Enabled,
            ["nGpuLayers"] = config.NGpuLayers,
            ["contextSize"] = config.ContextSize,
            ["port"] = config.Port,
            ["apiType"] = config.ApiType,
            ["extraArgs"] = config.ExtraArgs,
            ["threads"] = config.Threads,
            ["batchSize"] = config.BatchSize,
            ["cacheTypeK"] = config.CacheTypeK,
            ["cacheTypeV"] = config.CacheTypeV,
            ["useContBatching"] = config.UseContBatching,
        };
    }

    public static JsonObject BuildProviderJson(OpenClawLocalProviderConfigDto config)
    {
        var models = new JsonArray();
        foreach (var m in config.Models)
        {
            models.Add(new JsonObject
            {
                ["id"] = m.Id,
                ["name"] = m.Name,

            });
        }
        return new JsonObject
        {
            ["baseUrl"] = config.BaseUrl,
            ["enabled"] = config.Enabled,
            ["models"] = models,
        };
    }
}
