using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace TaskRunner.Services;

/// <summary>
/// AI Function Calling 服务：为 AI 聊天提供可调用工具
/// 使 AI 能够主动搜索知识库、获取系统信息等
/// </summary>
public class AiFunctionService
{
    private readonly VaultSettingsService _vaultSettings;
    private readonly VaultNoteIndexer _vaultNoteIndexer;
    private readonly SystemHealthService _healthService;
    private readonly AnkiCardGenerator _cardGenerator;
    private readonly ILogger<AiFunctionService> _logger;

    public AiFunctionService(
        VaultSettingsService vaultSettings,
        VaultNoteIndexer vaultNoteIndexer,
        SystemHealthService healthService,
        AnkiCardGenerator cardGenerator,
        ILogger<AiFunctionService> logger)
    {
        _vaultSettings = vaultSettings;
        _vaultNoteIndexer = vaultNoteIndexer;
        _healthService = healthService;
        _cardGenerator = cardGenerator;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有可用的 AI 工具
    /// </summary>
    public IList<AITool> GetAllTools()
    {
        return new List<AITool>
        {
            AIFunctionFactory.Create(SearchVaultAsync, "search_vault"),
            AIFunctionFactory.Create(GetCurrentDateAsync, "get_current_date"),
            AIFunctionFactory.Create(ListVaultsAsync, "list_vaults"),
            AIFunctionFactory.Create(CreateNoteAsync, "create_note"),
            AIFunctionFactory.Create(GetSystemStatusAsync, "get_system_status"),
        };
    }

    /// <summary>
    /// 搜索知识库中的笔记
    /// </summary>
    [Description("搜索知识库中的相关笔记，返回笔记标题和内容预览。当用户询问笔记、知识库相关内容时使用。")]
    private async Task<string> SearchVaultAsync(
        [Description("搜索关键词，如\"桂枝汤\"、\"太阳中风\"、\"发热恶寒\"")] string query)
    {
        try
        {
            var activeVault = _vaultSettings.GetActiveVault();
            if (activeVault == null)
                return "当前未配置知识库，无法搜索。";

            _logger.LogInformation("[AI Function] search_vault: query={Query}, vault={VaultId}", query, activeVault.Id);

            var results = await _vaultNoteIndexer.SearchAsync(activeVault.Id, query);
            if (results.Count == 0)
                return "未在知识库中找到相关内容。";

            var lines = results.Take(5).Select(r =>
                $"📄 **{r.Title}**\n{r.Preview}\n");

            return $"在知识库中找到 {results.Count} 条相关笔记（显示前 5 条）：\n\n" +
                   string.Join("\n---\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI Function] search_vault 失败");
            return $"搜索失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取当前日期时间
    /// </summary>
    [Description("获取当前日期和时间，格式为 yyyy-MM-dd HH:mm:ss。当用户询问时间、日期、今天几号等问题时使用。")]
    private Task<string> GetCurrentDateAsync()
    {
        return Task.FromResult($"当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }

    /// <summary>
    /// 列出已配置的知识库
    /// </summary>
    [Description("列出当前已配置的所有知识库名称和路径。当用户询问有哪些知识库、知识库配置情况时使用。")]
    private Task<string> ListVaultsAsync()
    {
        var vaults = _vaultSettings.GetVaults();
        if (vaults.Count == 0)
            return Task.FromResult("当前未配置任何知识库。");

        var lines = vaults.Select(v => $"- {v.Name} ({v.Path})");
        return Task.FromResult("已配置的知识库：\n" + string.Join("\n", lines));
    }

    /// <summary>
    /// 创建笔记到知识库
    /// </summary>
    [Description("创建一篇笔记并保存到知识库。当用户要求保存、记录、创建笔记时使用。")]
    private async Task<string> CreateNoteAsync(
        [Description("笔记标题，简洁概括主题，如\"桂枝汤的功效与主治\"")] string title,
        [Description("笔记的 Markdown 内容，支持标题、列表、引用等格式")] string content)
    {
        try
        {
            var activeVault = _vaultSettings.GetActiveVault();
            if (activeVault == null)
                return "当前未配置知识库，无法保存笔记。请先配置知识库。";

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                return "标题和内容不能为空。";

            var safeTitle = GenerateSafeFileName(title);
            var notesRoot = Path.Combine(activeVault.Path, "notes");
            var notePath = Path.Combine(notesRoot, $"AI 生成/{safeTitle}.md");
            var noteDir = Path.GetDirectoryName(notePath) ?? throw new InvalidOperationException($"无法获取目录：{notePath}");
            Directory.CreateDirectory(noteDir);

            var sourceInfo = $"> 📌 **来源**: AI 生成  \n" +
                $"> ⏰ **时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  \n\n";

            var fullContent = $"# {title}\n\n{sourceInfo}{content}";
            await File.WriteAllTextAsync(notePath, fullContent);

            // 自动为该笔记生成 Anki 记忆卡片
            try
            {
                var relativePath = Path.GetRelativePath(notesRoot, notePath);
                relativePath = relativePath.Substring(0, relativePath.Length - 3); // 去掉 .md
                _ = Task.Run(async () => await _cardGenerator.GenerateWithAiAsync(relativePath));
                _logger.LogInformation("[AI Function] create_note: 笔记已保存，已触发卡片生成任务：{Path}", relativePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AI Function] create_note: 自动触发卡片生成失败");
            }

            return $"笔记《{title}》已保存到知识库 [{activeVault.Name}]。";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI Function] create_note 失败");
            return $"保存笔记失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    [Description("获取系统各组件的健康状态，包括 Git、Obsidian、Ollama、Python、Node、API Key、知识库路径等。当用户询问系统状态、是否正常运行、有哪些问题时使用。")]
    private async Task<string> GetSystemStatusAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var report = await _healthService.GetHealthReportAsync(cts.Token);

            var lines = report.Components.Select(c =>
            {
                var icon = c.Status switch
                {
                    "healthy" => "✅",
                    "warning" => "⚠️",
                    "critical" => "❌",
                    _ => "❓"
                };
                return $"{icon} **{c.Name}**: {c.Status} ({c.Message})";
            });

            var summary = report.Status switch
            {
                "healthy" => "系统运行正常",
                "warning" => "系统有警告项需要关注",
                "critical" => "系统有严重问题需要处理",
                _ => "系统状态未知"
            };

            return $"{summary}（健康评分：{report.HealthScore}/100）\n\n" + string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI Function] get_system_status 失败");
            return $"获取系统状态失败：{ex.Message}";
        }
    }

    private static string GenerateSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var invalidSet = new HashSet<char>(invalid);
        return string.Concat(title.Where(c => !invalidSet.Contains(c)).Take(50));
    }
}
