namespace TaskRunner.Contracts.LocalModels;

/// <summary>
/// 本地可部署模型数据库。静态数据，随版本发布更新。
/// 主要覆盖 Ollama Library 中常见的中文友好和通用模型。
/// </summary>
public static class ModelDatabase
{
    /// <summary>
    /// 所有内置模型条目
    /// </summary>
    public static IReadOnlyList<ModelEntry> AllModels => _models;

    private static readonly List<ModelEntry> _models = new()
    {
        // ========== 小模型 / 边缘设备 / CPU 友好 ==========
        new()
        {
            Id = "qwen2.5-0.5b",
            Name = "Qwen 2.5 0.5B",
            OllamaModelName = "qwen2.5:0.5b",
            LmStudioSearchName = "qwen2.5-0.5b-instruct",
            Description = "阿里云通义千问超轻量版，适合极低资源环境和快速响应场景",
            ParameterSize = "0.5B",
            Quantization = "Q4_K_M",
            SizeGiB = 0.4,
            MinVramGiB = null, // CPU only is fine
            MinRamGiB = 1.0,
            Tags = new() { "chat", "chinese", "lightweight", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "qwen2.5-1.5b",
            Name = "Qwen 2.5 1.5B",
            OllamaModelName = "qwen2.5:1.5b",
            LmStudioSearchName = "qwen2.5-1.5b-instruct",
            Description = "阿里云通义千问轻量版，中文能力强，适合低资源设备",
            ParameterSize = "1.5B",
            Quantization = "Q4_K_M",
            SizeGiB = 1.0,
            MinVramGiB = null,
            MinRamGiB = 2.0,
            Tags = new() { "chat", "chinese", "lightweight", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "llama3.2-1b",
            Name = "Llama 3.2 1B",
            OllamaModelName = "llama3.2:1b",
            LmStudioSearchName = "llama-3.2-1b-instruct",
            Description = "Meta 最新轻量模型，多语言支持好，适合边缘设备",
            ParameterSize = "1B",
            Quantization = "Q4_K_M",
            SizeGiB = 0.8,
            MinVramGiB = null,
            MinRamGiB = 1.5,
            Tags = new() { "chat", "lightweight" },
            Company = "Meta",
        },
        new()
        {
            Id = "llama3.2-3b",
            Name = "Llama 3.2 3B",
            OllamaModelName = "llama3.2:3b",
            LmStudioSearchName = "llama-3.2-3b-instruct",
            Description = "Meta 最新小模型，性能优于前代 8B，适合一般对话",
            ParameterSize = "3B",
            Quantization = "Q4_K_M",
            SizeGiB = 2.0,
            MinVramGiB = 3.0,
            MinRamGiB = 4.0,
            Tags = new() { "chat", "lightweight" },
            Company = "Meta",
        },
        new()
        {
            Id = "gemma2-2b",
            Name = "Gemma 2 2B",
            OllamaModelName = "gemma2:2b",
            LmStudioSearchName = "gemma-2-2b-it",
            Description = "Google 轻量模型，2B 参数达到上一代 7B 水平",
            ParameterSize = "2B",
            Quantization = "Q4_K_M",
            SizeGiB = 1.6,
            MinVramGiB = 2.5,
            MinRamGiB = 3.0,
            Tags = new() { "chat", "code", "lightweight" },
            Company = "Google",
        },
        new()
        {
            Id = "phi3-3.8b",
            Name = "Phi 3 Mini 3.8B",
            OllamaModelName = "phi3:3.8b",
            LmStudioSearchName = "phi-3-mini-4k-instruct",
            Description = "Microsoft 小模型，质量优异，适合日常对话和轻量任务",
            ParameterSize = "3.8B",
            Quantization = "Q4_K_M",
            SizeGiB = 2.3,
            MinVramGiB = 3.5,
            MinRamGiB = 4.0,
            Tags = new() { "chat", "code", "lightweight" },
            Company = "Microsoft",
        },

        // ========== 7B-8B 级别（主流消费级显卡） ==========
        new()
        {
            Id = "qwen2.5-7b",
            Name = "Qwen 2.5 7B",
            OllamaModelName = "qwen2.5:7b",
            LmStudioSearchName = "qwen2.5-7b-instruct",
            Description = "阿里云通义千问主力模型，中文能力顶尖，推荐首选",
            ParameterSize = "7B",
            Quantization = "Q4_K_M",
            SizeGiB = 4.7,
            MinVramGiB = 6.0,
            MinRamGiB = 8.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "qwen2.5-7b-q8",
            Name = "Qwen 2.5 7B Q8",
            OllamaModelName = "qwen2.5:7b-q8_0",
            LmStudioSearchName = "qwen2.5-7b-instruct-q8_0",
            Description = "Qwen 2.5 7B 高精度量化版，质量更好，需要更多显存",
            ParameterSize = "7B",
            Quantization = "Q8_0",
            SizeGiB = 8.1,
            MinVramGiB = 10.0,
            MinRamGiB = 12.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "llama3.1-8b",
            Name = "Llama 3.1 8B",
            OllamaModelName = "llama3.1:8b",
            LmStudioSearchName = "llama-3.1-8b-instruct",
            Description = "Meta 开源主力模型，128K 上下文，多语言支持优秀",
            ParameterSize = "8B",
            Quantization = "Q4_K_M",
            SizeGiB = 4.9,
            MinVramGiB = 6.5,
            MinRamGiB = 8.0,
            Tags = new() { "chat", "code" },
            Company = "Meta",
        },
        new()
        {
            Id = "gemma2-9b",
            Name = "Gemma 2 9B",
            OllamaModelName = "gemma2:9b",
            LmStudioSearchName = "gemma-2-9b-it",
            Description = "Google 中端模型，9B 参数达到 20B+ 水平",
            ParameterSize = "9B",
            Quantization = "Q4_K_M",
            SizeGiB = 5.5,
            MinVramGiB = 7.0,
            MinRamGiB = 10.0,
            Tags = new() { "chat", "code" },
            Company = "Google",
        },
        new()
        {
            Id = "deepseek-r1-7b",
            Name = "DeepSeek-R1 7B",
            OllamaModelName = "deepseek-r1:7b",
            LmStudioSearchName = "deepseek-r1-distill-qwen-7b",
            Description = "DeepSeek 推理模型蒸馏版，适合数学推理和逻辑思考",
            ParameterSize = "7B",
            Quantization = "Q4_K_M",
            SizeGiB = 4.7,
            MinVramGiB = 6.0,
            MinRamGiB = 8.0,
            Tags = new() { "reasoning", "code", "chinese", "tcm" },
            Company = "DeepSeek",
        },

        // ========== 14B 级别（中高端显卡） ==========
        new()
        {
            Id = "qwen2.5-14b",
            Name = "Qwen 2.5 14B",
            OllamaModelName = "qwen2.5:14b",
            LmStudioSearchName = "qwen2.5-14b-instruct",
            Description = "Qwen 2.5 大杯，中文综合能力接近 GPT-3.5",
            ParameterSize = "14B",
            Quantization = "Q4_K_M",
            SizeGiB = 9.0,
            MinVramGiB = 11.0,
            MinRamGiB = 14.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "qwen2.5-14b-q8",
            Name = "Qwen 2.5 14B Q8",
            OllamaModelName = "qwen2.5:14b-q8_0",
            LmStudioSearchName = "qwen2.5-14b-instruct-q8_0",
            Description = "Qwen 2.5 14B 高精度量化版",
            ParameterSize = "14B",
            Quantization = "Q8_0",
            SizeGiB = 15.5,
            MinVramGiB = 18.0,
            MinRamGiB = 20.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "deepseek-r1-14b",
            Name = "DeepSeek-R1 14B",
            OllamaModelName = "deepseek-r1:14b",
            LmStudioSearchName = "deepseek-r1-distill-qwen-14b",
            Description = "DeepSeek 推理模型，14B 蒸馏版，推理能力出色",
            ParameterSize = "14B",
            Quantization = "Q4_K_M",
            SizeGiB = 9.0,
            MinVramGiB = 11.0,
            MinRamGiB = 14.0,
            Tags = new() { "reasoning", "code", "chinese", "tcm" },
            Company = "DeepSeek",
        },
        new()
        {
            Id = "phi4-14b",
            Name = "Phi 4 14B",
            OllamaModelName = "phi4:14b",
            LmStudioSearchName = "phi-4-mini-instruct",
            Description = "Microsoft 最新模型，14B 参数达到 70B 级别性能",
            ParameterSize = "14B",
            Quantization = "Q4_K_M",
            SizeGiB = 9.1,
            MinVramGiB = 11.0,
            MinRamGiB = 14.0,
            Tags = new() { "chat", "code", "reasoning" },
            Company = "Microsoft",
        },

        // ========== 32B 级别（高端显卡） ==========
        new()
        {
            Id = "qwen2.5-32b",
            Name = "Qwen 2.5 32B",
            OllamaModelName = "qwen2.5:32b",
            LmStudioSearchName = "qwen2.5-32b-instruct",
            Description = "Qwen 2.5 超大杯，中文能力接近 GPT-4",
            ParameterSize = "32B",
            Quantization = "Q4_K_M",
            SizeGiB = 19.3,
            MinVramGiB = 22.0,
            MinRamGiB = 26.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "deepseek-r1-32b",
            Name = "DeepSeek-R1 32B",
            OllamaModelName = "deepseek-r1:32b",
            LmStudioSearchName = "deepseek-r1-distill-qwen-32b",
            Description = "DeepSeek 推理模型 32B 版，推理能力接近 GPT-4",
            ParameterSize = "32B",
            Quantization = "Q4_K_M",
            SizeGiB = 19.3,
            MinVramGiB = 22.0,
            MinRamGiB = 26.0,
            Tags = new() { "reasoning", "code", "chinese", "tcm" },
            Company = "DeepSeek",
        },
        new()
        {
            Id = "llama3.3-70b-q4",
            Name = "Llama 3.3 70B (Q4)",
            OllamaModelName = "llama3.3:70b",
            LmStudioSearchName = "llama-3.3-70b-instruct",
            Description = "Meta 最新 70B 模型 Q4 量化版，需要大显存",
            ParameterSize = "70B",
            Quantization = "Q4_K_M",
            SizeGiB = 42.5,
            MinVramGiB = 45.0,
            MinRamGiB = 50.0,
            Tags = new() { "chat", "code" },
            Company = "Meta",
        },

        // ========== 70B+ 级别（顶级显卡/多卡） ==========
        new()
        {
            Id = "qwen2.5-72b",
            Name = "Qwen 2.5 72B",
            OllamaModelName = "qwen2.5:72b",
            LmStudioSearchName = "qwen2.5-72b-instruct",
            Description = "Qwen 2.5 旗舰模型，综合能力最强",
            ParameterSize = "72B",
            Quantization = "Q4_K_M",
            SizeGiB = 43.3,
            MinVramGiB = 48.0,
            MinRamGiB = 56.0,
            Tags = new() { "chat", "chinese", "code", "tcm" },
            Company = "阿里云",
        },
        new()
        {
            Id = "deepseek-r1-70b",
            Name = "DeepSeek-R1 70B",
            OllamaModelName = "deepseek-r1:70b",
            LmStudioSearchName = "deepseek-r1-distill-llama-70b",
            Description = "DeepSeek 推理模型 70B 版，顶级推理能力",
            ParameterSize = "70B",
            Quantization = "Q4_K_M",
            SizeGiB = 43.3,
            MinVramGiB = 48.0,
            MinRamGiB = 56.0,
            Tags = new() { "reasoning", "code", "chinese", "tcm" },
            Company = "DeepSeek",
        },
    };

    /// <summary>
    /// 按标签筛选模型
    /// </summary>
    public static IEnumerable<ModelEntry> GetByTag(string tag)
    {
        return _models.Where(m => m.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按 ID 查找模型
    /// </summary>
    public static ModelEntry? GetById(string id)
    {
        return _models.FirstOrDefault(m =>
            m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按 Ollama 模型名查找
    /// </summary>
    public static ModelEntry? GetByOllamaName(string ollamaName)
    {
        return _models.FirstOrDefault(m =>
            m.OllamaModelName.Equals(ollamaName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按 LM Studio 搜索名称查找
    /// </summary>
    public static ModelEntry? GetByLmStudioName(string lmStudioName)
    {
        return _models.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.LmStudioSearchName) &&
            m.LmStudioSearchName.Equals(lmStudioName, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 模型数据库条目（内部使用）
/// </summary>
public class ModelEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string OllamaModelName { get; set; } = "";
    public string? LmStudioSearchName { get; set; }
    public string? HuggingFaceRepo { get; set; }
    public string? GgufFilename { get; set; }
    public string Description { get; set; } = "";
    public string ParameterSize { get; set; } = "";
    public string Quantization { get; set; } = "";
    public double SizeGiB { get; set; }
    public double? MinVramGiB { get; set; }
    public double MinRamGiB { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Company { get; set; } = "";
}
