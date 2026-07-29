using TaskRunner.Core.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Mcp;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Models;

namespace TaskRunner.Services;

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
        ILogger<McpServerService> logger)
    {
        _taskManager = taskManager;
        _openClawTaskService = openClawTaskService;
        _localAiConfig = localAiConfig;
        _healthService = healthService;
        _aiClientService = aiClientService;
        _vaultSettings = vaultSettings;
        _aiSettings = aiSettings;
        _logger = logger;

        RegisterTools();
    }

    #region Tool Registration

    private void RegisterTools()
    {
        // 1. query_ai - 同步 AI 查询
        _tools["query_ai"] = new McpTool
        {
            Name = "query_ai",
            Description = "直接调用 AI 模型进行查询并返回结果。支持指定模型，否则使用默认模型。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["query"] = new() { Type = "string", Description = "要查询的内容" },
                    ["model"] = new() { Type = "string", Description = "指定使用的 AI 模型，如 ollama/biancang:latest、Qwen/Qwen2.5-14B-Instruct。留空使用默认模型。" },
                    ["system_prompt"] = new() { Type = "string", Description = "可选的 system prompt，覆盖默认的经方家助手角色" },
                },
                Required = new List<string> { "query" }
            }
        };
        _handlers["query_ai"] = HandleQueryAiAsync;

        // 2. create_ai_query_task - 创建 AI 查询后台任务
        _tools["create_ai_query_task"] = new McpTool
        {
            Name = "create_ai_query_task",
            Description = "创建一个异步 AI 查询后台任务，返回 taskId 供后续轮询。适合需要长时间运行的查询。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["query"] = new() { Type = "string", Description = "要查询的内容" },
                    ["model"] = new() { Type = "string", Description = "指定使用的 AI 模型，留空使用默认模型" },
                    ["save_to_vault"] = new() { Type = "boolean", Description = "是否将结果保存到知识库", Default = false },
                    ["vault_id"] = new() { Type = "string", Description = "目标知识库 ID（save_to_vault 为 true 时必填）" },
                },
                Required = new List<string> { "query" }
            }
        };
        _handlers["create_ai_query_task"] = HandleCreateAiQueryTaskAsync;

        // 3. create_openclaw_task - 创建 OpenClaw 任务
        _tools["create_openclaw_task"] = new McpTool
        {
            Name = "create_openclaw_task",
            Description = "使用 OpenClaw 运行一个 AI 任务（通过 openclaw agent）。适合复杂的知识库分析任务。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["prompt"] = new() { Type = "string", Description = "OpenClaw 任务提示词" },
                },
                Required = new List<string> { "prompt" }
            }
        };
        _handlers["create_openclaw_task"] = HandleCreateOpenClawTaskAsync;

        // 4. get_task_status - 获取后台任务状态
        _tools["get_task_status"] = new McpTool
        {
            Name = "get_task_status",
            Description = "获取指定后台任务（ai_query、split_atom_notes、openclaw 等）的当前状态和结果。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["task_id"] = new() { Type = "string", Description = "任务 ID" },
                },
                Required = new List<string> { "task_id" }
            }
        };
        _handlers["get_task_status"] = HandleGetTaskStatusAsync;

        // 5. list_tasks - 列出后台任务
        _tools["list_tasks"] = new McpTool
        {
            Name = "list_tasks",
            Description = "列出最近创建的后台任务，包括 AI 查询、笔记拆分、OpenClaw 任务等。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["limit"] = new() { Type = "integer", Description = "返回数量上限", Default = 20 },
                    ["status"] = new() { Type = "string", Description = "按状态过滤：Pending、Running、Success、Failed、Timeout" },
                },
                Required = null
            }
        };
        _handlers["list_tasks"] = HandleListTasksAsync;

        // 6. get_system_health - 系统健康检查
        _tools["get_system_health"] = new McpTool
        {
            Name = "get_system_health",
            Description = "获取 TaskRunner 系统健康状态报告，包括 Git、Obsidian、Ollama、Python、Node.js、API Key、知识库等组件的状态。",
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
            Description = "扫描并列出本地 AI 服务（Ollama、LM Studio、llama.cpp）上的可用模型。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["provider"] = new() { Type = "string", Description = "指定 provider：ollama、lmstudio、llamacpp。留空扫描所有已配置的 provider。" },
                },
                Required = null
            }
        };
        _handlers["list_local_ai_models"] = HandleListLocalAiModelsAsync;

        // 8. list_openclaw_tasks - 列出 OpenClaw 任务
        _tools["list_openclaw_tasks"] = new McpTool
        {
            Name = "list_openclaw_tasks",
            Description = "列出 OpenClaw 任务历史。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["limit"] = new() { Type = "integer", Description = "返回数量上限", Default = 20 },
                },
                Required = null
            }
        };
        _handlers["list_openclaw_tasks"] = HandleListOpenClawTasksAsync;

        // 9. get_openclaw_task_report - 获取 OpenClaw 任务报告
        _tools["get_openclaw_task_report"] = new McpTool
        {
            Name = "get_openclaw_task_report",
            Description = "获取指定 OpenClaw 任务的完整报告内容。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["task_id"] = new() { Type = "integer", Description = "OpenClaw 任务的数据库 ID（不是 TaskId 字符串）" },
                },
                Required = new List<string> { "task_id" }
            }
        };
        _handlers["get_openclaw_task_report"] = HandleGetOpenClawTaskReportAsync;

        // 10. list_vaults
        _tools["list_vaults"] = new McpTool
        {
            Name = "list_vaults",
            Description = "列出所有已配置的知识库（Vault）。",
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
            Description = "读取知识库中的指定笔记内容。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["vault_id"] = new() { Type = "string", Description = "知识库 ID" },
                    ["path"] = new() { Type = "string", Description = "笔记路径（不含 .md 后缀），如 症状/头痛" },
                },
                Required = new List<string> { "vault_id", "path" }
            }
        };
        _handlers["read_vault_note"] = HandleReadVaultNoteAsync;

        // 12. search_vault
        _tools["search_vault"] = new McpTool
        {
            Name = "search_vault",
            Description = "在知识库中搜索关键词，返回匹配的笔记列表。",
            InputSchema = new McpJsonSchema
            {
                Properties = new Dictionary<string, McpJsonSchemaProperty>
                {
                    ["vault_id"] = new() { Type = "string", Description = "知识库 ID" },
                    ["query"] = new() { Type = "string", Description = "搜索关键词" },
                    ["limit"] = new() { Type = "integer", Description = "返回数量上限", Default = 20 },
                },
                Required = new List<string> { "vault_id", "query" }
            }
        };
        _handlers["search_vault"] = HandleSearchVaultAsync;
    }

    #endregion

}
