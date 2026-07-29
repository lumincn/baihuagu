using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class OllamaService
{
        #region Running Models

        public async Task<List<RunningModelDto>> GetRunningModelsAsync(CancellationToken ct = default)
        {
            var list = new List<RunningModelDto>();
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var response = await client.GetAsync("http://localhost:11434/api/ps", ct);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var modelsArray)) return list;

            foreach (var item in modelsArray.EnumerateArray())
            {
                var dto = new RunningModelDto
                {
                    ToolId = "ollama",
                    ToolName = "Ollama",
                    ModelName = JsonHelper.GetString(item, "name"),
                    DisplayName = JsonHelper.GetString(item, "name"),
                    Status = "running",
                    SizeBytes = JsonHelper.GetLong(item, "size"),
                };

                if (item.TryGetProperty("size_vram", out var vramProp) && vramProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                    dto.VramBytes = vramProp.GetInt64();

                if (item.TryGetProperty("expires_at", out var expProp) && expProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    if (DateTime.TryParse(expProp.GetString(), out var expDt))
                        dto.ExpiresAt = expDt;

                if (item.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                    dto.ContextLength = ctxProp.GetInt32();

                if (item.TryGetProperty("details", out var details))
                {
                    dto.Family = JsonHelper.GetString(details, "family");
                }

                list.Add(dto);
            }

            return list;
        }

        #endregion

        #region Available Models

        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("http://localhost:11434/api/tags", ct);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("models", out var modelsArray)) return list;

                foreach (var item in modelsArray.EnumerateArray())
                {
                    var name = JsonHelper.GetString(item, "name");
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 Ollama 可用模型列表失败");
            }
            return list;
        }

        #endregion

        #region Downloaded Models

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(List<RunningModelDto> runningModels, CancellationToken ct = default)
        {
            var list = new List<DownloadedModelDto>();
            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("http://localhost:11434/api/tags", ct);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("models", out var modelsArray)) return list;

                var runningSet = new HashSet<string>(runningModels
                    .Where(r => r.ToolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.ModelName), StringComparer.OrdinalIgnoreCase);

                foreach (var item in modelsArray.EnumerateArray())
                {
                    var name = JsonHelper.GetString(item, "name");
                    if (string.IsNullOrEmpty(name)) continue;

                    long size = 0;
                    if (item.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        size = sizeProp.GetInt64();

                    DateTime? modifiedAt = null;
                    if (item.TryGetProperty("modified_at", out var modProp) && modProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        if (DateTime.TryParse(modProp.GetString(), out var md))
                            modifiedAt = md;

                    list.Add(new DownloadedModelDto
                    {
                        Name = name,
                        ToolId = "ollama",
                        ToolName = "Ollama",
                        SizeBytes = size,
                        ModifiedAt = modifiedAt,
                        Digest = JsonHelper.GetString(item, "digest"),
                        IsRunning = runningSet.Contains(name),
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 Ollama 已下载模型列表失败");
            }
            return list;
        }

        #endregion

}
