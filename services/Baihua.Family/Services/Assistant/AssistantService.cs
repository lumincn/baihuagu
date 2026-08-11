using System.Net.Http.Json;
using System.Text.Json;
using Baihua.Contracts;
using Baihua.Contracts.Assistant;
using Baihua.Family.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Baihua.Family.Services;

/// <summary>
/// AI 数字助理：设置（开关）、每日兴趣分析、触发知识库生成。
/// 每日分析：读取当天用户活动 → AI 推测兴趣主题（0-3 个）→
/// 对每个主题调用 vault-generation 生成知识库（复用知识库生成任务）。
/// </summary>
public class AssistantService
{
    private readonly ILogger<AssistantService> _logger;
    private readonly AiClientService _aiClient;
    private readonly AiSettingsService _aiSettings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly UserActivityService _activities;
    private readonly object _lock = new();

    private static readonly string SettingsPath =
        Path.Combine(BaihuaPaths.Home, "assistant", "settings.json");

    public AssistantService(
        ILogger<AssistantService> logger,
        AiClientService aiClient,
        AiSettingsService aiSettings,
        IHttpClientFactory httpFactory,
        UserActivityService activities)
    {
        _logger = logger;
        _aiClient = aiClient;
        _aiSettings = aiSettings;
        _httpFactory = httpFactory;
        _activities = activities;
    }

    private string DataDir => Path.Combine(BaihuaPaths.Home, "assistant");
    private string AnalysisFile(DateTime day) => Path.Combine(DataDir, $"analysis-{day:yyyy-MM-dd}.json");

    #region 设置与开关

