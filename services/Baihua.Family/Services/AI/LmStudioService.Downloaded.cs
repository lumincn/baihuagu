using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LmStudioService
{
        #region Downloaded Models

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(List<RunningModelDto> runningModels, CancellationToken ct = default)
        {
            var list = new List<DownloadedModelDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runningSet = new HashSet<string>(runningModels
                .Where(r => r.ToolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ModelName), StringComparer.OrdinalIgnoreCase);

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
                                    if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;

                                    long size = 0;
                                    if (item.TryGetProperty("sizeBytes", out var sz) && sz.ValueKind == System.Text.Json.JsonValueKind.Number)
                                        size = sz.GetInt64();

                                    DateTime? modifiedAt = null;
                                    if (item.TryGetProperty("modifiedAt", out var modProp) && modProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                        if (DateTime.TryParse(modProp.GetString(), out var md))
                                            modifiedAt = md;
                                    if (modifiedAt == null && item.TryGetProperty("downloadedAt", out var dlProp) && dlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                        if (DateTime.TryParse(dlProp.GetString(), out var dd))
                                            modifiedAt = dd;

                                    list.Add(new DownloadedModelDto
                                    {
                                        Name = key,
                                        ToolId = "lmstudio",
                                        ToolName = "LM Studio",
                                        SizeBytes = size,
                                        ModifiedAt = modifiedAt,
                                        IsRunning = runningSet.Contains(key),
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "lms ls --json 获取已下载模型失败");
            }

            if (list.Count == 0)
            {
                try
                {
                    var modelsPath = LmStudioDownloadService.GetDefaultModelsPath();
                    if (Directory.Exists(modelsPath))
                    {
                        var ggufFiles = Directory.EnumerateFiles(modelsPath, "*.gguf", SearchOption.AllDirectories);
                        foreach (var file in ggufFiles)
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            if (!seen.Add(name)) continue;

                            long size = 0;
                            DateTime? modifiedAt = null;
                            try
                            {
                                var fi = new FileInfo(file);
                                size = fi.Length;
                                modifiedAt = fi.LastWriteTime;
                            }
                            catch (Exception ex) { logger.LogDebug(ex, "文件系统操作失败"); }

                            list.Add(new DownloadedModelDto
                            {
                                Name = name,
                                ToolId = "lmstudio",
                                ToolName = "LM Studio",
                                SizeBytes = size,
                                ModifiedAt = modifiedAt,
                                IsRunning = runningSet.Contains(name),
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "扫描 LM Studio 已下载模型目录失败");
                }
            }

            return list;
        }

        #endregion

}
