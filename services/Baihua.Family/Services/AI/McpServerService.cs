using Baihua.Core;
using Baihua.Core.Localization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.Mcp;
using Baihua.Contracts.OpenClaw;
using Baihua.Family.Models;

namespace Baihua.Family.Services;

/// <summary>
/// MCP (Model Context Protocol) Server 服务：暴露 JSON-RPC 工具接口
/// 使 Claude/Cursor/VS Code 等客户端可通过标准 MCP 协议调用 TaskRunner 功能
/// </summary>
public partial class McpServerService
{
    private readonly TaskManager _taskManager;
    private readonly IOpenClawTaskService _openClawTaskService;
    private readonly ILocalAiConfigService _localAiConfig;
    private readonly SystemHealthService _healthService;
    private readonly AiClientService _aiClientService;
    private readonly VaultSettingsService _vaultSettings;
    private readonly AiSettingsService _aiSettings;
    private readonly ILogger<McpServerService> _logger;
    private readonly IStringLocalizer<SharedResources> _loc;

    // 工具注册表
    private readonly Dictionary<string, McpTool> _tools = new();
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<McpToolCallResult>>> _handlers = new();

    public McpServerService(
        TaskManager taskManager,
        IOpenClawTaskService openClawTaskService,
        ILocalAiConfigService localAiConfig,
        SystemHealthService healthService,
        AiClientService aiClientService,
        VaultSettingsService vaultSettings,
        AiSettingsService aiSettings,
        ILogger<McpServerService> logger,
        IStringLocalizer<SharedResources> loc)
    {
        _taskManager = taskManager;
        _openClawTaskService = openClawTaskService;
        _localAiConfig = localAiConfig;
        _healthService = healthService;
        _aiClientService = aiClientService;
        _vaultSettings = vaultSettings;
        _aiSettings = aiSettings;
        _logger = logger;
        _loc = loc;

        RegisterTools();
    }

    #region Tool Registration

    private void RegisterTools()
    {
        // 1. query_ai - 同步 AI 查询
        _tools["query_ai"] = new McpTool
        {
            Name = "query_ai",
            Description = _loc["Mcp_QueryAi"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["query"] = new() { Type = "string", Description = _loc["Mcp_QueryParam"] },
                    ["model"] = new() { Type = "string", Description = _loc["Mcp_ModelParam"] },
                    ["system_prompt"] = new() { Type = "string", Description = _loc["Mcp_SystemPromptParam"] },
                },
                Required = new List<string> { "query" }
            }
        };
        _handlers["query_ai"] = HandleQueryAiAsync;

