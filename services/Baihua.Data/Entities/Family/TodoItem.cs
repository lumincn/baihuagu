using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Baihua.Data.Entities;

/// <summary>
/// 个人待办事项：单用户、极简（标题 + 完成状态）。
/// </summary>
public class TodoItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>待办标题</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>是否已完成</summary>
    public bool IsDone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>完成时间（未完成时为 null）</summary>
    public DateTime? CompletedAt { get; set; }
}
