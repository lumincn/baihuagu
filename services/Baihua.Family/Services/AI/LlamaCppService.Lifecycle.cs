using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services
{

    public partial class LlamaCppService
    {
        #region Model Lifecycle

        public async Task<ModelDetailsDto?> GetModelDetailsAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var config = await localAiConfig.GetLocalAiConfigAsync();
                var llamaCpp = config.LlamaCpp;
                if (llamaCpp != null && !string.IsNullOrWhiteSpace(llamaCpp.ModelPath))
                {
                    var fileName = Path.GetFileNameWithoutExtension(llamaCpp.ModelPath);
                    if (fileName.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                    {
                        var fi = new FileInfo(llamaCpp.ModelPath);
                        var details = new List<string>
                        {
                            $"Path: {llamaCpp.ModelPath}",
                            $"Size: {fi.Length / (1024.0 * 1024 * 1024):F2} GB",
                            $"Modified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                            $"Binary: {llamaCpp.BinaryPath}",
                            $"Port: {llamaCpp.Port}",
                            $"GPU Layers: {llamaCpp.NGpuLayers}",
                            $"Context Size: {llamaCpp.ContextSize}",
                        };

                        return new ModelDetailsDto
                        {
                            Name = modelName,
                            ToolId = "llamacpp",
                            Parameters = string.Join("\n", details),
                        };
                    }
                }

                var searchDir = llamaCpp?.ModelPath != null ? Path.GetDirectoryName(llamaCpp.ModelPath) : null;
                if (!string.IsNullOrWhiteSpace(searchDir) && Directory.Exists(searchDir))
                {
                    foreach (var file in Directory.EnumerateFiles(searchDir, "*.gguf", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                        {
                            var fi = new FileInfo(file);
                            return new ModelDetailsDto
                            {
                                Name = modelName,
                                ToolId = "llamacpp",
                                Parameters = $"Path: {file}\nSize: {fi.Length / (1024.0 * 1024 * 1024):F2} GB\nModified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 llama.cpp 模型 {ModelName} 详情失败", modelName);
            }
            return null;
        }

        public async Task<bool> LoadModelAsync(string modelName, CancellationToken ct = default)
        {
            var config = await localAiConfig.GetLocalAiConfigAsync();
            var llamaCpp = config.LlamaCpp;

            if (llamaCpp == null || !llamaCpp.Enabled)
            {
                logger.LogWarning("llama.cpp 未启用");
                return false;
            }

            var searchDir = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath)
                ? Path.GetDirectoryName(llamaCpp.ModelPath)
                : null;

            string? targetModelPath = null;
            if (!string.IsNullOrWhiteSpace(searchDir) && Directory.Exists(searchDir))
            {
                targetModelPath = Directory.EnumerateFiles(searchDir, "*.gguf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(modelName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(targetModelPath) || !File.Exists(targetModelPath))
            {
                logger.LogWarning("llama.cpp 模型文件未找到: {ModelName}", modelName);
                return false;
            }

            var currentModelFile = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath)
                ? Path.GetFileNameWithoutExtension(llamaCpp.ModelPath)
                : null;
            if (currentModelFile?.Equals(modelName, StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    using var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var response = await client.GetAsync($"{llamaCpp.BaseUrl.TrimEnd('/')}/v1/models", ct);
                    if (response.IsSuccessStatusCode) return true;
                }
                catch (Exception ex) { logger.LogDebug(ex, "检测服务运行状态失败"); }
            }

            await UnloadModelAsync(ct);

            llamaCpp.ModelPath = targetModelPath;
            await localAiConfig.SaveLocalAiConfigAsync(new SaveOpenClawLocalAiConfigRequest
            {
                LlamaCpp = llamaCpp
            });

            var status = await localAiConfig.DetectAndStartLocalAiAsync("llamacpp");
            return status.IsRunning;
        }

        public async Task<bool> UnloadModelAsync(CancellationToken ct = default)
        {
            try
            {
                var processes = Process.GetProcessesByName("llama-server");
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        await proc.WaitForExitAsync(ct);
                    }
                    catch (Exception ex) { logger.LogDebug(ex, "操作失败"); }
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pkill",
                        Arguments = "-f llama-server",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var pkill = Process.Start(psi);
                    if (pkill != null)
                    {
                        await pkill.WaitForExitAsync(ct);
                    }
                }
                catch (Exception ex) { logger.LogDebug(ex, "操作失败"); }

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "卸载 llama.cpp 模型失败");
                return false;
            }
        }

        #endregion
    }
}
