using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Todo;
using Baihua.Family.Services.Todo;

namespace Baihua.Family.Controllers;

/// <summary>
/// 个人待办清单 API（单用户、极简：标题 + 完成状态）。
/// </summary>
[ApiController]
[Route("api/todos")]
public class TodoController : ControllerBase
{
    private readonly TodoService _todoService;

    public TodoController(TodoService todoService)
    {
        _todoService = todoService;
    }

    /// <summary>获取全部待办（按创建顺序）</summary>
    [HttpGet]
    public async Task<ActionResult<List<TodoItemDto>>> GetAll(CancellationToken ct)
    {
        var items = await _todoService.GetAllAsync(ct);
        return Ok(items.Select(ToDto).ToList());
    }

    /// <summary>创建待办</summary>
    [HttpPost]
    public async Task<ActionResult<TodoItemDto>> Create([FromBody] CreateTodoRequest request, CancellationToken ct)
    {
        var title = request?.Title?.Trim() ?? "";
        if (title.Length == 0)
            return BadRequest(new { error = "待办内容不能为空" });
        if (title.Length > 200)
            return BadRequest(new { error = "待办内容过长（最多 200 字）" });

        var item = await _todoService.CreateAsync(title, ct);
        return Ok(ToDto(item!));
    }

    /// <summary>更新待办（标题 / 完成状态）</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TodoItemDto>> Update(int id, [FromBody] UpdateTodoRequest request, CancellationToken ct)
    {
        if (request == null || (request.Title == null && request.IsDone == null))
            return BadRequest(new { error = "至少需要提供标题或完成状态之一" });

        string? title = null;
        if (request.Title != null)
        {
            title = request.Title.Trim();
            if (title.Length == 0)
                return BadRequest(new { error = "待办内容不能为空" });
            if (title.Length > 200)
                return BadRequest(new { error = "待办内容过长（最多 200 字）" });
        }

        var item = await _todoService.UpdateAsync(id, title, request.IsDone, ct);
        if (item == null)
            return NotFound(new { error = "待办不存在" });

        return Ok(ToDto(item));
    }

    /// <summary>删除待办</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _todoService.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound(new { error = "待办不存在" });
    }

    private static TodoItemDto ToDto(Baihua.Data.Entities.TodoItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        IsDone = item.IsDone,
        CreatedAt = item.CreatedAt,
        CompletedAt = item.CompletedAt
    };
}
