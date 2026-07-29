using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LmStudioService
{
        #region Running Models

        public async Task<List<RunningModelDto>> GetRunningModelsAsync(CancellationToken ct = default)
        {
            var list = new List<RunningModelDto>();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "lms",
                    Arguments = "ps --json",
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
                                    var modelId = JsonHelper.GetString(item, "modelKey");
                                    if (string.IsNullOrEmpty(modelId))
                                        modelId = JsonHelper.GetString(item, "identifier");
                                    if (string.IsNullOrEmpty(modelId))
                                        modelId = JsonHelper.GetString(item, "id");

                                    if (string.IsNullOrEmpty(modelId)) continue;

                                    var dto = new RunningModelDto
                                    {
                                        ToolId = "lmstudio",
                                        ToolName = "LM Studio",
                                        ModelName = modelId,
                                        DisplayName = modelId,
                                        Status = "running",
                                    };

                                    if (item.TryGetProperty("loadedContextLength", out var ctxProp) && ctxProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                        dto.ContextLength = ctxProp.GetInt32();

                                    list.Add(dto);
                                }
                                return list;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "lms ps --json 失败，回退到 HTTP API");
            }

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                var response = await client.GetAsync("http://localhost:1234/v1/models", ct);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataArray)) return list;

                foreach (var item in dataArray.EnumerateArray())
                {
                    var modelId = JsonHelper.GetString(item, "id");
                    var dto = new RunningModelDto
                    {
                        ToolId = "lmstudio",
                        ToolName = "LM Studio",
                        ModelName = modelId,
                        DisplayName = modelId,
                        Status = "running",
                    };
                    list.Add(dto);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 LM Studio 运行中模型失败");
            }

            return list;
        }

        #endregion

}
