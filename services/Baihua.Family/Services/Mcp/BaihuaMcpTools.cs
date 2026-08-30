using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Baihua.Core.Services;
using ModelContextProtocol.Server;

namespace Baihua.Family.Services.Mcp;

/// <summary>
/// 百花 MCP 工具：知识库只读能力（搜索/列表/读笔记）。
/// 挂在 Baihua.Family 的 /mcp 端点（streamable-http），供 DSH / Claude Desktop / Cursor 等 MCP 客户端使用。
/// vault_search 与 vault_read_note 走 HTTP 调 Vault（k8s 下 Family/Vault 不同 pod，文件系统不共享，
/// 且搜索逻辑含 obsidian-cli/语义/FTS5/重排，复用 SearchController 单一来源）；
/// vault_list 直接调 VaultSettingsService（Family 已连 vault 库，零 HTTP 跳）。
/// 工具名与原 baihua-mcp-server 保持一致，DSH 侧 mcp__baihua__ 前缀工具名不变，可无缝切换。
/// </summary>
[McpServerToolType]
public sealed class BaihuaVaultTools
{
    private readonly VaultSettingsService _vaultSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOpts = JsonSerializerOptions.Web;

    public BaihuaVaultTools(VaultSettingsService vaultSettings, IHttpClientFactory httpClientFactory)
    {
        _vaultSettings = vaultSettings;
        _httpClientFactory = httpClientFactory;
    }

    [McpServerTool(Name = "baihua_vault_search"), Description("搜索百花知识库（全文/语义），返回命中的笔记片段。query 为关键词，vaultId 可选（留空搜全部）。")]
    public async Task<string> VaultSearch(string query, string? vaultId = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Vault");
            var url = $"/api/search?q={Uri.EscapeDataString(query)}&vaultId={Uri.EscapeDataString(vaultId ?? "")}";
            using var resp = await client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode
                ? json
                : JsonSerializer.Serialize(new { ok = false, error = $"HTTP {(int)resp.StatusCode}", detail = json }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool(Name = "baihua_vault_list"), Description("列出百花全部知识库（名称/路径/来源）。")]
    public string VaultList()
    {
        var vaults = _vaultSettings.GetVaults();
        return JsonSerializer.Serialize(new { vaults }, JsonOpts);
    }

    [McpServerTool(Name = "baihua_vault_read_note"), Description("读取百花知识库中的一条笔记（markdown 全文）。path 为笔记相对路径（如 基础认识/笔记.md），vaultId 为知识库 id。")]
    public async Task<string> VaultReadNote(string path, string vaultId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Vault");
            var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            var url = $"/vault/read/{escaped}?vaultId={Uri.EscapeDataString(vaultId)}";
            using var resp = await client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode
                ? json
                : JsonSerializer.Serialize(new { ok = false, error = $"HTTP {(int)resp.StatusCode}", detail = json }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool(Name = "baihua_vault_create"), Description("创建百花知识库。name 必填（唯一，重名报错）；industry 可选（如 国学/编程/中医）；path 可选（留空自动生成到 data/vaults/{industry}/{name}）。返回新知识库 id/name/path。")]
    public string VaultCreate(string name, string? industry = null, string? path = null)
    {
        try
        {
            var vault = _vaultSettings.AddVault(name, path ?? "", industry ?? "");
            return JsonSerializer.Serialize(new { ok = true, vault }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool(Name = "baihua_vault_write_note"), Description("在百花知识库中新建或覆盖一篇笔记。path 为笔记相对路径（如 学习计划/入门.md），vaultId 为知识库 id（先经 baihua_vault_list 或 baihua_vault_create 取得），content 为 markdown 全文。")]
    public async Task<string> VaultWriteNote(string path, string vaultId, string content)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Vault");
            var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            var url = $"/vault/write/{escaped}?vaultId={Uri.EscapeDataString(vaultId)}";
            using var resp = await client.PostAsJsonAsync(url, new { content });
            var json = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode
                ? JsonSerializer.Serialize(new { ok = true, success = true }, JsonOpts)
                : JsonSerializer.Serialize(new { ok = false, error = $"HTTP {(int)resp.StatusCode}", detail = json }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOpts);
        }
    }
}

/// <summary>
/// 百花 MCP 工具：家庭数据只读能力（记账汇总/任务列表）。
/// 直接调 Family 内部服务层（FamilyBudgetService / TaskManager），零 HTTP 跳，强类型契约。
/// </summary>
[McpServerToolType]
public sealed class BaihuaFamilyTools
{
    private readonly FamilyBudgetService _budget;
    private readonly TaskManager _taskManager;
    private static readonly JsonSerializerOptions JsonOpts = JsonSerializerOptions.Web;

    public BaihuaFamilyTools(FamilyBudgetService budget, TaskManager taskManager)
    {
        _budget = budget;
        _taskManager = taskManager;
    }

    [McpServerTool(Name = "baihua_budget_summary"), Description("查看百花家庭记账汇总（本月收入/支出/结余/分类）。")]
    public async Task<string> BudgetSummary()
    {
        var summary = await _budget.GetSummaryAsync(null, null);
        return JsonSerializer.Serialize(summary, JsonOpts);
    }

    [McpServerTool(Name = "baihua_tasks_list"), Description("查看百花家庭任务/待办列表（标题/状态/时间）。status 可选筛选状态，limit 限制返回数量（默认 50，最大 200）。")]
    public string TasksList(string? status = null, int limit = 50)
    {
        var tasks = _taskManager.GetAllTasks(limit: Math.Clamp(limit, 1, 200));
        if (!string.IsNullOrEmpty(status))
            tasks = tasks.Where(t => t.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        return JsonSerializer.Serialize(new { tasks, total = tasks.Count }, JsonOpts);
    }
}