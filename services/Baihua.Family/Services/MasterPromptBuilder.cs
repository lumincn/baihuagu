using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.Ai;
using Baihua.Contracts.Master;
using Baihua.Core.Localization;
using System.Reflection;
using System.Text.Json;

namespace Baihua.Family.Services;

public class MasterPromptBuilder
{
    private readonly IStringLocalizer<SharedResources> _loc;

    public MasterPromptBuilder(IStringLocalizer<SharedResources> loc)
    {
        _loc = loc;
    }

    private static readonly Dictionary<string, string> IndustryMasterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["中医"] = "岐伯",
        ["医学"] = "岐伯",
        ["计算机"] = "图灵",
        ["IT"] = "图灵",
        ["会计"] = "算圣",
        ["财务"] = "算圣",
        ["教资"] = "夫子",
        ["教育"] = "夫子",
        ["法律"] = "廷尉",
        ["建筑"] = "鲁班",
    };

    private static readonly Dictionary<string, StagePersona> StagePersonas = new()
    {
        ["入道"] = new("引路人", "温和、好奇、善问", "你是一位温和的引路人。你的任务是了解学徒的基础、目标和动机。通过提问引导学徒自我认知，而非直接灌输。用好奇的语气探索学徒的背景，帮助他们明确学习方向。"),
        ["筑基"] = new("严师", "有耐心但要求严格", "你是一位严格的师父。你要求学徒扎实掌握基础知识，不容许敷衍了事。每日布置功课并检查完成情况。对错误耐心纠正，但要求必须改对。强调'基础不牢，地动山摇'。"),
        ["精进"] = new("匠人", "极其耐心、绝不放过细节错误", "你是一位精益求精的匠人师父。你对细节有近乎偏执的追求，任何微小错误都要指出并纠正。你会反复追问直到学徒完全理解。强调'差之毫厘，谬以千里'。"),
        ["磨砺"] = new("考官", "模拟真实考试环境", "你是一位严肃的考官。你模拟真实考试环境，出题考察学徒的综合能力。时间限制严格，评分标准明确。考后详细分析错题，指出知识盲区。强调'实战出真知'。"),
        ["出师"] = new("前辈", "实战建议、考试经验", "你是一位经验丰富的前辈。你分享实战经验和考试技巧，帮助学徒做好最后的冲刺准备。提供报考指导、考前心理建设、应试策略。强调'临阵磨枪，不快也光'。"),
    };

    private static readonly string[] SafetyBlockedKeywords =
    [
        "真实诊断", "开处方", "开药方", "真实法律建议", "代理诉讼",
        "医疗诊断", "处方药", "手术方案", "法律代理"
    ];

    public string ResolveMasterName(string industry)
    {
        foreach (var (key, name) in IndustryMasterNames)
        {
            if (industry.Contains(key, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return _loc["Prompt_DefaultMasterName"];
    }

    public List<ChatMessage> BuildMessages(
        string goal,
        string industry,
        string masterName,
        string currentStage,
        string? coreProfile,
        string? stageSummary,
        List<ChatHistoryItem>? recentHistory,
        string userMessage)
    {
        var messages = new List<ChatMessage>();

        var systemPrompt = BuildSystemPrompt(goal, industry, masterName, currentStage, coreProfile, stageSummary);
        messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        if (recentHistory != null)
        {
            foreach (var item in recentHistory.TakeLast(20))
            {
                var role = item.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
                messages.Add(new ChatMessage(role, item.Content));
            }
        }

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        return messages;
    }

    public bool ContainsBlockedContent(string input)
    {
        return SafetyBlockedKeywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public string BuildSafetyRefusal()
    {
        return _loc["Prompt_SafetyRefusal"];
    }

    public string BuildSystemPrompt(
        string goal,
        string industry,
        string masterName,
        string currentStage,
        string? coreProfile,
        string? stageSummary)
    {
        var persona = StagePersonas.GetValueOrDefault(currentStage)
            ?? StagePersonas["入道"];

        var sb = new System.Text.StringBuilder();

        sb.AppendLine(_loc["Prompt_SectionMasterRole"]);
        sb.AppendLine(string.Format(_loc["Prompt_YouAreTemplate"], masterName, industry, persona.RoleName));
        sb.AppendLine(string.Format(_loc["Prompt_StyleTemplate"], persona.Style));
        sb.AppendLine();
        sb.AppendLine(persona.Prompt);
        sb.AppendLine();
        sb.AppendLine(_loc["Prompt_SectionApprenticeGoal"]);
        sb.AppendLine(string.Format(_loc["Prompt_GoalTemplate"], goal));
        sb.AppendLine(string.Format(_loc["Prompt_CurrentStageTemplate"], currentStage));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(coreProfile))
        {
            sb.AppendLine(_loc["Prompt_SectionCoreProfile"]);
            sb.AppendLine(coreProfile);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(stageSummary))
        {
            sb.AppendLine(_loc["Prompt_SectionStageSummary"]);
            sb.AppendLine(stageSummary);
            sb.AppendLine();
        }

        sb.AppendLine(_loc["Prompt_SectionTeachingPrinciples"]);
        sb.AppendLine(_loc["Prompt_Principle1"]);
        sb.AppendLine(_loc["Prompt_Principle2"]);
        sb.AppendLine(_loc["Prompt_Principle3"]);
        sb.AppendLine(_loc["Prompt_Principle4"]);
        sb.AppendLine(_loc["Prompt_Principle5"]);
        sb.AppendLine();
        sb.AppendLine(_loc["Prompt_SectionSafety"]);
        sb.AppendLine(_loc["Prompt_SafetyBoundary"]);

        return sb.ToString();
    }

    public List<StageInfo> GetDefaultStages()
    {
        return
        [
            new() { Name = "入道", DisplayName = _loc["Prompt_StageNameRuDao"], Description = _loc["Prompt_StageDescRuDao"], Order = 1 },
            new() { Name = "筑基", DisplayName = _loc["Prompt_StageNameZhuJi"], Description = _loc["Prompt_StageDescZhuJi"], Order = 2 },
            new() { Name = "精进", DisplayName = _loc["Prompt_StageNameJingJin"], Description = _loc["Prompt_StageDescJingJin"], Order = 3 },
            new() { Name = "磨砺", DisplayName = _loc["Prompt_StageNameMoLi"], Description = _loc["Prompt_StageDescMoLi"], Order = 4 },
            new() { Name = "出师", DisplayName = _loc["Prompt_StageNameChuShi"], Description = _loc["Prompt_StageDescChuShi"], Order = 5 },
        ];
    }

    public ExamOutline? MatchExamOutline(string goal, string industry)
    {
        var outlines = LoadAllOutlines();
        if (outlines.Count == 0) return null;

        var text = $"{goal} {industry}".ToLowerInvariant();

        foreach (var outline in outlines)
        {
            if (outline.Keywords == null) continue;
            foreach (var kw in outline.Keywords)
            {
                if (!string.IsNullOrEmpty(kw) && text.Contains(kw.ToLowerInvariant()))
                    return outline;
            }
        }

        return null;
    }

    public List<StageInfo> GetStagesForOutline(ExamOutline? outline)
    {
        if (outline?.Stages == null || outline.Stages.Count == 0)
            return GetDefaultStages();

        return outline.Stages.Select((s, i) => new StageInfo
        {
            Name = s.Name,
            DisplayName = s.Name,
            Description = s.Description ?? "",
            Order = i + 1
        }).ToList();
    }

    public string? GetOutlineContext(ExamOutline? outline, string currentStage)
    {
        if (outline == null) return null;

        var stageInfo = outline.Stages?.FirstOrDefault(s => s.Name == currentStage);
        if (stageInfo == null) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 考试大纲：{outline.Name}");

        if (stageInfo.KeyPoints != null && stageInfo.KeyPoints.Count > 0)
        {
            sb.AppendLine($"## 当前阶段考点");
            foreach (var kp in stageInfo.KeyPoints)
                sb.AppendLine($"- {kp}");
        }

        if (!string.IsNullOrEmpty(stageInfo.TransitionCriteria))
            sb.AppendLine($"## 阶段转换标准：{stageInfo.TransitionCriteria}");

        if (stageInfo.Milestones != null && stageInfo.Milestones.Count > 0)
        {
            sb.AppendLine($"## 里程碑");
            foreach (var m in stageInfo.Milestones)
                sb.AppendLine($"- {m}");
        }

        return sb.ToString();
    }

    private static List<ExamOutline> LoadAllOutlines()
    {
        var result = new List<ExamOutline>();
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Baihua.Data.ExamOutlines.";

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(".json")) continue;
            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var outline = JsonSerializer.Deserialize<ExamOutline>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (outline != null) result.Add(outline);
            }
            catch { }
        }

        return result;
    }

    private record StagePersona(string RoleName, string Style, string Prompt);
}

public class ExamOutline
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string>? Keywords { get; set; }
    public List<ExamStageOutline>? Stages { get; set; }
}

public class ExamStageOutline
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string>? KeyPoints { get; set; }
    public List<string>? Milestones { get; set; }
    public string? TransitionCriteria { get; set; }
}
