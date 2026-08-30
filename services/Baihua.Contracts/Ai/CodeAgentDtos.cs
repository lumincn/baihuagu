namespace Baihua.Contracts.Ai;

/// <summary>
/// 编程 Agent 请求（Microsoft Agent Framework + 本地/远程编程模型）。
/// </summary>
public class CodeAgentRequest
{
    /// <summary>用户需求描述（必填）</summary>
    public string Prompt { get; set; } = "";

    /// <summary>AI 提供方 ID（默认取主提供方）</summary>
    public string? ProviderId { get; set; }

    /// <summary>模型名（默认取提供方主模型）</summary>
    public string? Model { get; set; }

    /// <summary>目标语言/技术栈提示（如 "C# console"、"Python"），可为空</summary>
    public string? Language { get; set; }

    /// <summary>是否流式返回（默认 true；false 时一次性返回完整代码）</summary>
    public bool? Stream { get; set; }

    /// <summary>额外上下文（如已有代码片段、约束条件），可为空</summary>
    public string? Context { get; set; }

    /// <summary>历史记录 Id（继续对话时传）：恢复会话上下文并把本次生成更新到该记录</summary>
    public int? SessionId { get; set; }

    /// <summary>工具集模式（默认全部工具）</summary>
    public CodeAgentToolMode ToolMode { get; set; } = CodeAgentToolMode.All;
}

/// <summary>
/// 编程 Agent 流水线请求：多阶段执行（调研 → 编码 → 审查），每个阶段用独立 Agent。
/// </summary>
public class CodeAgentPipelineRequest : CodeAgentRequest
{
    /// <summary>跳过调研阶段（默认 false）</summary>
    public bool SkipResearch { get; set; }

    /// <summary>跳过审查阶段（默认 false）</summary>
    public bool SkipReview { get; set; }

    /// <summary>规划（调研）阶段模型；为空则用主模型（默认建议 deepseek-v4-pro）</summary>
    public string? PlanModel { get; set; }

    /// <summary>审查阶段模型；为空则用主模型</summary>
    public string? ReviewModel { get; set; }
}

/// <summary>
/// 编程 Agent 历史记录（列表项，不含大文本）
/// </summary>
public class CodeAgentSessionSummaryDto
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Prompt { get; set; } = "";
    public string? Language { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public string ToolMode { get; set; } = "All";
    public bool IsPipeline { get; set; }
    public bool PlanPro { get; set; }
    public string? FileName { get; set; }

    /// <summary>输出总长度（摘要排序用）</summary>
    public int OutputLength { get; set; }
}

/// <summary>
/// 编程 Agent 历史详情（含各阶段输出）
/// </summary>
public class CodeAgentSessionDetailDto : CodeAgentSessionSummaryDto
{
    public string? Output { get; set; }
    public string? Research { get; set; }
    public string? Code { get; set; }
    public string? Review { get; set; }
}

/// <summary>
/// 保存编程 Agent 会话记录请求
/// </summary>
public class CodeAgentSessionSaveRequest
{
    public string Prompt { get; set; } = "";
    public string? Language { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public string? ToolMode { get; set; }
    public bool IsPipeline { get; set; }
    public bool PlanPro { get; set; }
    public string? Output { get; set; }
    public string? Research { get; set; }
    public string? Code { get; set; }
    public string? Review { get; set; }
    public string? FileName { get; set; }

    /// <summary>MAF 会话状态序列化（续聊用）</summary>
    public string? SessionStateJson { get; set; }
}

/// <summary>
/// 编程 Agent 流水线响应（非流式）。
/// </summary>
public class CodeAgentPipelineResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>调研阶段输出</summary>
    public string? Research { get; set; }

    /// <summary>编码阶段输出的代码（已提取，无 ``` 包裹）</summary>
    public string? Code { get; set; }

    /// <summary>编码阶段提取的文件名</summary>
    public string? FileName { get; set; }

    /// <summary>审查阶段输出</summary>
    public string? Review { get; set; }

    public string? ProviderId { get; set; }
    public string? Model { get; set; }
}

/// <summary>
/// 编程 Agent 工具集模式：控制挂载哪些工具（工具定义会占用上下文，纯代码生成场景建议 None）。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CodeAgentToolMode
{
    /// <summary>全部工具：联网搜索 + 网页精读 + 代码图谱</summary>
    All,

    /// <summary>仅联网：tavily_search + web_fetch</summary>
    Search,

    /// <summary>仅代码图谱：gitnexus_query / context / impact</summary>
    CodeGraph,

    /// <summary>无工具：纯代码生成，上下文最小、最稳定</summary>
    None
}

/// <summary>
/// 编程 Agent 响应（非流式模式）。
/// </summary>
public class CodeAgentResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }

    /// <summary>从代码中提取的文件名（如 "Program.cs"），可能为空</summary>
    public string? FileName { get; set; }

    /// <summary>实际使用的提供方与模型</summary>
    public string? ProviderId { get; set; }
    public string? Model { get; set; }

    /// <summary>会话记录 Id（继续对话时回传）</summary>
    public int? SessionId { get; set; }

    /// <summary>MAF 会话状态序列化（前端保存历史时存回，用于续聊）</summary>
    public string? SessionStateJson { get; set; }
}
