using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;

namespace TaskRunner.Services;

/// <summary>
/// 从 Ollama Library (ollama.com/library) 抓取最新模型列表，补充静态模型库。
/// </summary>
public class OllamaLibraryClient
{
    private readonly ILogger<OllamaLibraryClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private List<ModelEntry> _cachedModels = new();
    private readonly object _lock = new();

    public OllamaLibraryClient(IHttpClientFactory httpClientFactory, ILogger<OllamaLibraryClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 获取已缓存的 Library 模型（线程安全）。
    /// </summary>
    public IReadOnlyList<ModelEntry> GetCachedModels()
    {
        lock (_lock)
        {
            return _cachedModels.ToList();
        }
    }

    /// <summary>
    /// 手动刷新 Library 模型列表。
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // 使用默认 25 秒整体超时，避免网络不畅时长时间挂起
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            _logger.LogInformation("开始从 Ollama Library 拉取模型列表...");
            var models = await FetchLibraryModelsAsync(linkedCt);
            lock (_lock)
            {
                _cachedModels = models;
            }
            _logger.LogInformation("Ollama Library 刷新完成，共 {Count} 个模型", models.Count);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("从 Ollama Library 拉取模型列表超时，保留旧缓存");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 Ollama Library 拉取模型列表失败，保留旧缓存");
        }
    }

    private async Task<List<ModelEntry>> FetchLibraryModelsAsync(CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("OllamaLibrary");
        var html = await client.GetStringAsync("https://ollama.com/library", cancellationToken);
        // 限制抓取数量，避免超时；优先抓取热门模型（页面顺序）
        var modelNames = ExtractModelNames(html).Take(50).ToList();

        var entries = new List<ModelEntry>();
        foreach (var name in modelNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = await FetchModelEntryAsync(name, cancellationToken);
                if (entry != null) entries.Add(entry);
            }
            catch (OperationCanceledException)
            {
                throw; // 向上传播取消信号
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取模型 {Name} 详情失败", name);
            }
            // 简单限流，避免请求过快被封
            await Task.Delay(80, cancellationToken);
        }
        return entries;
    }

    private static List<string> ExtractModelNames(string html)
    {
        // 提取 href="/library/llama3.1" 中的模型名
        var regex = new Regex(@"href=""/library/([^""/:#]+)""", RegexOptions.Compiled);
        return regex.Matches(html)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !n.Contains(' ') && n.Length > 1)
            .ToList();
    }

    private async Task<ModelEntry?> FetchModelEntryAsync(string name, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("OllamaLibrary");
        var html = await client.GetStringAsync($"https://ollama.com/library/{name}", cancellationToken);

        // 描述
        var descMatch = Regex.Match(html, @"<meta\s+name=""description""\s+content=""([^""]+)""");
        var description = descMatch.Success ? descMatch.Groups[1].Value.Trim() : "";

        // 默认 latest tag 的详细信息（页面底部通常显示 latest 的元数据）
        var paramMatch = Regex.Match(
            html,
            @"parameters.*?(?:</span>|\s*[:=]\s*)([0-9,.]+\s*[KMGT]B)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var paramSize = paramMatch.Success
            ? paramMatch.Groups[1].Value.Replace(",", "").Trim()
            : InferParamSizeFromName(name);

        var quantMatch = Regex.Match(
            html,
            @"quantization.*?(?:</span>|\s*[:=]\s*)(Q[0-9][A-Z0-9_]*|F(?:16|32))",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var quantization = quantMatch.Success ? quantMatch.Groups[1].Value.ToUpperInvariant() : "Q4_K_M";

        var sizeMatch = Regex.Match(
            html,
            @"·\s*([0-9,.]+)\s*GB\s*·",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var sizeGiB = sizeMatch.Success
            ? double.Parse(sizeMatch.Groups[1].Value.Replace(",", ""))
            : EstimateSizeGiB(paramSize);

        var (minRam, minVram) = EstimateHardwareRequirements(sizeGiB);

        return new ModelEntry
        {
            Id = $"ollama-{name.Replace('.', '-').Replace('_', '-').ToLowerInvariant()}",
            Name = CapitalizeName(name),
            OllamaModelName = $"{name}:latest",
            Description = string.IsNullOrEmpty(description) ? $"Ollama Library 官方模型: {name}" : description,
            ParameterSize = paramSize,
            Quantization = quantization,
            SizeGiB = Math.Round(sizeGiB, 1),
            MinRamGiB = minRam,
            MinVramGiB = minVram,
            Tags = new() { "chat" },
        };
    }

    private static string InferParamSizeFromName(string name)
    {
        var match = Regex.Match(name, @"([0-9.]+)[Bb]");
        return match.Success ? match.Groups[1].Value + "B" : "unknown";
    }

    private static double EstimateSizeGiB(string paramSize)
    {
        var match = Regex.Match(paramSize, @"([0-9.]+)\s*([KMGT])?B", RegexOptions.IgnoreCase);
        if (!match.Success) return 5;
        var val = double.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value.ToUpperInvariant();
        // Q4_K_M 量化下约 0.6 GB / 1B 参数
        return unit switch
        {
            "K" => val / 1024 / 1024 * 0.6,
            "M" => val / 1024 * 0.6,
            _ => val * 0.6,
        };
    }

    private static (double MinRam, double? MinVram) EstimateHardwareRequirements(double sizeGiB)
    {
        var minRam = Math.Max(1.5, Math.Round(sizeGiB * 1.2, 1));
        double? minVram = sizeGiB > 3 ? Math.Round(sizeGiB * 1.1, 1) : null;
        return (minRam, minVram);
    }

    private static string CapitalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
