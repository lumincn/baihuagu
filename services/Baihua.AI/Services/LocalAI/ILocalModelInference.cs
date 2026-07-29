namespace TaskRunner.Services.LocalAI;

/// <summary>
/// 本地模型推理接口（GGUF / ONNX 等格式）
/// </summary>
public interface ILocalModelInference
{
    /// <summary>该推理后端支持的模型格式标识，如 "gguf"、"onnx"</summary>
    string ModelType { get; }

    /// <summary>检查模型文件是否可用</summary>
    Task<bool> IsModelAvailableAsync(string modelPath);

    /// <summary>流式对话</summary>
    IAsyncEnumerable<string> ChatAsync(
        string modelPath,
        string message,
        string? systemPrompt = null,
        List<(string Role, string Content)>? history = null,
        CancellationToken cancellationToken = default);
}
