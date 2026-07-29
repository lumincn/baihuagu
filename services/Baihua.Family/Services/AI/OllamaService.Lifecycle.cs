using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class OllamaService
{
        #region Model Lifecycle

        public async Task<bool> DeleteModelAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var body = new Dictionary<string, object> { ["name"] = modelName };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, "http://localhost:11434/api/delete")
                    {
                        Content = content
                    }, ct);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Ollama 模型 {ModelName} 已删除", modelName);
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("删除 Ollama 模型 {ModelName} 失败: {StatusCode} {Error}", modelName, response.StatusCode, error);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "删除 Ollama 模型 {ModelName} 异常", modelName);
                return false;
            }
        }

        public async Task<ModelDetailsDto?> GetModelDetailsAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var body = new Dictionary<string, object> { ["name"] = modelName };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://localhost:11434/api/show", content, ct);
                if (!response.IsSuccessStatusCode) return null;

                var respJson = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(respJson);
                var root = doc.RootElement;

                var dto = new ModelDetailsDto
                {
                    Name = modelName,
                    ToolId = "ollama",
                };

                if (root.TryGetProperty("modelfile", out var mf) && mf.ValueKind == System.Text.Json.JsonValueKind.String)
                    dto.Modelfile = mf.GetString();

                if (root.TryGetProperty("parameters", out var pa) && pa.ValueKind == System.Text.Json.JsonValueKind.String)
                    dto.Parameters = pa.GetString();

                if (root.TryGetProperty("template", out var tp) && tp.ValueKind == System.Text.Json.JsonValueKind.String)
                    dto.Template = tp.GetString();

                if (root.TryGetProperty("license", out var lic) && lic.ValueKind == System.Text.Json.JsonValueKind.String)
                    dto.License = lic.GetString();

                dto.DetailsJson = respJson;
                return dto;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 Ollama 模型 {ModelName} 详情失败", modelName);
                return null;
            }
        }

        public async Task<bool> LoadModelAsync(string modelName, int keepAliveMinutes, CancellationToken ct = default)
        {
            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(60);
                var keepAlive = keepAliveMinutes < 0 ? "-1" : $"{keepAliveMinutes}m";
                var body = new Dictionary<string, object>
                {
                    ["model"] = modelName,
                    ["prompt"] = "",
                    ["stream"] = false,
                    ["keep_alive"] = keepAlive
                };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://localhost:11434/api/generate", content, ct);
                if (!response.IsSuccessStatusCode) return false;

                var respJson = await response.Content.ReadAsStringAsync(ct);
                return respJson.Contains("\"done_reason\":\"load\"") || respJson.Contains("\"done\":true");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "加载 Ollama 模型失败: {Model}", modelName);
                return false;
            }
        }

        public async Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = $"stop {modelName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(ct);
                    if (process.ExitCode == 0) return true;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "ollama stop CLI 失败，回退到 API 方式");
            }

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var body = new Dictionary<string, object>
                {
                    ["model"] = modelName,
                    ["prompt"] = "",
                    ["stream"] = false,
                    ["keep_alive"] = 0
                };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
                {
                    Content = content
                };
                request.Headers.ConnectionClose = true;
                var response = await client.SendAsync(request, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "卸载 Ollama 模型失败: {Model}", modelName);
                return false;
            }
        }

        #endregion

}
