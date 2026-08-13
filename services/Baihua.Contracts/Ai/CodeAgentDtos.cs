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

    /// <summary>工具集模式（默认全部工具）</summary>
    public CodeAgentToolMode ToolMode { get; set; } = CodeAgentToolMode.All;
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
}
