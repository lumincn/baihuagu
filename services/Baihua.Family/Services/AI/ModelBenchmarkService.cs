using System.Diagnostics;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Benchmark;
using TaskRunner.Models;

namespace TaskRunner.Services;

/// <summary>
/// 模型基准测试服务：执行标准化测试、评分、状态管理
/// </summary>
public class ModelBenchmarkService
{
    private readonly AiClientService _aiClient;
    private readonly AiSettingsService _aiSettings;
    private readonly LocalModelDeploymentService _localDeployment;
    private readonly BenchmarkRepository _repo;
    private readonly AiMetricsService _metrics;
    private readonly ILogger<ModelBenchmarkService> _logger;
    private readonly object _statusLock = new();
    private BenchmarkStatusDto _status = new();
    private CancellationTokenSource? _runCts;

    public ModelBenchmarkService(
        AiClientService aiClient,
        AiSettingsService aiSettings,
        LocalModelDeploymentService localDeployment,
        BenchmarkRepository repo,
        AiMetricsService metrics,
        ILogger<ModelBenchmarkService> logger)
    {
        _aiClient = aiClient;
        _aiSettings = aiSettings;
        _localDeployment = localDeployment;
        _repo = repo;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前运行状态
    /// </summary>
    public BenchmarkStatusDto GetStatus()
    {
        lock (_statusLock)
        {
            return new BenchmarkStatusDto
            {
                Status = _status.Status,
                CurrentPromptTitle = _status.CurrentPromptTitle,
                CompletedCount = _status.CompletedCount,
                TotalCount = _status.TotalCount,
                Error = _status.Error,
                Result = _status.Result
            };
        }
    }

    /// <summary>
    /// 执行基准测试
    /// </summary>
    public async Task<BenchmarkSession> RunBenchmarkAsync(
        BenchmarkModelConfig model,
        string[]? promptIds,
        CancellationToken cancellationToken = default)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_statusLock)
        {
            _status = new BenchmarkStatusDto
            {
                Status = "running",
                CompletedCount = 0,
                TotalCount = 0,
                Error = null,
                Result = null
            };
        }

        var session = new BenchmarkSession
        {
            ModelName = model.DisplayName,
            Category = model.Category,
            ProviderId = model.ProviderId,
            ModelId = model.ModelId
        };

        try
        {
            var provider = _aiSettings.GetAiProvider(model.ProviderId)
                ?? throw new Exception($"未找到 AI 提供商：{model.ProviderId}");

            var allPrompts = BenchmarkPrompts.GetPromptsByCategory(model.Category);
            var prompts = promptIds?.Length > 0
                ? allPrompts.Where(p => promptIds.Contains(p.Id)).ToList()
                : allPrompts;

            if (prompts.Count == 0)
                throw new Exception("没有可运行的测试提示词");

            lock (_statusLock) _status.TotalCount = prompts.Count;

            foreach (var prompt in prompts)
            {
                _runCts.Token.ThrowIfCancellationRequested();

                lock (_statusLock) _status.CurrentPromptTitle = prompt.Title;

                var result = await RunSinglePromptAsync(provider, model.ModelId, prompt, _runCts.Token);
                session.Results.Add(result);

                lock (_statusLock) _status.CompletedCount++;
            }

            session.TestedAt = DateTime.Now;
            await _repo.SaveSessionAsync(session);

            lock (_statusLock)
            {
                _status.Status = "completed";
                _status.Result = session;
                _status.CurrentPromptTitle = null;
            }

            return session;
        }
        catch (OperationCanceledException)
        {
            lock (_statusLock)
            {
                _status.Status = "failed";
                _status.Error = "测试已取消";
            }

            // 取消后卸载本地模型释放资源
            try
            {
                var provider = _aiSettings.GetAiProvider(model.ProviderId);
                if (provider != null && CanUnloadLocally(provider))
                {
                    _logger.LogInformation("Benchmark 取消后卸载本地模型: {Provider} {Model}", provider.Id, model.ModelId);
                    await _localDeployment.UnloadModelAsync(provider.Id, model.ModelId);
                }
            }
            catch (Exception unloadEx)
            {
                _logger.LogDebug(unloadEx, "Benchmark 取消后卸载模型失败");
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基准测试失败");
            lock (_statusLock)
            {
                _status.Status = "failed";
                _status.Error = ex.Message;
            }
            throw;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    /// <summary>
    /// 停止当前运行的基准测试
    /// </summary>
    public void StopBenchmark()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止基准测试失败");
        }
    }

    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(60);

