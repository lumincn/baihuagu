using Baihua.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Baihua.Contracts.Mcp;
using Baihua.Contracts.OpenClaw;
using Baihua.Family.Models;

namespace Baihua.Family.Services;

public partial class McpServerService
{
    #region Public API

    public McpInitializeResult Initialize(McpInitializeRequest request)
    {
        _logger.LogInformation("MCP 客户端连接: {ClientName} v{Version}, 协议版本: {Protocol}",
            request.ClientInfo.Name, request.ClientInfo.Version, request.ProtocolVersion);

        return new McpInitializeResult
        {
            ProtocolVersion = request.ProtocolVersion,
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpToolsCapability { ListChanged = false },
                Prompts = new McpPromptsCapability { ListChanged = false },
                Resources = new McpResourcesCapability { Subscribe = false, ListChanged = false }
            },
            ServerInfo = new McpImplementationInfo
            {
                Name = "taskrunner-mcp",
                Version = "1.1.0"
            }
        };
    }

    public McpToolListResult ListTools()
    {
        return new McpToolListResult
        {
            Tools = _tools.Values.ToList()
        };
    }

    public async Task<McpToolCallResult> CallToolAsync(McpToolCallRequest request, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(request.Name, out var handler))
        {
            return ErrorResult(string.Format(_loc["Mcp_UnknownTool"], request.Name));
        }

        try
        {
            return await handler(request.Arguments, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 工具调用失败: {ToolName}", request.Name);
            return ErrorResult(string.Format(_loc["Mcp_ToolCallFailed"], ex.Message));
        }
    }

    public McpPromptListResult ListPrompts()
    {
        return new McpPromptListResult
        {
            Prompts = new List<McpPrompt>
            {
                new()
                {
                    Name = "diagnose_symptoms",
                    Description = _loc["Mcp_DiagnoseSymptoms"],
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "symptoms", Description = _loc["Mcp_SymptomsParam"], Required = true },
                        new() { Name = "duration", Description = _loc["Mcp_DurationParam"], Required = false },
                    }
                },
                new()
                {
                    Name = "analyze_formula",
                    Description = _loc["Mcp_AnalyzeFormula"],
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "formula", Description = _loc["Mcp_FormulaParam"], Required = true },
                    }
                },
                new()
                {
                    Name = "study_classic",
                    Description = _loc["Mcp_StudyClassic"],
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "text", Description = _loc["Mcp_ClassicTextParam"], Required = true },
                        new() { Name = "source", Description = _loc["Mcp_SourceParam"], Required = false },
                    }
                },
                new()
                {
                    Name = "case_analysis",
                    Description = _loc["Mcp_CaseAnalysis"],
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "case_text", Description = _loc["Mcp_CaseTextParam"], Required = true },
                    }
                }
            }
        };
    }

    public McpPromptGetResult GetPrompt(McpPromptGetRequest request)
    {
        var symptoms = request.Arguments?.GetValueOrDefault("symptoms") ?? request.Arguments?.GetValueOrDefault("case_text") ?? "";
        var formula = request.Arguments?.GetValueOrDefault("formula") ?? "";
        var text = request.Arguments?.GetValueOrDefault("text") ?? "";
        var source = request.Arguments?.GetValueOrDefault("source") ?? "";
        var duration = request.Arguments?.GetValueOrDefault("duration") ?? "";

        return request.Name switch
        {
            "diagnose_symptoms" => new McpPromptGetResult
            {
                Description = _loc["Mcp_DiagnoseSymptomsTitle"],
                Messages = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "system",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = _loc["Mcp_DiagnoseSymptomsSystemPrompt"]
                        }
                    },
                    new()
                    {
                        Role = "user",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = string.IsNullOrWhiteSpace(duration)
                                ? string.Format(_loc["Mcp_PatientSymptoms"], symptoms)
                                : string.Format(_loc["Mcp_PatientSymptomsWithDuration"], symptoms, duration)
                        }
                    }
                }
            },
            "analyze_formula" => new McpPromptGetResult
            {
                Description = _loc["Mcp_AnalyzeFormulaTitle"],
                Messages = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "system",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = _loc["Mcp_AnalyzeFormulaSystemPrompt"]
                        }
                    },
                    new()
                    {
                        Role = "user",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = string.Format(_loc["Mcp_AnalyzeFormulaRequest"], formula)
                        }
                    }
                }
            },
            "study_classic" => new McpPromptGetResult
            {
                Description = _loc["Mcp_StudyClassicTitle"],
                Messages = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "system",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = _loc["Mcp_StudyClassicSystemPrompt"]
                        }
                    },
                    new()
                    {
                        Role = "user",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = string.IsNullOrWhiteSpace(source)
                                ? string.Format(_loc["Mcp_StudyClassicRequest"], text)
                                : string.Format(_loc["Mcp_StudyClassicRequestWithSource"], source, text)
                        }
                    }
                }
            },
            "case_analysis" => new McpPromptGetResult
            {
                Description = _loc["Mcp_CaseAnalysisTitle"],
                Messages = new List<McpPromptMessage>
                {
                    new()
                    {
                        Role = "system",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = _loc["Mcp_CaseAnalysisSystemPrompt"]
                        }
                    },
                    new()
                    {
                        Role = "user",
                        Content = new McpPromptMessageContent
                        {
                            Type = "text",
                            Text = string.Format(_loc["Mcp_CaseAnalysisRequest"], symptoms)
                        }
                    }
                }
            },
            _ => throw new Exception(string.Format(_loc["Mcp_UnknownPrompt"], request.Name))
        };
    }

    public McpResourceListResult ListResources()
    {
        var resources = new List<McpResource>();
        try
        {
            var vaults = _vaultSettings.GetVaults();
            foreach (var vault in vaults)
            {
                if (string.IsNullOrWhiteSpace(vault.Path) || !Directory.Exists(vault.Path))
                    continue;

                var notesPath = Path.Combine(vault.Path, "notes");
                if (!Directory.Exists(notesPath))
                    continue;

                var files = Directory.GetFiles(notesPath, "*.md", SearchOption.AllDirectories);
                foreach (var file in files.Take(50)) // 限制数量
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var relPath = file.Substring(notesPath.Length).Replace('\\', '/').TrimStart('/');
                    var uri = $"vault://{vault.Id}/{relPath}";
                    resources.Add(new McpResource
                    {
                        Uri = uri,
                        Name = fileName,
                        Description = string.Format(_loc["Mcp_ResourceNoteDesc"], vault.Name),
                        MimeType = "text/markdown"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫描知识库资源失败");
        }

        return new McpResourceListResult { Resources = resources };
    }

    public McpResourceReadResult ReadResource(McpResourceReadRequest request)
    {
        var uri = request.Uri;
        if (!uri.StartsWith("vault://"))
            throw new Exception(string.Format(_loc["Mcp_UnsupportedUri"], uri));

        var parts = uri[8..].Split('/', 2);
        if (parts.Length < 2)
            throw new Exception(string.Format(_loc["Mcp_InvalidUri"], uri));

        var vaultId = parts[0];
        var path = parts[1];

        var vault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
        if (vault == null || string.IsNullOrWhiteSpace(vault.Path))
            throw new Exception(string.Format(_loc["Mcp_VaultNotFound"], vaultId));

        var notesPath = Path.Combine(vault.Path, "notes");
        var filePath = Path.Combine(notesPath, path);

        // 安全检查
        var fullPath = Path.GetFullPath(filePath);
        var safeBase = Path.GetFullPath(notesPath);
        if (!fullPath.StartsWith(safeBase))
            throw new Exception(_loc["Mcp_InvalidPath"]);

        if (!File.Exists(filePath))
            throw new Exception(string.Format(_loc["Mcp_ResourceNotFound"], path));

        var content = File.ReadAllText(filePath);
        return new McpResourceReadResult
        {
            Contents = new List<McpResourceContent>
            {
                new()
                {
                    Uri = uri,
                    MimeType = "text/markdown",
                    Text = content
                }
            }
        };
    }

    #endregion


    #region Helpers

    private static McpToolCallResult TextResult(string text)
    {
        return new McpToolCallResult
        {
            Content = new List<McpToolCallContent>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private static McpToolCallResult ErrorResult(string message)
    {
        return new McpToolCallResult
        {
            IsError = true,
            Content = new List<McpToolCallContent>
            {
                new() { Type = "text", Text = $"❌ {message}" }
            }
        };
    }

    private static string? GetString(JsonElement? args, string key)
    {
        if (args == null || args.Value.ValueKind != JsonValueKind.Object) return null;
        if (args.Value.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int GetInt(JsonElement? args, string key, int defaultValue)
    {
        if (args == null || args.Value.ValueKind != JsonValueKind.Object) return defaultValue;
        if (args.Value.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                return val;
            // 尝试从字符串解析
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    private static bool GetBool(JsonElement? args, string key, bool defaultValue)
    {
        if (args == null || args.Value.ValueKind != JsonValueKind.Object) return defaultValue;
        if (args.Value.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    #endregion
}
