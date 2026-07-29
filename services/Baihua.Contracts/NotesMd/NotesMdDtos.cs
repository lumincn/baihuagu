namespace TaskRunner.Contracts.NotesMd;

public class NotesMdCliStatus
{
    public bool Available { get; set; }
    public List<string> RegisteredPaths { get; set; } = new();
}

public class NotesMdBatchResult
{
    public bool Success { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Succeeded { get; set; } = new();
    public List<string> Failed { get; set; } = new();
}