    public AssistantSettingsDto GetSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AssistantSettingsDto>(File.ReadAllText(SettingsPath)) ?? new AssistantSettingsDto();
        }
        catch { }
        return new AssistantSettingsDto();
    }

    public void SaveSettings(AssistantSettingsDto settings)
    {
        Directory.CreateDirectory(DataDir);
        lock (_lock)
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public bool IsEnabled() => GetSettings().Enabled;

    #endregion

    #region 每日分析

    /// <summary>今日是否已分析</summary>
    public bool IsTodayAnalyzed()
    {
        var settings = GetSettings();
        var today = DateTime.Today;
        // 若已存在今天的分析结果则视为已分析（幂等，手动触发可 force）
        return File.Exists(AnalysisFile(today));
    }

    /// <summary>执行每日分析（当天活动 → 兴趣 → 生成知识库）</summary>
    public async Task<AssistantAnalysisDto> AnalyzeAsync(bool force = false, CancellationToken ct = default)
    {
        var settings = GetSettings();
        if (!settings.Enabled && !force)
            throw new InvalidOperationException("助理已关闭");

        var today = DateTime.Today;
        var activities = _activities.GetActivities(today);
        if (activities.Count == 0 && !force)
            return new AssistantAnalysisDto { Date = today.ToString("yyyy-MM-dd"), Summary = "今天还没有活动记录", AnalyzedAt = DateTime.Now };

        // 1. AI 推测兴趣（0-3 个）
        var (provider, model) = ResolveProvider();
        var prompt = BuildAnalysisPrompt(activities);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是用户行为分析助理，根据活动记录推测用户兴趣，只输出严格 JSON。"),
            new(ChatRole.User, prompt)
        };
        var options = new ChatOptions { Temperature = 0.3f, MaxOutputTokens = 2000 };

        string raw;
        try
        {
            var resp = await _aiClient.GetChatResponseWithAutoStartAsync(
                provider, model, messages, options, ct, operation: "assistant");
            raw = resp.Text ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "助理兴趣分析失败");
            throw new InvalidOperationException($"兴趣分析失败：{ex.Message}");
        }

        var interests = ParseInterests(raw);

        // 2. 为每个兴趣生成知识库（走 vault-generation 任务）
        var generated = new List<GeneratedVaultDto>();
        foreach (var interest in interests)
        {
            var vault = await TriggerVaultGenerationAsync(interest.Topic, ct);
            generated.Add(vault);
        }

        // 3. 汇总摘要
        var summary = BuildSummary(activities, interests);
        var result = new AssistantAnalysisDto
        {
            Date = today.ToString("yyyy-MM-dd"),
            Summary = summary,
            Interests = interests,
            GeneratedVaults = generated,
            ActivityCount = activities.Count,
            AnalyzedAt = DateTime.Now,
            Model = $"{provider.Name}/{model}",
            Raw = raw
        };

        Directory.CreateDirectory(DataDir);
        await File.WriteAllTextAsync(AnalysisFile(today),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);

        _logger.LogInformation("助理每日分析完成：{Count} 个兴趣，{Vaults} 个知识库",
            interests.Count, generated.Count);
        return result;
    }

    /// <summary>读取某天分析结果</summary>
    public AssistantAnalysisDto? GetAnalysis(DateTime day)
    {
        try
        {
            var file = AnalysisFile(day);
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<AssistantAnalysisDto>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    /// <summary>最近 N 天分析记录（有结果的）</summary>
    public List<AssistantAnalysisDto> GetHistory(int days = 14)
    {
        var list = new List<AssistantAnalysisDto>();
        for (var i = days - 1; i >= 0; i--)
        {
            var a = GetAnalysis(DateTime.Today.AddDays(-i));
            if (a != null && a.Interests.Count > 0)
                list.Add(a);
        }
        return list;
    }

    private (AiProviderConfig provider, string model) ResolveProvider()
    {
        var provider = _aiSettings.GetMainAiProvider()
            ?? throw new InvalidOperationException("未配置 AI 提供商，请先在 AI 设置中配置");
        var model = _aiSettings.GetModelForProvider(provider.Id);
        return (provider, model);
    }

    private static string BuildAnalysisPrompt(List<UserActivityDto> activities)
    {
        var lines = activities
            .OrderBy(a => a.Time)
            .Select(a => $"[{a.Time:HH:mm}] {a.Type}: {a.Text}")
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("以下是用户今天在知识管理系统中的活动记录（聊天/搜索/任务等）：");
        sb.AppendLine();
        foreach (var l in lines.Take(200))
            sb.AppendLine(l);
        if (lines.Count > 200)
            sb.AppendLine($"...（共 {lines.Count} 条，已截取前 200 条）");
        sb.AppendLine();
        sb.AppendLine("请推测用户可能感兴趣的主题，要求：");
        sb.AppendLine("1. 最多 3 个主题，如果信息太少无法确定，返回空数组 []");
        sb.AppendLine("2. 主题要具体（如\"中医养生\"而非\"健康\"），避免泛泛而谈");
        sb.AppendLine("3. 只输出一个 JSON 对象（不要 markdown 代码块）：");
        sb.AppendLine("{\"interests\":[{\"topic\":\"中医\",\"confidence\":0.85,\"evidence\":\"多次搜索中医相关内容\"}]}");
        sb.AppendLine("confidence 为 0-1 的置信度，evidence 简要说明依据。");
        return sb.ToString();
    }

    private static List<InterestTopicDto> ParseInterests(string text)
    {
        var result = new List<InterestTopicDto>();
        var json = TryExtractJson(text);
        if (json == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var arr = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("interests", out var r) ? r : default;
            if (arr.ValueKind != JsonValueKind.Array) return result;

            foreach (var item in arr.EnumerateArray())
            {
                var topic = GetStr(item, "topic");
                if (string.IsNullOrWhiteSpace(topic)) continue;
                result.Add(new InterestTopicDto
                {
                    Topic = topic.Trim(),
                    Confidence = item.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0.5,
                    Evidence = GetStr(item, "evidence")
                });
            }
            return result.Take(3).ToList();
        }
        catch
        {
            return result;
        }
    }

    private async Task<GeneratedVaultDto> TriggerVaultGenerationAsync(string topic, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var body = new
            {
                industry = topic,
                keyword = topic,
                noteCount = 10,
                generateCards = false
            };
            var resp = await http.PostAsJsonAsync("http://127.0.0.1:8788/api/tasks/vault-generation", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                return new GeneratedVaultDto { Topic = topic, Status = "failed", Error = $"HTTP {(int)resp.StatusCode}: {err[..Math.Min(120, err.Length)]}" };
            }
            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            string? GetProp(params string[] names)
            {
                foreach (var n in names)
                    if (result.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString();
                return null;
            }
            var taskId = GetProp("taskId", "TaskId");
            var vaultName = GetProp("vaultName", "VaultName");
            return new GeneratedVaultDto { Topic = topic, TaskId = taskId, VaultName = vaultName };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "触发知识库生成失败: {Topic}", topic);
            return new GeneratedVaultDto { Topic = topic, Status = "failed", Error = ex.Message };
        }
    }

    private static string BuildSummary(List<UserActivityDto> activities, List<InterestTopicDto> interests)
    {
        var chat = activities.Count(a => a.Type == "chat");
        var search = activities.Count(a => a.Type == "search");
        var task = activities.Count(a => a.Type == "task");
        var other = activities.Count - chat - search - task;

        var sb = new System.Text.StringBuilder();
        sb.Append($"今日共记录 {activities.Count} 条活动");
        var parts = new List<string>();
        if (chat > 0) parts.Add($"聊天 {chat} 条");
        if (search > 0) parts.Add($"搜索 {search} 次");
        if (task > 0) parts.Add($"任务 {task} 个");
        if (other > 0) parts.Add($"其他 {other} 条");
        if (parts.Count > 0) sb.Append($"（{string.Join("、", parts)}）");

        if (interests.Count > 0)
            sb.Append($"。推测兴趣：{string.Join("、", interests.Select(i => i.Topic))}");
        else
            sb.Append("。今日信息不足以确定兴趣主题。");

        return sb.ToString();
    }

    private static string? TryExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        var fence = System.Text.RegularExpressions.Regex.Match(t, "```(?:json)?\\s*([\\s\\S]*?)```");
        if (fence.Success) t = fence.Groups[1].Value.Trim();
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        return start >= 0 && end > start ? t[start..(end + 1)] : null;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    #endregion
}
