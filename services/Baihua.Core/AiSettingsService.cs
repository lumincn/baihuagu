using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaskRunner.Models;

namespace TaskRunner.Services;

/// <summary>
/// AI 运行时配置服务：聚合 AI 提供商、模型、API Key、请求参数、Embedding 配置。
/// 作为 SettingsService 的继任者，专注 AI 域的运行时读取需求。
/// </summary>
public class AiSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiSettingsService> _logger;
    private IReadOnlyList<AiProviderConfig>? _aiProvidersCache;

    public AiSettingsService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<AiSettingsService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void ClearAiProvidersCache()
    {
        _aiProvidersCache = null;
        _logger.LogInformation("AI 提供商缓存已清除");
    }

    public IReadOnlyList<AiProviderConfig> GetAiProviders()
    {
        if (_aiProvidersCache != null)
            return _aiProvidersCache;

        try
        {
            var aiConfigService = _serviceProvider.GetService(typeof(AiConfigService)) as AiConfigService;
            var dbProviders = aiConfigService?.GetProviders();
            if (dbProviders != null && dbProviders.Count > 0)
            {
                _aiProvidersCache = dbProviders;
                return _aiProvidersCache;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从数据库加载 AI 提供商配置失败，回退到 appsettings.json");
        }

        var list = _configuration.GetSection("Ai").Get<List<AiProviderConfig>>() ?? new List<AiProviderConfig>();
        _aiProvidersCache = list;
        return _aiProvidersCache;
    }

    public AiProviderConfig? GetAiProvider(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return GetAiProviders().FirstOrDefault(p =>
            p.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public AiProviderConfig? GetMainAiProvider()
    {
        try
        {
            var aiConfigService = _serviceProvider.GetService(typeof(AiConfigService)) as AiConfigService;
            var mainFromDb = aiConfigService?.GetMainProvider();
            if (mainFromDb != null)
                return mainFromDb;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从数据库加载主 AI 提供商失败，回退到配置文件中查找");
        }

        var list = GetAiProviders();
        var main = list.FirstOrDefault(p => p.IsMain);
        if (main != null)
            return main;
        return list.FirstOrDefault();
    }

    public string GetApiKeyForProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return "";

        var idTrim = providerId.Trim();

        try
        {
            var aiConfigService = _serviceProvider.GetService(typeof(AiConfigService)) as AiConfigService;
            var keyFromDb = aiConfigService?.GetApiKey(idTrim);
            if (!string.IsNullOrEmpty(keyFromDb))
                return keyFromDb;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从数据库加载 AI 提供商 API Key 失败: {ProviderId}", idTrim);
        }

        return "";
    }

    public virtual string GetAiApiKey(string providerId)
    {
        return GetApiKeyForProvider(providerId);
    }

    public string AiApiKey => GetApiKeyForProvider(GetMainAiProvider()?.Id ?? "");

    public string AiApiUrl
    {
        get
        {
            var envUrl = Environment.GetEnvironmentVariable("TASK_RUNNER_AI_API_URL");
            if (!string.IsNullOrEmpty(envUrl))
                return envUrl;

            var main = GetMainAiProvider();
            if (main != null && !string.IsNullOrWhiteSpace(main.AiBaseUrl))
                return main.AiBaseUrl.TrimEnd('/');

            return _configuration["AiBaseUrl"]?.TrimEnd('/')
                ?? "https://coding.dashscope.aliyuncs.com/v1";
        }
    }

    public string AiModel => GetModelForProvider(GetMainAiProvider()?.Id ?? "");

    public string GetModelForProvider(string providerId, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return _configuration["AiModel"] ?? "Qwen/Qwen2.5-14B-Instruct";

        var provider = GetAiProvider(providerId);
        if (provider == null)
            return _configuration["AiModel"] ?? "Qwen/Qwen2.5-14B-Instruct";

        var models = provider.GetModelOptions();
        if (models.Count == 0)
            return _configuration["AiModel"] ?? "Qwen/Qwen2.5-14B-Instruct";

        if (!string.IsNullOrWhiteSpace(model))
        {
            var matched = models.FirstOrDefault(m =>
                m.Name.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
            if (matched != null)
                return matched.Name;
        }

        var mainModel = models.FirstOrDefault(m => m.IsMain);
        return mainModel?.Name ?? models[0].Name;
    }

    public int AiRequestTimeoutMinutes
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TASK_RUNNER_AI_REQUEST_TIMEOUT_MINUTES");
            if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var v) && v > 0)
                return v;
            var cfg = _configuration["AiRequestTimeoutMinutes"];
            if (!string.IsNullOrWhiteSpace(cfg) && int.TryParse(cfg, out var c) && c > 0)
                return c;
            return 5;
        }
    }

    public int AiRequestMaxAttempts
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TASK_RUNNER_AI_REQUEST_MAX_ATTEMPTS");
            if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var v) && v > 0)
                return v;
            var cfg = _configuration["AiRequestMaxAttempts"];
            if (!string.IsNullOrWhiteSpace(cfg) && int.TryParse(cfg, out var c) && c > 0)
                return c;
            return 3;
        }
    }

    public int AiRequestInitialBackoffMs
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TASK_RUNNER_AI_REQUEST_INITIAL_BACKOFF_MS");
            if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var v) && v > 0)
                return v;
            var cfg = _configuration["AiRequestInitialBackoffMs"];
            if (!string.IsNullOrWhiteSpace(cfg) && int.TryParse(cfg, out var c) && c > 0)
                return c;
            return 1000;
        }
    }

    public int AiRequestMaxBackoffMs
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TASK_RUNNER_AI_REQUEST_MAX_BACKOFF_MS");
            if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var v) && v > 0)
                return v;
            var cfg = _configuration["AiRequestMaxBackoffMs"];
            if (!string.IsNullOrWhiteSpace(cfg) && int.TryParse(cfg, out var c) && c > 0)
                return c;
            return 30000;
        }
    }

    public string SemanticEmbeddingUrl =>
        Environment.GetEnvironmentVariable("TASK_RUNNER_EMBEDDING_URL")
        ?? _configuration["EmbeddingUrl"]
        ?? "";

    public string SemanticEmbeddingModel =>
        Environment.GetEnvironmentVariable("TASK_RUNNER_EMBEDDING_MODEL")
        ?? _configuration["EmbeddingModel"]
        ?? "";
}
