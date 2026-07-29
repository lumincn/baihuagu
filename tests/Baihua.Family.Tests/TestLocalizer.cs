using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;
using Moq;

namespace Baihua.Family.Tests;

public static class TestLocalizer
{
    private static readonly Lazy<IStringLocalizer<SharedResources>> _instance =
        new Lazy<IStringLocalizer<SharedResources>>(CreateLocalizer);

    public static IStringLocalizer<SharedResources> Instance => _instance.Value;

    public static IStringLocalizer<SharedResources> Create() => Instance;

    private static IStringLocalizer<SharedResources> CreateLocalizer()
    {
        var mock = new Mock<IStringLocalizer<SharedResources>>();

        mock.Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, GetValue(key), true));
        mock.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns<string, object[]>((key, args) => new LocalizedString(key, string.Format(GetValue(key), args), true));

        return mock.Object;
    }

    private static string GetValue(string key) => key switch
    {
        // === Anki Card Generator ===
        "Anki_DeckPrefixJingFang" => "经方::{0}",
        "Anki_TagJingFang" => "经方",
        "Anki_NotesDirNotConfigured" => "笔记目录未配置",
        "Anki_NoteNotFound" => "笔记不存在：{0}",
        "Anki_AiNoCards" => "AI 未从笔记生成卡片: {0}",
        "Anki_AiSuccess" => "AI 成功生成 {0} 张卡片: {1}",
        "Anki_AiFailed" => "AI 生成失败: {0}",
        "Anki_DirNotFound" => "目录不存在：{0}",
        "Anki_PreparingNotes" => "准备处理 {0} 篇笔记...",
        "Anki_BatchComplete" => "AI 批量生成完成: {0} 张卡片，来自 {1} 篇笔记",

        // === MasterPromptBuilder - Safety ===
        "Prompt_SafetyRefusal" => "抱歉，我无法提供真实的医疗诊断、处方或法律建议。我是学习辅助师父，只能帮助您学习考证知识。如有真实需求，请咨询持证专业人士。",
        "Prompt_DefaultMasterName" => "先生",

        // === MasterPromptBuilder - System Prompt Sections ===
        "Prompt_SectionMasterRole" => "# 师父角色设定",
        "Prompt_YouAreTemplate" => "你是「{0}」，一位{1}行业的{2}。",
        "Prompt_StyleTemplate" => "你的教学风格：{0}。",
        "Prompt_SectionApprenticeGoal" => "# 学徒目标",
        "Prompt_GoalTemplate" => "学徒的目标是：{0}",
        "Prompt_CurrentStageTemplate" => "当前阶段：{0}",
        "Prompt_SectionCoreProfile" => "# 学徒核心画像",
        "Prompt_SectionStageSummary" => "# 当前阶段学习摘要",
        "Prompt_SectionTeachingPrinciples" => "# 教学原则",
        "Prompt_Principle1" => "1. 因材施教：根据学徒基础调整教学节奏",
        "Prompt_Principle2" => "2. 循序渐进：不跳过基础，不急于求成",
        "Prompt_Principle3" => "3. 实战导向：所有知识都要联系实际应用",
        "Prompt_Principle4" => "4. 及时反馈：指出错误并给予改进建议",
        "Prompt_Principle5" => "5. 鼓励为主：肯定进步，激发学习动力",
        "Prompt_SectionSafety" => "# 安全边界",
        "Prompt_SafetyBoundary" => "你只能辅助学习考证知识，不能提供真实的医疗诊断、处方、法律建议。遇到此类请求，必须明确拒绝并建议咨询持证专业人士。",

        // === MasterPromptBuilder - Stage Names ===
        "Prompt_StageNameRuDao" => "入道",
        "Prompt_StageNameZhuJi" => "筑基",
        "Prompt_StageNameJingJin" => "精进",
        "Prompt_StageNameMoLi" => "磨砺",
        "Prompt_StageNameChuShi" => "出师",

        // === MasterPromptBuilder - Stage Descriptions ===
        "Prompt_StageDescRuDao" => "确定目标、评估基础、生成初始知识库和任务",
        "Prompt_StageDescZhuJi" => "建立知识框架、每日任务、养成学习习惯",
        "Prompt_StageDescJingJin" => "分科细化、攻克细节、消除薄弱环节",
        "Prompt_StageDescMoLi" => "模拟考试、查漏补缺、强化高频考点",
        "Prompt_StageDescChuShi" => "能力认证、报考指导、考前冲刺",

        // === MasterPromptBuilder - Stage Personas ===
        "MasterPrompt_RuDao_Role" => "引路人",
        "MasterPrompt_RuDao_Style" => "温和、好奇、善问",
        "MasterPrompt_RuDao_Prompt" => "你是一位温和的引路人。你的任务是了解学徒的基础、目标和动机。通过提问引导学徒自我认知，而非直接灌输。用好奇的语气探索学徒的背景，帮助他们明确学习方向。",
        "MasterPrompt_ZhuJi_Role" => "严师",
        "MasterPrompt_ZhuJi_Style" => "有耐心但要求严格",
        "MasterPrompt_ZhuJi_Prompt" => "你是一位严格的师父。你要求学徒扎实掌握基础知识，不容许敷衍了事。每日布置功课并检查完成情况。对错误耐心纠正，但要求必须改对。强调'基础不牢，地动山摇'。",
        "MasterPrompt_JingJin_Role" => "匠人",
        "MasterPrompt_JingJin_Style" => "极其耐心、绝不放过细节错误",
        "MasterPrompt_JingJin_Prompt" => "你是一位精益求精的匠人师父。你对细节有近乎偏执的追求，任何微小错误都要指出并纠正。你会反复追问直到学徒完全理解。强调'差之毫厘，谬以千里'。",
        "MasterPrompt_MoLi_Role" => "考官",
        "MasterPrompt_MoLi_Style" => "模拟真实考试环境",
        "MasterPrompt_MoLi_Prompt" => "你是一位严肃的考官。你模拟真实考试环境，出题考察学徒的综合能力。时间限制严格，评分标准明确。考后详细分析错题，指出知识盲区。强调'实战出真知'。",
        "MasterPrompt_ChuShi_Role" => "前辈",
        "MasterPrompt_ChuShi_Style" => "实战建议、考试经验",
        "MasterPrompt_ChuShi_Prompt" => "你是一位经验丰富的前辈。你分享实战经验和考试技巧，帮助学徒做好最后的冲刺准备。提供报考指导、考前心理建设、应试策略。强调'临阵磨枪，不快也光'。",

        // === MasterPromptBuilder - Master Names ===
        "MasterPrompt_MasterQiBo" => "岐伯",
        "MasterPrompt_MasterTuLing" => "图灵",
        "MasterPrompt_MasterSuanSheng" => "算圣",
        "MasterPrompt_MasterFuZi" => "夫子",
        "MasterPrompt_MasterTingWei" => "廷尉",
        "MasterPrompt_MasterLuBan" => "鲁班",

        // === MasterPromptBuilder - Outline ===
        "MasterPrompt_OutlineHeader" => "# 考试大纲：{0}",
        "MasterPrompt_StageKeyPoints" => "## 当前阶段考点",
        "MasterPrompt_TransitionCriteria" => "## 阶段转换标准：{0}",
        "MasterPrompt_Milestones" => "## 里程碑",

        // === AchievementEngine ===
        "Achievement_FirstStep_Title" => "第一步",
        "Achievement_FirstStep_Desc" => "完成首次卡片学习",
        "Achievement_Streak3_Title" => "三日不断",
        "Achievement_Streak3_Desc" => "连续学习 3 天",
        "Achievement_Streak7_Title" => "周周坚持",
        "Achievement_Streak7_Desc" => "连续学习 7 天",
        "Achievement_Streak30_Title" => "月月不辍",
        "Achievement_Streak30_Desc" => "连续学习 30 天",
        "Achievement_Cards10_Title" => "十题小试",
        "Achievement_Cards10_Desc" => "累计学习 10 张卡片",
        "Achievement_Cards50_Title" => "半百精进",
        "Achievement_Cards50_Desc" => "累计学习 50 张卡片",
        "Achievement_Cards100_Title" => "百题大关",
        "Achievement_Cards100_Desc" => "累计学习 100 张卡片",
        "Achievement_Cards500_Title" => "学富五车",
        "Achievement_Cards500_Desc" => "累计学习 500 张卡片",
        "Achievement_Creator1_Title" => "初出茅庐",
        "Achievement_Creator1_Desc" => "首次家长出题",
        "Achievement_Creator10_Title" => "出题能手",
        "Achievement_Creator10_Desc" => "累计出题 10 道",
        "Achievement_Explorer1_Title" => "初识岐黄",
        "Achievement_Explorer1_Desc" => "首次使用 AI 对话",
        "Achievement_Explorer10_Title" => "问道十次",
        "Achievement_Explorer10_Desc" => "累计使用 AI 对话 10 次",
        "Achievement_Accuracy80_Title" => "百发百中",
        "Achievement_Accuracy80_Desc" => "单日正确率达到 80%",
        "Achievement_EarlyBird_Title" => "闻鸡起舞",
        "Achievement_EarlyBird_Desc" => "早上 6 点前完成学习",

        // === DataEncryptionService ===
        "Security_EncryptFailed" => "加密失败",
        "Security_DecryptFailed" => "解密失败",

        // === AiMetricsService (used by AnkiCardGenerator via AiClientService) ===
        "AiMetrics_AiLatency" => "AI 请求延迟",
        "AiMetrics_AiTps" => "AI Token 生成速率",
        "AiMetrics_AiRequests" => "AI 请求总次数",
        "AiMetrics_AiTokens" => "AI Token 处理总量",

        // Default fallback - return the key itself
        _ => key
    };
}