        // 2. create_ai_query_task - 创建 AI 查询后台任务
        _tools["create_ai_query_task"] = new McpTool
        {
            Name = "create_ai_query_task",
            Description = _loc["Mcp_CreateAiQueryTask"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["query"] = new() { Type = "string", Description = _loc["Mcp_QueryParam"] },
                    ["model"] = new() { Type = "string", Description = _loc["Mcp_ModelParamOptional"] },
                    ["save_to_vault"] = new() { Type = "boolean", Description = _loc["Mcp_SaveToVaultParam"], Default = false },
                    ["vault_id"] = new() { Type = "string", Description = _loc["Mcp_VaultIdParam"] },
                },
                Required = new List<string> { "query" }
            }
        };
        _handlers["create_ai_query_task"] = HandleCreateAiQueryTaskAsync;

        // 3. create_openclaw_task - 创建 OpenClaw 任务
        _tools["create_openclaw_task"] = new McpTool
        {
            Name = "create_openclaw_task",
            Description = _loc["Mcp_CreateOpenClawTask"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["prompt"] = new() { Type = "string", Description = _loc["Mcp_OpenClawPromptParam"] },
                },
                Required = new List<string> { "prompt" }
            }
        };
        _handlers["create_openclaw_task"] = HandleCreateOpenClawTaskAsync;

        // 4. get_task_status - 获取后台任务状态
        _tools["get_task_status"] = new McpTool
        {
            Name = "get_task_status",
            Description = _loc["Mcp_GetTaskStatus"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["task_id"] = new() { Type = "string", Description = _loc["Mcp_TaskIdParam"] },
                },
                Required = new List<string> { "task_id" }
            }
        };
        _handlers["get_task_status"] = HandleGetTaskStatusAsync;

        // 5. list_tasks - 列出后台任务
        _tools["list_tasks"] = new McpTool
        {
            Name = "list_tasks",
            Description = _loc["Mcp_ListTasks"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["limit"] = new() { Type = "integer", Description = _loc["Mcp_LimitParam"], Default = 20 },
                    ["status"] = new() { Type = "string", Description = _loc["Mcp_StatusFilterParam"] },
                },
                Required = null
            }
        };
        _handlers["list_tasks"] = HandleListTasksAsync;

        // 6. get_system_health - 系统健康检查
        _tools["get_system_health"] = new McpTool
        {
            Name = "get_system_health",
            Description = _loc["Mcp_GetSystemHealth"],
            InputSchema = new McpJsonSchema
            {
                Properties = new(),
                Required = null
            }
        };
        _handlers["get_system_health"] = HandleGetSystemHealthAsync;

        // 7. list_local_ai_models - 列出本地 AI 模型
        _tools["list_local_ai_models"] = new McpTool
        {
            Name = "list_local_ai_models",
            Description = _loc["Mcp_ListLocalAiModels"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["provider"] = new() { Type = "string", Description = _loc["Mcp_ProviderParam"] },
                },
                Required = null
            }
        };
        _handlers["list_local_ai_models"] = HandleListLocalAiModelsAsync;

        // 8. list_openclaw_tasks - 列出 OpenClaw 任务
        _tools["list_openclaw_tasks"] = new McpTool
        {
            Name = "list_openclaw_tasks",
            Description = _loc["Mcp_ListOpenClawTasks"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["limit"] = new() { Type = "integer", Description = _loc["Mcp_LimitParam"], Default = 20 },
                },
                Required = null
            }
        };
        _handlers["list_openclaw_tasks"] = HandleListOpenClawTasksAsync;

        // 9. get_openclaw_task_report - 获取 OpenClaw 任务报告
        _tools["get_openclaw_task_report"] = new McpTool
        {
            Name = "get_openclaw_task_report",
            Description = _loc["Mcp_GetOpenClawTaskReport"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["task_id"] = new() { Type = "integer", Description = _loc["Mcp_OpenClawTaskIdParam"] },
                },
                Required = new List<string> { "task_id" }
            }
        };
        _handlers["get_openclaw_task_report"] = HandleGetOpenClawTaskReportAsync;

        // 10. list_vaults
        _tools["list_vaults"] = new McpTool
        {
            Name = "list_vaults",
            Description = _loc["Mcp_ListVaults"],
            InputSchema = new McpJsonSchema
            {
                Properties = new(),
                Required = null
            }
        };
        _handlers["list_vaults"] = HandleListVaultsAsync;

        // 11. read_vault_note
        _tools["read_vault_note"] = new McpTool
        {
            Name = "read_vault_note",
            Description = _loc["Mcp_ReadVaultNote"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["vault_id"] = new() { Type = "string", Description = _loc["Mcp_VaultIdParam"] },
                    ["path"] = new() { Type = "string", Description = _loc["Mcp_NotePathParam"] },
                },
                Required = new List<string> { "vault_id", "path" }
            }
        };
        _handlers["read_vault_note"] = HandleReadVaultNoteAsync;

        // 12. search_vault
        _tools["search_vault"] = new McpTool
        {
            Name = "search_vault",
            Description = _loc["Mcp_SearchVault"],
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["vault_id"] = new() { Type = "string", Description = _loc["Mcp_VaultIdParam"] },
                    ["query"] = new() { Type = "string", Description = _loc["Mcp_SearchQueryParam"] },
                    ["limit"] = new() { Type = "integer", Description = _loc["Mcp_LimitParam"], Default = 20 },
                },
                Required = new List<string> { "vault_id", "query" }
            }
        };
        _handlers["search_vault"] = HandleSearchVaultAsync;
    }

    #endregion

}
