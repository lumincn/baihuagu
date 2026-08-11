namespace Baihua.Contracts.LocalModels;

/// <summary>OpenVINO 可下载模型目录条目（页面用）</summary>
public class OpenVinoCatalogItemDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParameterSize { get; set; } = "";
    public string Quantization { get; set; } = "";
    public double SizeGiB { get; set; }
    public string? Description { get; set; }
    public bool IsVision { get; set; }
    public string ModelScopeRepo { get; set; } = "";
    public bool Installed { get; set; }
}

/// <summary>OpenVINO 可下载模型目录条目（网上仓库：ModelScope / HuggingFace）</summary>
public class OpenVinoCatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParameterSize { get; set; } = "";
    public string Quantization { get; set; } = "INT4";
    public double SizeGiB { get; set; }
    public string? Description { get; set; }
    public bool IsVision { get; set; }
    public string ModelScopeRepo { get; set; } = "";
    public string HuggingFaceRepo { get; set; } = "";
    /// <summary>下载到本地模型根目录后的子目录名</summary>
    public string DirectoryName { get; set; } = "";
}

/// <summary>OpenVINO 模型下载请求</summary>
public class OpenVinoDownloadRequest
{
    public string ModelId { get; set; } = "";
}

/// <summary>下载任务状态</summary>
public class OpenVinoDownloadTaskDto
{
    public string TaskId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending/running/completed/failed/cancelled
    public int ProgressPercent { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string CurrentFile { get; set; } = "";
    public double SpeedMBps { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> Logs { get; set; } = new();
}

/// <summary>已下载的 OpenVINO 模型（扫描模型根目录）</summary>
public class OpenVinoInstalledModelDto
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool HasOpenVinoBin { get; set; }
    public bool IsRunning { get; set; }
    public int? Port { get; set; }
    public DateTime? LastModified { get; set; }
}

/// <summary>运行 OpenVINO 模型请求</summary>
public class OpenVinoRunRequest
{
    public string ModelPath { get; set; } = "";
    public string Device { get; set; } = "GPU";

    /// <summary>停止时用：模型监听端口</summary>
    public int? Port { get; set; }
}

/// <summary>运行结果</summary>
public class OpenVinoRunResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int Port { get; set; }
    public int? ProcessId { get; set; }
    public string Endpoint { get; set; } = "";
}
