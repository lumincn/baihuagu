namespace Baihua.Contracts.Tasks;

public class TasksResponse
{
    public List<TaskInfo> Tasks { get; set; } = new();
}

public class TaskInfo
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public int Status { get; set; }

    public string StatusText => Status switch
    {
        0 => "Pending",
        1 => "Running",
        2 => "Success",
        3 => "Failed",
        4 => "Timeout",
        5 => "Cancelled",
        _ => "Unknown"
    };

    public TaskProgress Progress { get; set; } = new();
    public TaskResult? Result { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TaskProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string Message { get; set; } = "";
    public double Percentage { get; set; }
}

public class TaskResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}

public class AiTaskResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? TaskId { get; set; }
}

public class VaultGenerationRequest
{
    public string Industry { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int NoteCount { get; set; } = 30;
    public bool GenerateCards { get; set; }
}

public class VaultGenerationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? TaskId { get; set; }
}

public class AiTaskRequest
{
    public string Query { get; set; } = string.Empty;
    public bool SaveToVault { get; set; } = true;
    public string? VaultId { get; set; }
    public string? VaultPath { get; set; }
    public string? Model { get; set; }
    public bool AutoSplit { get; set; }
    public string? SystemPrompt { get; set; }
    public string? Industry { get; set; }
}

public class RetryAiTaskRequest
{
    public int TimeoutMinutes { get; set; }
    public string? Model { get; set; }
}

public class TaskHistoryItem
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string ProgressMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TaskHistoryResponse
{
    public bool Success { get; set; }
    public List<TaskHistoryItem> Tasks { get; set; } = new();
    public int Total { get; set; }
}

public class CleanupRequest
{
    public int OlderThanDays { get; set; }
}

public class CleanupResponse
{
    public bool Success { get; set; }
    public int DeletedCount { get; set; }
}

public class TaskStatsResponse
{
    public bool Success { get; set; }
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Running { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
}
