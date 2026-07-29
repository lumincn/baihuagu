using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services
{
    /// <summary>
    /// llama.cpp 本地模型服务：处理 llama.cpp 的检测、运行、模型管理
    /// </summary>
    public partial class LlamaCppService(
        ILogger<LlamaCppService> logger,
        IHttpClientFactory httpClientFactory,
        ILocalAiConfigService localAiConfig)
    {
        #region Tool Info

        public async Task<(bool Installed, string? Version, bool Running, string? ModelPath)> GetToolInfoAsync(CancellationToken ct = default)
        {
            var config = await localAiConfig.GetLocalAiConfigAsync();
            var llamaCpp = config.LlamaCpp;

            if (llamaCpp == null)
                return (false, null, false, null);

            var binaryExists = !string.IsNullOrWhiteSpace(llamaCpp.BinaryPath) && File.Exists(llamaCpp.BinaryPath);
            if (!binaryExists)
                return (false, null, false, null);

            var modelExists = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath) && File.Exists(llamaCpp.ModelPath);

            string? version = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = llamaCpp.BinaryPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(ct);
                    if (process.ExitCode == 0)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync(ct);
                        var match = Regex.Match(output, @"(\d+\.\d+\.?\d*)");
                        version = match.Success ? match.Groups[1].Value : output.Trim().Split('\n')[0].Trim();
                    }
                }
            }
            catch (Exception ex) { logger.LogDebug(ex, "操作失败"); }

            var running = false;
            if (llamaCpp.Enabled && modelExists)
            {
                try
                {
                    using var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var response = await client.GetAsync($"{llamaCpp.BaseUrl.TrimEnd('/')}/v1/models", ct);
                    running = response.IsSuccessStatusCode;
                }
                catch (Exception ex) { logger.LogDebug(ex, "检测服务运行状态失败"); }
            }

            var modelDir = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath)
                ? Path.GetDirectoryName(llamaCpp.ModelPath)
                : null;

            return (true, version ?? "unknown", running, modelDir);
        }

        #endregion
        #region Service Control

        public async Task<LocalAiServiceStatusDto> StartAsync(CancellationToken ct = default)
        {
            return await localAiConfig.DetectAndStartLocalAiAsync("llamacpp");
        }

        public async Task<bool> StopAsync(CancellationToken ct = default)
        {
            return await UnloadModelAsync(ct);
        }

        #endregion

        #region Json Helpers



        #endregion
    }
}
