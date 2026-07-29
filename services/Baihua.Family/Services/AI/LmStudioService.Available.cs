using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LmStudioService
{
        #region Available Models

        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                                    if (string.IsNullOrEmpty(key))
                                        key = JsonHelper.GetString(item, "name");
                                    if (string.IsNullOrEmpty(key))
                                        key = JsonHelper.GetString(item, "id");
                                    if (!string.IsNullOrEmpty(key) && seen.Add(key))
                                        list.Add(key);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "lms ls --json 失败");
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
                            if (seen.Add(name))
                                list.Add(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "扫描 LM Studio 模型目录失败");
                }
            }

            if (list.Count == 0)
            {
                try
                {
                    using var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var response = await client.GetAsync("http://localhost:1234/v1/models", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(ct);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var dataArray))
                        {
                            foreach (var item in dataArray.EnumerateArray())
                            {
                                var id = JsonHelper.GetString(item, "id");
                                if (!string.IsNullOrEmpty(id) && seen.Add(id))
                                    list.Add(id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "获取 LM Studio 可用模型列表失败");
                }
            }

            return list;
        }

        #endregion

}
