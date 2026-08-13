namespace Baihua.Data.Entities;

/// <summary>
/// 编程 Agent 会话记录（轻量历史：prompt/参数/输出，刷新不丢）
/// </summary>
public class CodeAgentSession
{
    public int Id { get; set; }

    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>用户需求</summary>
    public string Prompt { get; set; } = "";

    /// <summary>语言/技术栈</summary>
    public string? Language { get; set; }

    /// <summary>提供商 ID</summary>
    public string? ProviderId { get; set; }

    /// <summary>模型 ID</summary>
    public string? Model { get; set; }

    /// <summary>工具集模式（All/Search/CodeGraph/None）</summary>
    public string ToolMode { get; set; } = "All";

    /// <summary>是否流水线模式</summary>
    public bool IsPipeline { get; set; }

    /// <summary>流水线规划阶段是否用 Pro 模型</summary>
    public bool PlanPro { get; set; }

    /// <summary>单 Agent 输出</summary>
    public string? Output { get; set; }

    /// <summary>流水线调研输出</summary>
    public string? Research { get; set; }

    /// <summary>流水线代码输出</summary>
    public string? Code { get; set; }

    /// <summary>流水线审查输出</summary>
    public string? Review { get; set; }

    /// <summary>识别出的文件名</summary>
    public string? FileName { get; set; }
}
