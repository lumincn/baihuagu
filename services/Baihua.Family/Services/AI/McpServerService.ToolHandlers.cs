using Baihua.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Baihua.Contracts.Mcp;
using Baihua.Contracts.OpenClaw;
using Baihua.AI.Provider;
using Baihua.Family.Models;
using Baihua.AI.Provider;

namespace Baihua.Family.Services;

public partial class McpServerService
{
    #region Tool Handlers

    private async Task<McpToolCallResult> HandleQueryAiAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return ErrorResult(_loc["Mcp_MissingArgs"]);
        var query = GetString(args.Value, "query");
        if (string.IsNullOrWhiteSpace(query)) return ErrorResult(_loc["Mcp_QueryRequired"]);

        var model = GetString(args.Value, "model");
        var systemPrompt = GetString(args.Value, "system_prompt");

        try
        {
            // 确定模型和 provider
            var providers = _aiSettings.GetAiProviders();
            AiProviderConfig? provider = null;
            string modelName = "";

            // 处理 provider/model 格式（如 ollama/biancang:latest）
            if (!string.IsNullOrWhiteSpace(model) && model.Contains('/'))
            {
                var parts = model.Split('/', 2);
                var providerHint = parts[0].ToLowerInvariant();
                modelName = parts[1];

                // 按 provider id 匹配
                provider = providers.FirstOrDefault(p =>
                    p.Id.Equals(providerHint, StringComparison.OrdinalIgnoreCase));

                // 按 URL 特征匹配
                if (provider is null)
                {
                    provider = providers.FirstOrDefault(p =>
                        p.AiBaseUrl.Contains(providerHint, StringComparison.OrdinalIgnoreCase));
                }

                // 为 ollama 创建临时 provider
                if (provider is null && providerHint == "ollama")
                {
                    provider = new AiProviderConfig
                    {
                        Id = "ollama",
                        Name = "Ollama",
                        AiBaseUrl = "http://localhost:11434",
                        Models = new List<AiModelConfig>
                        {
                            new() { Name = modelName, IsMain = true }
                        }
                    };
                }
            }

            if (provider is null)
            {
                // 尝试根据模型名匹配 provider
                provider = providers.FirstOrDefault(p =>
                    p.Models.Any(m => m.Name.Equals(model ?? "", StringComparison.OrdinalIgnoreCase)));
            }

            if (provider is null)
            {
                provider = providers.FirstOrDefault(p => p.IsMain) ?? providers.FirstOrDefault()
                    ?? throw new Exception(_loc["Mcp_NoAiProvider"]);
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = !string.IsNullOrWhiteSpace(model)
                    ? model.Trim()
                    : provider.GetMainModel()
                      ?? "Qwen/Qwen2.5-14B-Instruct";
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, string.IsNullOrWhiteSpace(systemPrompt)
                    ? _loc["Mcp_DefaultSystemPrompt"]
                    : systemPrompt),
                new(ChatRole.User, query)
            };

