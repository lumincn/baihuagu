using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Contracts.Master;
using System.Reflection;
using System.Text.Json;

namespace TaskRunner.Services;

public class MasterPromptBuilder
{
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
        return "先生";
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
        return "抱歉，我无法提供真实的医疗诊断、处方或法律建议。我是学习辅助师父，只能帮助您学习考证知识。如有真实需求，请咨询持证专业人士。";
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

        sb.AppendLine($"# 师父角色设定");
        sb.AppendLine($"你是「{masterName}」，一位{industry}行业的{persona.RoleName}。");
        sb.AppendLine($"你的教学风格：{persona.Style}。");
        sb.AppendLine();
        sb.AppendLine(persona.Prompt);
        sb.AppendLine();
        sb.AppendLine($"# 学徒目标");
        sb.AppendLine($"学徒的目标是：{goal}");
        sb.AppendLine($"当前阶段：{currentStage}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(coreProfile))
        {
            sb.AppendLine($"# 学徒核心画像");
            sb.AppendLine(coreProfile);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(stageSummary))
        {
            sb.AppendLine($"# 当前阶段学习摘要");
            sb.AppendLine(stageSummary);
            sb.AppendLine();
        }

        sb.AppendLine($"# 教学原则");
        sb.AppendLine($"1. 因材施教：根据学徒基础调整教学节奏");
        sb.AppendLine($"2. 循序渐进：不跳过基础，不急于求成");
        sb.AppendLine($"3. 实战导向：所有知识都要联系实际应用");
        sb.AppendLine($"4. 及时反馈：指出错误并给予改进建议");
        sb.AppendLine($"5. 鼓励为主：肯定进步，激发学习动力");
        sb.AppendLine();
        sb.AppendLine($"# 安全边界");
        sb.AppendLine($"你只能辅助学习考证知识，不能提供真实的医疗诊断、处方、法律建议。遇到此类请求，必须明确拒绝并建议咨询持证专业人士。");

        return sb.ToString();
    }

    public static List<StageInfo> GetDefaultStages()
    {
        return
        [
            new() { Name = "入道", DisplayName = "入道", Description = "确定目标、评估基础、生成初始知识库和任务", Order = 1 },
            new() { Name = "筑基", DisplayName = "筑基", Description = "建立知识框架、每日任务、养成学习习惯", Order = 2 },
            new() { Name = "精进", DisplayName = "精进", Description = "分科细化、攻克细节、消除薄弱环节", Order = 3 },
            new() { Name = "磨砺", DisplayName = "磨砺", Description = "模拟考试、查漏补缺、强化高频考点", Order = 4 },
            new() { Name = "出师", DisplayName = "出师", Description = "能力认证、报考指导、考前冲刺", Order = 5 },
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
        var prefix = "TaskRunner.Data.ExamOutlines.";

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
