using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace TaskRunner.Services.LocalAI;

/// <summary>
/// GGUF 本地模型推理（基于 LLamaSharp / llama.cpp）
/// </summary>
public class LlamaSharpInference : ILocalModelInference, IDisposable
{
    private readonly ILogger<LlamaSharpInference> _logger;
    private readonly ConcurrentDictionary<string, LLamaWeights> _weightsCache = new();

    public string ModelType => "gguf";

    public LlamaSharpInference(ILogger<LlamaSharpInference> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsModelAvailableAsync(string modelPath)
    {
        return Task.FromResult(File.Exists(modelPath) && Path.GetExtension(modelPath).Equals(".gguf", StringComparison.OrdinalIgnoreCase));
    }

    public async IAsyncEnumerable<string> ChatAsync(
        string modelPath,
        string message,
        string? systemPrompt = null,
        List<(string Role, string Content)>? history = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("GGUF 模型文件不存在", modelPath);

        var weights = _weightsCache.GetOrAdd(modelPath, path =>
        {
            _logger.LogInformation("正在加载 GGUF 模型: {Path}", path);
            var parameters = new ModelParams(path)
            {
                ContextSize = 2048,
                GpuLayerCount = 0, // CPU 推理
                Encoding = Encoding.UTF8
            };
            return LLamaWeights.LoadFromFile(parameters);
        });

        var contextParams = new ModelParams(modelPath)
        {
            ContextSize = 2048,
            GpuLayerCount = 0,
            Encoding = Encoding.UTF8
        };

        using var context = weights.CreateContext(contextParams);
        var executor = new InteractiveExecutor(context);
        var sampling = new DefaultSamplingPipeline
        {
            Temperature = 0.7f
        };

        var chatHistory = new ChatHistory();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            chatHistory.AddMessage(AuthorRole.System, systemPrompt);
        }

        // 加载历史对话上下文
        if (history != null)
        {
            foreach (var (role, content) in history)
            {
                var authorRole = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? AuthorRole.Assistant
                    : AuthorRole.User;
                chatHistory.AddMessage(authorRole, content);
            }
        }

        var session = new ChatSession(executor, chatHistory);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 1024,
            AntiPrompts = new List<string> { "User:", "<|im_end|>", "<|endoftext|>" },
            SamplingPipeline = sampling
        };

        var userMessage = new ChatHistory.Message(AuthorRole.User, message);
        await foreach (var text in session.ChatAsync(userMessage, inferenceParams, cancellationToken))
        {
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    public void Dispose()
    {
        foreach (var weights in _weightsCache.Values)
        {
            weights.Dispose();
        }
        _weightsCache.Clear();
    }
}