            var options = AiClientService.BuildChatOptions();
            var response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                provider, modelName, messages, options, ct);

            var content = response.Text ?? _loc["Mcp_AiResponseEmpty"];
            return TextResult(content);
        }
        catch (Exception ex)
        {
            return ErrorResult(string.Format(_loc["Mcp_AiQueryFailed"], ex.Message));
        }
    }

    private Task<McpToolCallResult> HandleCreateAiQueryTaskAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return Task.FromResult(ErrorResult(_loc["Mcp_MissingArgs"]));
        var query = GetString(args.Value, "query");
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(ErrorResult(_loc["Mcp_QueryRequired"]));

        var model = GetString(args.Value, "model");
        var saveToVault = GetBool(args.Value, "save_to_vault", false);
        var vaultId = GetString(args.Value, "vault_id");

        var taskId = _taskManager.CreateTask("ai_query", new Dictionary<string, string>
        {
            ["query"] = query,
            ["model"] = model ?? "",
            ["saveToVault"] = saveToVault.ToString(),
            ["vaultId"] = vaultId ?? ""
        });

        // Note: 实际执行需要 TasksController 中相同的逻辑。MCP 只负责创建任务。
        // 如果需要立即执行，这里需要注入更多依赖并复制 TasksController 的执行逻辑。
        // 为简化，MCP 仅创建任务，用户用 get_task_status 查询。
        return Task.FromResult(TextResult(string.Format(_loc["Mcp_AiQueryTaskCreated"], taskId)));
    }

    private async Task<McpToolCallResult> HandleCreateOpenClawTaskAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return ErrorResult(_loc["Mcp_MissingArgs"]);
        var prompt = GetString(args.Value, "prompt");
        if (string.IsNullOrWhiteSpace(prompt)) return ErrorResult(_loc["Mcp_PromptRequired"]);

        try
        {
            var task = await _openClawTaskService.CreateTaskAsync(prompt.Trim());
            return TextResult(string.Format(_loc["Mcp_OpenClawTaskCreated"], task.TaskId, task.Id, task.Status, task.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
        }
        catch (Exception ex)
        {
            return ErrorResult(string.Format(_loc["Mcp_CreateOpenClawTaskFailed"], ex.Message));
        }
    }

    private Task<McpToolCallResult> HandleGetTaskStatusAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return Task.FromResult(ErrorResult(_loc["Mcp_MissingArgs"]));
        var taskId = GetString(args.Value, "task_id");
        if (string.IsNullOrWhiteSpace(taskId)) return Task.FromResult(ErrorResult(_loc["Mcp_TaskIdRequired"]));

        var task = _taskManager.GetTask(taskId);
        if (task == null) return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_TaskNotFound"], taskId)));

        var result = new JsonObject
        {
            ["taskId"] = task.Id,
            ["type"] = task.Type,
            ["status"] = task.Status.ToString(),
            ["progress"] = new JsonObject
            {
                ["current"] = task.Progress.Current,
                ["total"] = task.Progress.Total,
                ["percentage"] = task.Progress.Percentage,
                ["message"] = task.Progress.Message
            },
            ["createdAt"] = task.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ["updatedAt"] = task.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        if (task.Result != null)
        {
            result["result"] = new JsonObject
            {
                ["success"] = task.Result.Success,
                ["error"] = task.Result.Error,
            };
            if (task.Result.Data != null)
            {
                try
                {
                    var resultObj = result["result"] as JsonObject;
                    if (resultObj != null)
                        resultObj["data"] = JsonSerializer.Serialize(task.Result.Data);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "序列化 task result data 失败");
                }
            }
        }

        return Task.FromResult(TextResult(result.ToJsonString(JsonHelper.Indented)));
    }

    private Task<McpToolCallResult> HandleListTasksAsync(JsonElement? args, CancellationToken ct)
    {
        var limit = GetInt(args, "limit", 20);
        var status = GetString(args, "status");

        List<Baihua.Core.TaskInfo> tasks;
        if (!string.IsNullOrWhiteSpace(status))
        {
            tasks = _taskManager.GetTasksByStatus(status, limit);
        }
        else
        {
            tasks = _taskManager.GetAllTasks(limit, 0);
        }

        var array = new JsonArray();
        foreach (var t in tasks)
        {
            array.Add(new JsonObject
            {
                ["taskId"] = t.Id,
                ["type"] = t.Type,
                ["status"] = t.Status.ToString(),
                ["progressMessage"] = t.Progress.Message,
                ["progressPercent"] = t.Progress.Percentage,
                ["createdAt"] = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }

        var result = new JsonObject { ["tasks"] = array, ["count"] = array.Count };
        return Task.FromResult(TextResult(result.ToJsonString(JsonHelper.Indented)));
    }

    private async Task<McpToolCallResult> HandleGetSystemHealthAsync(JsonElement? args, CancellationToken ct)
    {
        try
        {
            var report = await _healthService.GetHealthReportAsync(ct);
            var result = new JsonObject
            {
                ["status"] = report.Status,
                ["healthScore"] = report.HealthScore,
                ["totalWallClockMs"] = report.TotalWallClockMs,
                ["timestamp"] = report.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ["components"] = new JsonArray(report.Components.Select(c => new JsonObject
                {
                    ["name"] = c.Name,
                    ["status"] = c.Status,
                    ["message"] = c.Message,
                    ["version"] = c.Version,
                    ["checkDurationMs"] = c.CheckDurationMs
                }).ToArray())
            };
            return TextResult(result.ToJsonString(JsonHelper.Indented));
        }
        catch (Exception ex)
        {
            return ErrorResult(string.Format(_loc["Mcp_HealthCheckFailed"], ex.Message));
        }
    }

    private async Task<McpToolCallResult> HandleListLocalAiModelsAsync(JsonElement? args, CancellationToken ct)
    {
        var provider = GetString(args, "provider");
        var allModels = new List<OpenClawLocalModelDto>();

        var providers = string.IsNullOrWhiteSpace(provider)
            ? new[] { "ollama", "lmstudio", "llamacpp" }
            : new[] { provider.ToLowerInvariant() };

        foreach (var p in providers)
        {
            try
            {
                var models = await _localAiConfig.ScanLocalModelsAsync(p);
                foreach (var m in models)
                {
                    m.Id = $"{p}/{m.Id}";
                }
                allModels.AddRange(models);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "扫描 {Provider} 模型失败", p);
            }
        }

        var array = new JsonArray(allModels.Select(m => new JsonObject
        {
            ["id"] = m.Id,
            ["name"] = m.Name,
            ["apiType"] = m.ApiType,
            ["contextWindow"] = m.ContextWindow,
            ["maxTokens"] = m.MaxTokens,
        }).ToArray());

        var result = new JsonObject { ["models"] = array, ["count"] = array.Count };
        return TextResult(result.ToJsonString(JsonHelper.Indented));
    }

    private async Task<McpToolCallResult> HandleListOpenClawTasksAsync(JsonElement? args, CancellationToken ct)
    {
        var limit = GetInt(args, "limit", 20);
        try
        {
            var tasks = await _openClawTaskService.GetTasksAsync(limit);
            var array = new JsonArray(tasks.Select(t => new JsonObject
            {
                ["id"] = t.Id,
                ["taskId"] = t.TaskId,
                ["prompt"] = t.Prompt.Length > 100 ? t.Prompt[..100] + "..." : t.Prompt,
                ["status"] = t.Status,
                ["createdAt"] = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ["completedAt"] = t.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss")
            }).ToArray());

            var result = new JsonObject { ["tasks"] = array, ["count"] = array.Count };
            return TextResult(result.ToJsonString(JsonHelper.Indented));
        }
        catch (Exception ex)
        {
            return ErrorResult(string.Format(_loc["Mcp_ListOpenClawTasksFailed"], ex.Message));
        }
    }

    private async Task<McpToolCallResult> HandleGetOpenClawTaskReportAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return ErrorResult(_loc["Mcp_MissingArgs"]);
        var taskId = GetInt(args.Value, "task_id", 0);
        if (taskId <= 0) return ErrorResult(_loc["Mcp_TaskIdPositive"]);

        try
        {
            var report = await _openClawTaskService.GetReportContentAsync(taskId);
            if (report == null) return ErrorResult(_loc["Mcp_ReportNotFound"]);
            return TextResult(report);
        }
        catch (Exception ex)
        {
            return ErrorResult(string.Format(_loc["Mcp_GetReportFailed"], ex.Message));
        }
    }

    private Task<McpToolCallResult> HandleListVaultsAsync(JsonElement? args, CancellationToken ct)
    {
        var vaults = _vaultSettings.GetVaults();
        var array = new JsonArray(vaults.Select(v => new JsonObject
        {
            ["id"] = v.Id,
            ["name"] = v.Name,
            ["path"] = v.Path,
        }).ToArray());

        var result = new JsonObject { ["vaults"] = array, ["count"] = array.Count };
        return Task.FromResult(TextResult(result.ToJsonString(JsonHelper.Indented)));
    }

    private Task<McpToolCallResult> HandleReadVaultNoteAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return Task.FromResult(ErrorResult(_loc["Mcp_MissingArgs"]));
        var vaultId = GetString(args.Value, "vault_id");
        var path = GetString(args.Value, "path");
        if (string.IsNullOrWhiteSpace(vaultId)) return Task.FromResult(ErrorResult(_loc["Mcp_VaultIdRequired"]));
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(ErrorResult(_loc["Mcp_PathRequired"]));

        var vault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
        if (vault == null) return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_VaultNotFound"], vaultId)));
        if (string.IsNullOrWhiteSpace(vault.Path) || !Directory.Exists(vault.Path))
            return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_VaultPathInvalid"], vault.Path)));

        try
        {
            var cleanPath = path.TrimEnd('/', '\\');
            if (cleanPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                cleanPath = cleanPath[..^3];
            if (cleanPath.StartsWith("notes/", StringComparison.OrdinalIgnoreCase))
                cleanPath = cleanPath.Substring("notes/".Length);

            var notesPath = Path.Combine(vault.Path, "notes");
            var filePath = Path.Combine(notesPath, cleanPath + ".md");

            if (!File.Exists(filePath))
                return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_NoteNotFound"], path)));

            var content = File.ReadAllText(filePath);
            return Task.FromResult(TextResult(content));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_ReadNoteFailed"], ex.Message)));
        }
    }

    private Task<McpToolCallResult> HandleSearchVaultAsync(JsonElement? args, CancellationToken ct)
    {
        if (args == null) return Task.FromResult(ErrorResult(_loc["Mcp_MissingArgs"]));
        var vaultId = GetString(args.Value, "vault_id");
        var query = GetString(args.Value, "query");
        var limit = GetInt(args, "limit", 20);
        if (string.IsNullOrWhiteSpace(vaultId)) return Task.FromResult(ErrorResult(_loc["Mcp_VaultIdRequired"]));
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(ErrorResult(_loc["Mcp_QueryRequired"]));

        var vault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
        if (vault == null) return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_VaultNotFound"], vaultId)));
        if (string.IsNullOrWhiteSpace(vault.Path) || !Directory.Exists(vault.Path))
            return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_VaultPathInvalid"], vault.Path)));

        try
        {
            var queryLower = query.ToLowerInvariant();
            var files = Directory.GetFiles(vault.Path, "*.md", SearchOption.AllDirectories);
            var results = new List<JsonObject>();

            foreach (var file in files)
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;

                    var content = File.ReadAllText(file);
                    var title = Path.GetFileNameWithoutExtension(file);
                    var relPath = file.Substring(vault.Path.Length).Replace('\\', '/').TrimStart('/');
                    if (relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        relPath = relPath[..^3];
                    if (relPath.StartsWith("notes/", StringComparison.OrdinalIgnoreCase))
                        relPath = relPath.Substring("notes/".Length);

                    var titleMatch = title.ToLowerInvariant().Contains(queryLower);
                    var contentMatch = content.ToLowerInvariant().Contains(queryLower);

                    if (titleMatch || contentMatch)
                    {
                        var preview = "";
                        var index = content.ToLowerInvariant().IndexOf(queryLower);
                        if (index >= 0)
                        {
                            var start = Math.Max(0, index - 50);
                            var length = Math.Min(200, content.Length - start);
                            preview = content.Substring(start, length).Replace("\n", " ").Replace("#", "").Replace("*", "");
                            if (start > 0) preview = "..." + preview;
                            if (start + length < content.Length) preview = preview + "...";
                        }

                        results.Add(new JsonObject
                        {
                            ["title"] = title,
                            ["path"] = relPath,
                            ["preview"] = preview.Trim(),
                            ["score"] = titleMatch ? 10 : 5
                        });
                    }
                }
                catch { /* 忽略单个文件读取错误 */ }
            }

            results = results.OrderByDescending(r => (int?)(r["score"]) ?? 0).Take(limit).ToList();
            var array = new JsonArray(results.ToArray());
            var result = new JsonObject { ["results"] = array, ["count"] = array.Count, ["query"] = query };
            return Task.FromResult(TextResult(result.ToJsonString(JsonHelper.Indented)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult(string.Format(_loc["Mcp_SearchFailed"], ex.Message)));
        }
    }

    #endregion
}
