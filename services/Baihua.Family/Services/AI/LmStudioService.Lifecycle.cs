using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LmStudioService
{
        #region Model Lifecycle

        public async Task<ModelDetailsDto?> GetModelDetailsAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "lms",
                    Arguments = "ls --json",
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
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(output);
                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var item in doc.RootElement.EnumerateArray())
                                {
                                    var key = JsonHelper.GetString(item, "modelKey");
                                    if (string.IsNullOrEmpty(key)) key = JsonHelper.GetString(item, "name");
                                    if (string.IsNullOrEmpty(key)) key = JsonHelper.GetString(item, "id");
                                    if (!key.Equals(modelName, StringComparison.OrdinalIgnoreCase)) continue;

                                    var dto = new ModelDetailsDto
                                    {
                                        Name = modelName,
                                        ToolId = "lmstudio",
                                    };

                                    var displayName = JsonHelper.GetString(item, "displayName");
                                    var publisher = JsonHelper.GetString(item, "publisher");
                                    var path = JsonHelper.GetString(item, "path");
                                    var paramsStr = JsonHelper.GetString(item, "paramsString");
                                    var architecture = JsonHelper.GetString(item, "architecture");

                                    var details = new List<string>();
                                    if (!string.IsNullOrEmpty(displayName)) details.Add($"Display Name: {displayName}");
                                    if (!string.IsNullOrEmpty(publisher)) details.Add($"Publisher: {publisher}");
                                    if (!string.IsNullOrEmpty(path)) details.Add($"Path: {path}");
                                    if (!string.IsNullOrEmpty(paramsStr)) details.Add($"Parameters: {paramsStr}");
                                    if (!string.IsNullOrEmpty(architecture)) details.Add($"Architecture: {architecture}");

                                    if (item.TryGetProperty("quantization", out var q) && q.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        var qName = JsonHelper.GetString(q, "name");
                                        if (q.TryGetProperty("bits", out var bits) && bits.ValueKind == System.Text.Json.JsonValueKind.Number)
                                            details.Add($"Quantization: {qName} ({bits.GetInt32()}-bit)");
                                        else if (!string.IsNullOrEmpty(qName))
                                            details.Add($"Quantization: {qName}");
                                    }

                                    if (item.TryGetProperty("maxContextLength", out var ctx) && ctx.ValueKind == System.Text.Json.JsonValueKind.Number)
                                        details.Add($"Max Context: {ctx.GetInt32()}");

                                    long sizeBytes = 0;
                                    if (item.TryGetProperty("sizeBytes", out var sz) && sz.ValueKind == System.Text.Json.JsonValueKind.Number)
                                        sizeBytes = sz.GetInt64();
                                    if (sizeBytes > 0)
                                        details.Add($"Size: {sizeBytes / (1024.0 * 1024 * 1024):F2} GB");

                                    dto.Parameters = string.Join("\n", details);
                                    dto.DetailsJson = output;
                                    return dto;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 LM Studio 模型 {ModelName} 详情失败", modelName);
            }

            return await GetFileBasedModelDetailsAsync(modelName, LmStudioDownloadService.GetDefaultModelsPath(), ct);
        }

        public async Task<bool> LoadModelAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "lms",
                    Arguments = $"load {modelName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return false;
                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "加载 LM Studio 模型失败: {Model}", modelName);
                return false;
            }
        }

        public async Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "lms",
                    Arguments = $"unload {modelName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return false;
                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "卸载 LM Studio 模型失败: {Model}", modelName);
                return false;
            }
        }

        private async Task<ModelDetailsDto?> GetFileBasedModelDetailsAsync(string modelName, string searchDir, CancellationToken ct)
        {
            try
            {
                if (!Directory.Exists(searchDir)) return null;

                foreach (var file in Directory.EnumerateFiles(searchDir, "*.gguf", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                    {
                        var fi = new FileInfo(file);
                        return new ModelDetailsDto
                        {
                            Name = modelName,
                            ToolId = "lmstudio",
                            Parameters = $"Path: {file}\nSize: {fi.Length / (1024.0 * 1024 * 1024):F2} GB\nModified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "从文件系统获取 LM Studio 模型 {ModelName} 详情失败", modelName);
            }
            return null;
        }

        #endregion


}
