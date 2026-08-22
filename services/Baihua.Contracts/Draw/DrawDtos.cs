namespace Baihua.Contracts.Draw;

/// <summary>文生图请求（本地 ComfyUI，SD 系列 checkpoint）。</summary>
public class DrawImageRequest
{
    /// <summary>正向提示词（英文效果更佳）。</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>负向提示词，可空。</summary>
    public string? NegativePrompt { get; set; }

    /// <summary>宽度，默认 512（建议 256-1024）。</summary>
    public int? Width { get; set; }

    /// <summary>高度，默认 512（建议 256-1024）。</summary>
    public int? Height { get; set; }

    /// <summary>采样步数，默认 20。</summary>
    public int? Steps { get; set; }

    /// <summary>checkpoint 文件名，可空（默认 SD1.5）。</summary>
    public string? Checkpoint { get; set; }
}

/// <summary>文生视频请求（本地 ComfyUI + LTX Video）。</summary>
public class DrawVideoRequest
{
    /// <summary>正向提示词（英文效果更佳）。</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>负向提示词，可空。</summary>
    public string? NegativePrompt { get; set; }

    /// <summary>宽度，默认 512（LTX 建议 ≤768）。</summary>
    public int? Width { get; set; }

    /// <summary>高度，默认 512（LTX 建议 ≤768）。</summary>
    public int? Height { get; set; }

    /// <summary>视频帧数，默认 97（≈4s @24fps；建议 25-121）。</summary>
    public int? Length { get; set; }

    /// <summary>帧率，默认 25。</summary>
    public int? Fps { get; set; }

    /// <summary>采样步数，默认 20（视频建议 15-30）。</summary>
    public int? Steps { get; set; }

    /// <summary>checkpoint 文件名，可空（默认 LTX Video）。</summary>
    public string? Checkpoint { get; set; }
}

/// <summary>生成结果（同步等待完成）。</summary>
public class DrawResultDto
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>生成的文件名（output 目录下），用 GET /api/draw/file 下载。</summary>
    public string? FileName { get; set; }

    /// <summary>MIME 类型（image/png、video/mp4 等）。</summary>
    public string? ContentType { get; set; }

    /// <summary>失败原因（Success 为 false 时）。</summary>
    public string? Error { get; set; }

    /// <summary>耗时（秒）。</summary>
    public double ElapsedSeconds { get; set; }
}

/// <summary>绘图能力状态。</summary>
public class DrawStatusDto
{
    /// <summary>本机 ComfyUI 是否在线。</summary>
    public bool ComfyUiOnline { get; set; }

    /// <summary>ComfyUI 版本（在线时）。</summary>
    public string? ComfyUiVersion { get; set; }

    /// <summary>可用 checkpoint（出图）。</summary>
    public List<string> ImageCheckpoints { get; set; } = new();

    /// <summary>可用 checkpoint（出视频）。</summary>
    public List<string> VideoCheckpoints { get; set; } = new();
}
