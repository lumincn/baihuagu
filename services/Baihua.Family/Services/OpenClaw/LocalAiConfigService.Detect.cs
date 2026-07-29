using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;

namespace TaskRunner.Services;

public partial class LocalAiConfigService
{
    #region Detect and Start Local AI

    public async Task<LocalAiServiceStatusDto> DetectAndStartLocalAiAsync(string provider)
    {
        var result = new LocalAiServiceStatusDto { Provider = provider };

        // llama.cpp 特殊处理
        if (provider.ToLowerInvariant() == "llamacpp")
        {
            return await DetectAndStartLlamaCppAsync();
        }

        var (checkUrl, startCmd, startArgs, displayName) = provider.ToLowerInvariant() switch
        {
            "ollama" => ("http://localhost:11434/api/tags", "ollama", "serve", "Ollama"),
            "lmstudio" => ("http://localhost:1234/v1/models", "lms", "server start", "LM Studio"),
            _ => (null, null, null, provider)
        };

        if (checkUrl == null)
        {
            result.Message = $"不支持的 provider: {provider}";
            return result;
        }

        // 1. 检测服务是否已运行
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync(checkUrl);
            if (response.IsSuccessStatusCode)
            {
                result.IsRunning = true;
                result.Message = $"{displayName} 服务正在运行";
                return result;
            }
        }
        catch
        {
            // 未运行，继续尝试启动
        }

        // 2. 尝试启动服务
        result.AttemptedStart = true;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = startCmd,
                Arguments = startArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                result.Message = $"启动 {displayName} 失败：无法创建进程";
                return result;
            }

            // 等待服务启动
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                try
                {
                    using var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var response = await client.GetAsync(checkUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        result.IsRunning = true;
                        result.StartSuccess = true;
                        result.Message = $"{displayName} 启动成功";
                        return result;
                    }
                }
                catch (Exception ex) { logger.LogDebug(ex, "探测 {DisplayName} 启动状态失败", displayName); }
            }

            result.Message = $"{displayName} 已尝试启动，但服务未在预期时间内就绪，请检查日志";
            logger.LogWarning("{DisplayName} 启动后未就绪", displayName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动 {DisplayName} 失败", displayName);
            result.Message = $"启动 {displayName} 失败: {ex.Message}";
        }

        return result;
    }

    private async Task<LocalAiServiceStatusDto> DetectAndStartLlamaCppAsync()
    {
        var result = new LocalAiServiceStatusDto { Provider = "llamacpp" };
        var config = await GetLocalAiConfigAsync();
        var llamaCpp = config.LlamaCpp;

        if (llamaCpp == null || !llamaCpp.Enabled)
        {
            result.Message = "llama.cpp 未启用";
            return result;
        }

        if (string.IsNullOrWhiteSpace(llamaCpp.BinaryPath) || !File.Exists(llamaCpp.BinaryPath))
        {
            result.Message = $"llama-server 路径无效: {llamaCpp.BinaryPath}";
            return result;
        }

        if (string.IsNullOrWhiteSpace(llamaCpp.ModelPath) || !File.Exists(llamaCpp.ModelPath))
        {
            result.Message = $"模型文件不存在: {llamaCpp.ModelPath}";
            return result;
        }

        var checkUrl = $"{llamaCpp.BaseUrl.TrimEnd('/')}/v1/models";

        // 1. 检测服务是否已运行
        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync(checkUrl);
            if (response.IsSuccessStatusCode)
            {
                result.IsRunning = true;
                result.Message = "llama.cpp 服务正在运行";
                return result;
            }
        }
        catch
        {
            // 未运行，继续尝试启动
        }

        // 2. 尝试启动服务
        result.AttemptedStart = true;
        try
        {
            var oneapiScript = "/opt/intel/oneapi/setvars.sh";
            var hasOneapi = File.Exists(oneapiScript);
            var args = $"-m \"{llamaCpp.ModelPath}\" -ngl {llamaCpp.NGpuLayers} --port {llamaCpp.Port} --host 127.0.0.1 -c {llamaCpp.ContextSize}";

            // 组合预定义参数
            if (llamaCpp.UseFlashAttn) args += " --flash-attn";
            if (llamaCpp.UseMlock) args += " --mlock";
            if (llamaCpp.UseNoMmap) args += " --no-mmap";
            if (llamaCpp.Threads > 0) args += $" -t {llamaCpp.Threads}";
            if (llamaCpp.BatchSize > 0) args += $" -b {llamaCpp.BatchSize}";
            if (!string.IsNullOrWhiteSpace(llamaCpp.CacheTypeK)) args += $" --cache-type-k {llamaCpp.CacheTypeK}";
            if (!string.IsNullOrWhiteSpace(llamaCpp.CacheTypeV)) args += $" --cache-type-v {llamaCpp.CacheTypeV}";
            if (llamaCpp.UseContBatching) args += " --cont-batching";

            if (!string.IsNullOrWhiteSpace(llamaCpp.ExtraArgs))
                args += " " + llamaCpp.ExtraArgs.Trim();
            var shellCmd = hasOneapi
                ? $"source {oneapiScript} > /dev/null 2>&1 && {llamaCpp.BinaryPath} {args}"
                : $"{llamaCpp.BinaryPath} {args}";

            logger.LogInformation("正在启动 llama.cpp: {Cmd}", shellCmd);
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{shellCmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                result.Message = "启动 llama.cpp 失败：无法创建进程";
                return result;
            }

            // llama.cpp 加载模型需要较长时间
            await Task.Delay(TimeSpan.FromSeconds(5));

            // 轮询检测服务是否就绪（最多 30 秒）
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                try
                {
                    using var httpClient = httpClientFactory.CreateClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(3);
                    var response = await httpClient.GetAsync(checkUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        result.IsRunning = true;
                        result.StartSuccess = true;
                        result.Message = "llama.cpp 启动成功（GPU 加速已启用）";
                        return result;
                    }
                }
                catch (Exception ex) { logger.LogDebug(ex, "探测 llama.cpp 启动状态失败"); }
            }

            result.Message = "llama.cpp 已尝试启动，但服务未在预期时间内就绪，请检查日志";
            logger.LogWarning("llama.cpp 启动后未就绪");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动 llama.cpp 失败");
            result.Message = $"启动 llama.cpp 失败: {ex.Message}";
        }

        return result;
    }

    #endregion

}
