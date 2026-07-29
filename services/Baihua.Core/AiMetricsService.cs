using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Services;

/// <summary>
/// AI 与 Benchmark 指标服务：基于 System.Diagnostics.Metrics，
/// 通过 OpenTelemetry OTLP 导出到 OpenObserve 等可观测平台。
/// </summary>
public class AiMetricsService : IDisposable
{
    public static readonly string MeterName = "TaskRunner.AI";

    private readonly Meter _meter;

    // AI 通用调用指标
    private readonly Histogram<double> _aiLatency;
    private readonly Histogram<double> _aiTps;
    private readonly Counter<long> _aiRequests;
    private readonly Counter<long> _aiTokens;

    // Benchmark 专属指标
    private readonly Histogram<double> _benchLatency;
    private readonly Histogram<double> _benchTps;
    private readonly Histogram<int> _benchQuality;
    private readonly Counter<long> _benchTimeouts;
    private readonly Counter<long> _benchErrors;
    private readonly Counter<long> _benchRuns;

    // 健康检查指标
    private readonly Histogram<double> _healthCheckDuration;
    private readonly Histogram<double> _healthWallClock;
    private readonly Histogram<int> _healthScore;
    private readonly Counter<long> _healthComponents;

    public AiMetricsService()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _aiLatency = _meter.CreateHistogram<double>("ai.latency_ms", unit: "ms", description: "AI 请求延迟");
        _aiTps = _meter.CreateHistogram<double>("ai.tokens_per_second", unit: "tokens/s", description: "AI Token 生成速率");
        _aiRequests = _meter.CreateCounter<long>("ai.requests.total", unit: "{request}", description: "AI 请求总次数");
        _aiTokens = _meter.CreateCounter<long>("ai.tokens.total", unit: "{token}", description: "AI Token 处理总量");

        _benchLatency = _meter.CreateHistogram<double>("benchmark.latency_ms", unit: "ms", description: "Benchmark 单条提示词延迟");
        _benchTps = _meter.CreateHistogram<double>("benchmark.tokens_per_second", unit: "tokens/s", description: "Benchmark Token 生成速率");
        _benchQuality = _meter.CreateHistogram<int>("benchmark.quality_score", unit: "1", description: "Benchmark 质量评分");
        _benchTimeouts = _meter.CreateCounter<long>("benchmark.timeouts.total", unit: "{timeout}", description: "Benchmark 超时次数");
        _benchErrors = _meter.CreateCounter<long>("benchmark.errors.total", unit: "{error}", description: "Benchmark 错误次数");
        _benchRuns = _meter.CreateCounter<long>("benchmark.runs.total", unit: "{run}", description: "Benchmark 提示词运行次数");

        _healthCheckDuration = _meter.CreateHistogram<double>("health.check.duration_ms", unit: "ms", description: "单个健康检查组件耗时");
        _healthWallClock = _meter.CreateHistogram<double>("health.check.wallclock_ms", unit: "ms", description: "健康检查整体耗时");
        _healthScore = _meter.CreateHistogram<int>("health.score", unit: "1", description: "健康检查评分");
        _healthComponents = _meter.CreateCounter<long>("health.components.total", unit: "{component}", description: "健康检查组件状态统计");
    }

    /// <summary>
    /// 记录通用 AI 调用指标
    /// </summary>
    public void RecordAiRequest(
        string provider, string model, string operation,
        double latencyMs, bool success,
        long? inputTokens = null, long? outputTokens = null, double? tokensPerSecond = null)
    {
        var tags = new TagList();
        tags.Add("provider", provider);
        tags.Add("model", model);
        tags.Add("operation", operation);
        tags.Add("success", success);

        _aiRequests.Add(1, tags);
        _aiLatency.Record(latencyMs, tags);

        if (inputTokens.HasValue && outputTokens.HasValue)
        {
            _aiTokens.Add(inputTokens.Value + outputTokens.Value, tags);
        }

        if (tokensPerSecond.HasValue)
        {
            _aiTps.Record(tokensPerSecond.Value, tags);
        }
    }

    /// <summary>
    /// 记录 Benchmark 单条提示词指标
    /// </summary>
    public void RecordBenchmark(
        string provider, string model, string category, string promptId,
        double latencyMs, int qualityScore, bool isTimeout, bool isError,
        long? outputTokens = null, double? tokensPerSecond = null, bool isEstimatedTokens = false)
    {
        var tags = new TagList();
        tags.Add("provider", provider);
        tags.Add("model", model);
        tags.Add("category", category);
        tags.Add("prompt_id", promptId);
        tags.Add("is_estimated", isEstimatedTokens);

        _benchRuns.Add(1, tags);
        _benchLatency.Record(latencyMs, tags);
        _benchQuality.Record(qualityScore, tags);

        if (tokensPerSecond.HasValue)
        {
            _benchTps.Record(tokensPerSecond.Value, tags);
        }

        if (isTimeout)
        {
            _benchTimeouts.Add(1, tags);
        }

        if (isError)
        {
            _benchErrors.Add(1, tags);
        }
    }

    /// <summary>
    /// 记录健康检查结果
    /// </summary>
    public void RecordHealthCheck(
        string componentName, string status,
        double durationMs, double? wallClockMs = null, int? score = null)
    {
        var tags = new TagList();
        tags.Add("component", componentName);
        tags.Add("status", status);

        _healthCheckDuration.Record(durationMs, tags);
        _healthComponents.Add(1, tags);

        if (wallClockMs.HasValue)
        {
            _healthWallClock.Record(wallClockMs.Value, new TagList());
        }

        if (score.HasValue)
        {
            _healthScore.Record(score.Value, new TagList());
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
