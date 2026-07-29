namespace TaskRunner.Contracts.Health;

public class ComponentStatusDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // healthy, warning, critical
    public string? Version { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>该项检测耗时（毫秒），由 Task Runner 在并行检测中各自计时。</summary>
    public long CheckDurationMs { get; set; }
}

