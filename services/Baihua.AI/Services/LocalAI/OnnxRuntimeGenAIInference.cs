using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace TaskRunner.Services.LocalAI;

/// <summary>
/// ONNX 本地模型推理（基于 Microsoft.ML.OnnxRuntimeGenAI）
/// 针对 Phi-3 / Phi-3.5 系列模型优化
/// </summary>
public class OnnxRuntimeGenAIInference : ILocalModelInference, IDisposable
{
    private readonly ILogger<OnnxRuntimeGenAIInference> _logger;
    private readonly ConcurrentDictionary<string, CachedOnnxModel> _modelCache = new();

    public string ModelType => "onnx";

    public OnnxRuntimeGenAIInference(ILogger<OnnxRuntimeGenAIInference> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsModelAvailableAsync(string modelPath)
    {
        // ONNX 模型是一个目录，里面包含 model.onnx / genai_config.json / tokenizer.json 等
        var isDir = Directory.Exists(modelPath);
        var hasConfig = isDir && File.Exists(Path.Combine(modelPath, "genai_config.json"));
        return Task.FromResult(hasConfig);
    }

    public async IAsyncEnumerable<string> ChatAsync(
        string modelPath,
        string message,
        string? systemPrompt = null,
        List<(string Role, string Content)>? history = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"ONNX 模型目录不存在: {modelPath}");

        var cached = _modelCache.GetOrAdd(modelPath, path =>
        {
            _logger.LogInformation("正在加载 ONNX 模型: {Path}", path);
            var model = new Model(path);
            var tokenizer = new Tokenizer(model);
            return new CachedOnnxModel(model, tokenizer);
        });

        var fullPrompt = BuildPhi3Prompt(systemPrompt, message, history);

        var sequences = cached.Tokenizer.Encode(fullPrompt);

        using var generatorParams = new GeneratorParams(cached.Model);
        generatorParams.SetSearchOption("max_length", 2048);
        generatorParams.SetSearchOption("temperature", 0.7);

        using var tokenizerStream = cached.Tokenizer.CreateStream();
        using var generator = new Generator(cached.Model, generatorParams);
        generator.AppendTokenSequences(sequences);

        var sb = new StringBuilder();
        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();

            generator.GenerateNextToken();
            var tokenId = generator.GetSequence(0)[^1];
            var text = tokenizerStream.Decode(tokenId);

            sb.Append(text);

            // Phi-3 停止词检测
            if (text.Contains("<|end|>") || text.Contains("<|user|>") || text.Contains("<|assistant|>"))
                break;

            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }

            // 让出线程，避免阻塞
            await Task.Yield();
        }
    }

    /// <summary>
    /// 构建 Phi-3 / Phi-3.5 的对话 prompt（支持多轮历史）
    /// </summary>
    private static string BuildPhi3Prompt(string? systemPrompt, string userMessage, List<(string Role, string Content)>? history = null)
    {
        var sp = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are a helpful AI assistant."
            : systemPrompt;

        var sb = new StringBuilder();
        sb.Append($"<|system|>\n{sp}<|end|>\n");

        if (history != null)
        {
            foreach (var (role, content) in history)
            {
                var tag = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
                sb.Append($"<|{tag}|>\n{content}<|end|>\n");
            }
        }

        sb.Append($"<|user|>\n{userMessage}<|end|>\n<|assistant|>\n");
        return sb.ToString();
    }

    public void Dispose()
    {
        foreach (var entry in _modelCache.Values)
        {
            entry.Dispose();
        }
        _modelCache.Clear();
    }

    private class CachedOnnxModel : IDisposable
    {
        public Model Model { get; }
        public Tokenizer Tokenizer { get; }

        public CachedOnnxModel(Model model, Tokenizer tokenizer)
        {
            Model = model;
            Tokenizer = tokenizer;
        }

        public void Dispose()
        {
            Tokenizer.Dispose();
            Model.Dispose();
        }
    }
}