    private async Task<BenchmarkPromptResult> RunSinglePromptAsync(
        AiProviderConfig provider,
        string modelId,
        BenchmarkPrompt prompt,
        CancellationToken ct)
    {
        var result = new BenchmarkPromptResult
        {
            PromptId = prompt.Id,
            PromptTitle = prompt.Title
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(PromptTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, prompt.Prompt)
            };

            var options = AiClientService.BuildChatOptions(
                temperature: 0.3f,
                maxOutputTokens: prompt.MaxTokens,
                topP: 0.9f);

            var response = await _aiClient.GetChatResponseWithAutoStartAsync(
                provider, modelId, messages, options, linkedCts.Token, "benchmark");
            sw.Stop();

            var text = response.Text ?? "";
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.OutputChars = text.Length;
            result.ResponseText = text;

            // 优先从响应中获取真实 Token 数（OpenAI 兼容端点通常返回 usage）
            int? actualOutputTokens = response.Usage?.OutputTokenCount is long ot ? (int)ot : null;
            int? actualInputTokens = response.Usage?.InputTokenCount is long it ? (int)it : null;
            bool isEstimated = !actualOutputTokens.HasValue;

            // 无真实 Token 时 fallback 估算：中文字符约1.5 tokens，英文约0.25 tokens，混合取平均约0.7 tokens/char
            int effectiveOutputTokens = actualOutputTokens ?? (int)(text.Length * 0.7);
            result.TokensPerSecond = effectiveOutputTokens / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

            // 质量评分：关键词匹配
            EvaluateQuality(result, prompt);

            // 记录 Benchmark Metrics（OpenTelemetry -> OpenObserve）
            _metrics.RecordBenchmark(
                provider.Id, modelId, prompt.Category, prompt.Id,
                result.LatencyMs, result.QualityScore,
                isTimeout: false, isError: false,
                outputTokens: effectiveOutputTokens,
                tokensPerSecond: result.TokensPerSecond,
                isEstimatedTokens: isEstimated);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _logger.LogWarning("提示词 {PromptId} 测试超时", prompt.Id);
            result.Error = "超时";
            result.IsTimeout = true;
            result.LatencyMs = sw.ElapsedMilliseconds > 0 ? sw.ElapsedMilliseconds : -1;

            // 超时后尝试卸载本地模型，释放 GPU 显存（统一支持 Ollama / LM Studio / llama.cpp）
            if (CanUnloadLocally(provider))
            {
                _logger.LogWarning("Benchmark prompt 超时，尝试卸载本地模型释放显存: {Provider} {Model}", provider.Id, modelId);
                _ = Task.Run(async () =>
                {
                    try { await _localDeployment.UnloadModelAsync(provider.Id, modelId); }
                    catch (Exception unloadEx) { _logger.LogDebug(unloadEx, "卸载模型失败: {Provider} {Model}", provider.Id, modelId); }
                });
            }

            // 如果已获取部分响应，仍然尝试评估质量
            if (!string.IsNullOrEmpty(result.ResponseText))
            {
                EvaluateQuality(result, prompt);
            }
            else
            {
                result.QualityScore = 0;
            }

            // 记录超时指标
            _metrics.RecordBenchmark(
                provider.Id, modelId, prompt.Category, prompt.Id,
                result.LatencyMs, result.QualityScore,
                isTimeout: true, isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提示词 {PromptId} 测试失败", prompt.Id);
            result.Error = ex.Message;
            result.LatencyMs = -1;
            result.QualityScore = 0;

            // 记录错误指标
            _metrics.RecordBenchmark(
                provider.Id, modelId, prompt.Category, prompt.Id,
                result.LatencyMs, result.QualityScore,
                isTimeout: false, isError: true);
        }

        return result;
    }

    private static void EvaluateQuality(BenchmarkPromptResult result, BenchmarkPrompt prompt)
    {
        if (prompt.ExpectedKeywords.Length == 0)
        {
            result.QualityScore = 100;
            return;
        }

        var text = result.ResponseText;
        if (string.IsNullOrEmpty(text))
        {
            result.QualityScore = 0;
            return;
        }

        var matched = new List<string>();
        var missing = new List<string>();

        foreach (var kw in prompt.ExpectedKeywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                matched.Add(kw);
            else
                missing.Add(kw);
        }

        result.MatchedKeywords = matched.ToArray();
        result.MissingKeywords = missing.ToArray();
        result.QualityScore = (int)((double)matched.Count / prompt.ExpectedKeywords.Length * 100);
    }

    /// <summary>
    /// 判断该 provider 是否为可本地卸载的本地服务（Ollama / LM Studio / llama.cpp 且地址在本地）
    /// </summary>
    private static bool CanUnloadLocally(AiProviderConfig provider)
    {
        if (provider == null) return false;
        var isKnownLocalTool = provider.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            || provider.Id.Equals("lmstudio", StringComparison.OrdinalIgnoreCase)
            || provider.Id.Equals("llamacpp", StringComparison.OrdinalIgnoreCase);
        var url = provider.AiBaseUrl?.ToLowerInvariant() ?? "";
        var isLocalhost = url.Contains("localhost") || url.Contains("127.0.0.1") || url.Contains("0.0.0.0");
        return isKnownLocalTool && isLocalhost;
    }
}
