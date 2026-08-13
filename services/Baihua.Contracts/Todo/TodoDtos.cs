namespace Baihua.Contracts.Todo;

/// <summary>
/// 个人待办事项（单用户、极简：标题 + 完成状态）。
/// </summary>
public class TodoItemDto
{
    public int Id { get; set; }

    /// <summary>待办标题</summary>
    public string Title { get; set; } = "";

    /// <summary>是否已完成</summary>
    public bool IsDone { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>完成时间（未完成时为 null）</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>创建待办请求</summary>
public class CreateTodoRequest
{
    /// <summary>待办标题（必填，去空格后最长 200 字符）</summary>
    public string Title { get; set; } = "";
}

/// <summary>更新待办请求（标题与完成状态均可选，至少传一项）</summary>
public class UpdateTodoRequest
{
    public string? Title { get; set; }

    public bool? IsDone { get; set; }
}
