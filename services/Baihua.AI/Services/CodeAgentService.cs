using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Models;
using Microsoft.EntityFrameworkCore;
using Baihua.Family.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;

namespace Baihua.AI.Services;

/// <summary>
/// 编程 Agent 服务：基于 Microsoft Agent Framework (MAF) 封装。
/// 用任意 OpenAI 兼容提供方（含本地 OpenVINO 编程模型）执行代码生成任务。
/// </summary>
public class CodeAgentService
{
    /// <summary>CodeAgent 观测：trace 源（OpenObserve 里按此过滤 agent 调用链）</summary>
    private static readonly ActivitySource CodeAgentActivity = new("Baihua.AI.CodeAgent");

    private readonly AiSettingsService _aiSettings;
    private readonly IStringLocalizer<SharedResources> _loc;
    private readonly ILogger<CodeAgentService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDbContextFactory<AIDbContext> _aiDbFactory;

    public CodeAgentService(
        AiSettingsService aiSettings,
        IStringLocalizer<SharedResources> loc,
        ILogger<CodeAgentService> logger,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IDbContextFactory<AIDbContext> aiDbFactory)
    {
        _aiSettings = aiSettings;
        _loc = loc;
        _logger = logger;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _aiDbFactory = aiDbFactory;
    }

    /// <summary>系统提示词基础规则（不含工具说明，工具规则按模式动态追加）</summary>
    private const string BaseInstructions =
        """
        你是一名资深软件工程师，辅助用户完成编程任务。
        规则：
        1. 用户要求生成代码时：只输出代码本身，用 ``` 代码块包裹，不要输出解释、评论性前言或后语。
        2. 优先选择最简单可靠的实现，遵循目标语言的主流最佳实践。
        3. 如有多个文件，按逻辑顺序依次输出，每个文件用注释标明文件名（如 // File: Program.cs）。
        4. 不要假设环境里有未安装的库；控制台程序优先用 .NET 内置 / Python 标准库实现。
        """;

    private const string SearchToolRule =
        "需要外部信息（最新资料、官方文档、API 用法、版本号、报错原因）时，必须直接调用 tavily_search 搜索，必要时调用 web_fetch 精读页面，然后基于真实信息回答；绝对不要编写调用搜索 API 的示例代码来代替实际查询。\n" +
        "涉及微软技术（MAF/.NET/Azure/Windows/C# 官方 API）时优先调用 learn_docs_search / learn_docs_fetch / learn_code_sample_search 查微软官方文档与代码示例，而不是凭记忆写 API。\n";

    private const string CodeGraphToolRule =
        "用户问题涉及 baihuagu 项目代码本身（某功能在哪、某符号被谁用、改某处会影响什么）时，优先调用 gitnexus_query / gitnexus_context / gitnexus_impact 基于真实代码图谱回答，不要凭记忆猜测。\n";

    /// <summary>
    /// 创建 MAF ChatClientAgent（OpenAI 兼容端点），按工具模式挂载工具。
    /// enableCompaction=true 时挂 CompactionProvider（ContextWindow 策略：工具结果先压、超预算截断），
    /// 仅在传入 AgentSession 的会话模式下生效（单次调用不压缩）。
    /// </summary>
    public ChatClientAgent CreateAgent(string providerId, string model, CodeAgentToolMode toolMode = CodeAgentToolMode.All, string? customInstructions = null, bool enableCompaction = false)
    {
        var provider = _aiSettings.GetAiProvider(providerId)
            ?? throw new InvalidOperationException(_loc["AiClient_ProviderNotFound", providerId]);

        var apiKey = _aiSettings.GetAiApiKey(providerId);
        var endpoint = new Uri(provider.AiBaseUrl.TrimEnd('/'));

        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = endpoint };
        var credential = string.IsNullOrWhiteSpace(apiKey)
            ? new System.ClientModel.ApiKeyCredential("placeholder")
            : new System.ClientModel.ApiKeyCredential(apiKey);

        var chatClient = new OpenAI.OpenAIClient(credential, clientOptions)
            .GetChatClient(model)
            .AsIChatClient();
        // OTel GenAI 遥测（span/metric 按 GenAI 语义约定，含 token 用量）
        var builder = chatClient.AsBuilder().UseOpenTelemetry();
        if (enableCompaction)
        {
#pragma warning disable MAAI001 // 压缩 API 目前标记 Experimental（官方文档推荐用法）
            builder.UseAIContextProviders([
                new Microsoft.Agents.AI.Compaction.CompactionProvider(
                    new Microsoft.Agents.AI.Compaction.ContextWindowCompactionStrategy(
                        maxContextWindowTokens: 1_000_000,
                        maxOutputTokens: 8_000))
            ]);
#pragma warning restore MAAI001
        }
        var chatClientWithTelemetry = builder.Build();

        var codeAgentTools = new CodeAgentTools(_configuration, _loggerFactory);
        var tools = new List<Microsoft.Extensions.AI.AITool>();
        var instructions = customInstructions ?? BaseInstructions;

        if (toolMode is CodeAgentToolMode.All or CodeAgentToolMode.Search)
        {
            tools.Add(AIFunctionFactory.Create(codeAgentTools.TavilySearch,
                "tavily_search",
                "使用 Tavily 搜索引擎查询全网信息（最新资料、官方文档、报错排查）。参数 query 为搜索关键词，maxResults 为返回条数（1-10，默认 5）。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.WebFetch,
                "web_fetch",
                "抓取指定网页（http/https）并返回纯文本正文，适合精读官方文档。参数 url 为完整地址，maxChars 为最大字符数（默认 20000）。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.LearnDocsSearch,
                "learn_docs_search",
                "搜索微软官方文档（Microsoft Learn）：MAF/.NET/Azure/Windows/Office 等微软技术的权威资料。参数 query 为搜索关键词。微软技术问题优先用它而非 tavily_search。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.LearnDocsFetch,
                "learn_docs_fetch",
                "获取微软 Learn 文档完整文章（markdown）。用于精读 learn_docs_search 命中或已知的高价值页面，参数 url 必须是以 microsoft.com 结尾的文档地址（如 https://learn.microsoft.com/...）。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.LearnCodeSampleSearch,
                "learn_code_sample_search",
                "搜索微软官方代码示例（Learn）：生成或参考 .NET/C#/MAF/Azure 相关代码时使用，返回带语言的官方代码片段。参数 query 为描述/SDK/类名，language 可选（csharp/javascript/typescript/python 等）。"));
            instructions += SearchToolRule;
        }

        if (toolMode is CodeAgentToolMode.All or CodeAgentToolMode.CodeGraph)
        {
            tools.Add(AIFunctionFactory.Create(codeAgentTools.GitNexusQuery,
                "gitnexus_query",
                "在本地代码知识图谱中按概念搜索代码：找某个功能/流程的实现在哪些文件、涉及哪些符号。参数 query 为概念关键词（如\"登录流程\"、\"CodeAgent\"），repo 默认 baihuagu。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.GitNexusContext,
                "gitnexus_context",
                "查看某个代码符号（类/方法/函数名）的 360° 上下文：谁调用它、它调用谁、参与哪些执行流。参数 symbol 为符号名。"));
            tools.Add(AIFunctionFactory.Create(codeAgentTools.GitNexusImpact,
                "gitnexus_impact",
                "分析修改某个符号的影响范围（爆炸半径）：upstream=哪些代码依赖它（改它会不会破坏别人），downstream=它依赖什么。参数 target 为符号名，direction 默认 upstream。"));
            instructions += CodeGraphToolRule;
        }

        return new ChatClientAgent(chatClientWithTelemetry,
            instructions: instructions.Trim(),
            name: "CodeAgent",
            tools: tools);
    }

    /// <summary>
    /// 构建用户消息（含语言/上下文约束）。
    /// </summary>
    public static ChatMessage BuildUserMessage(string prompt, string? language, string? context)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(language))
        {
            sb.AppendLine($"语言/技术栈：{language}");
        }
        sb.AppendLine($"需求：{prompt}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine("补充上下文：");
            sb.AppendLine(context);
        }
        return new ChatMessage(ChatRole.User, sb.ToString());
    }

    /// <summary>
    /// 从 agent 输出中提取代码块（去掉 ``` 标记），并识别文件名。
    /// </summary>
    public static (string Code, string? FileName) ExtractCode(string output)
    {
        var code = output;
        var fileName = (string?)null;

        // 文件名识别：// File: xxx / # File: xxx / <!-- File: xxx -->
        var fileMatch = System.Text.RegularExpressions.Regex.Match(output,
            @"(?i)(?:File|文件名)\s*[:：]\s*([^\r\n`]+)");
        if (fileMatch.Success)
        {
            var candidate = fileMatch.Groups[1].Value.Trim().Trim('"', '\'', '*');
            if (!string.IsNullOrWhiteSpace(candidate))
                fileName = candidate;
        }

        // 提取首个代码块内容
        var blockMatch = System.Text.RegularExpressions.Regex.Match(output, @"```[^\r\n]*\r?\n(.*?)```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (blockMatch.Success)
        {
            code = blockMatch.Groups[1].Value.TrimEnd('\r', '\n');
        }

        return (code, fileName);
    }

    // ================== 多 Agent 流水线（调研 → 编码 → 审查） ==================

    private const string ResearchInstructions =
        """
        你是技术调研助手。针对用户需求做快速调研：
        1. 涉及的技术方案与资料要点（需要时用 tavily_search / web_fetch 查最新资料）
        2. 相关代码位置（需要时用 gitnexus 查代码图谱）
        3. 潜在风险点
        输出为简洁条目，400 字以内，不要写代码。
        """;

    private const string ReviewInstructions =
        """
        你是资深代码审查员。审查用户提供的代码，按严重程度列出问题清单，每项包含：位置 / 问题 / 修改建议。
        重点检查：正确性、边界条件、安全性、可读性、资源释放。
        如果未发现问题，回复「✅ 未发现问题」。不要重写代码，只列问题。
        """;

    /// <summary>从序列化状态恢复会话（无状态时返回 null）。</summary>
    public static async Task<Microsoft.Agents.AI.AgentSession?> ResolveSessionAsync(
        ChatClientAgent agent, string? sessionStateJson)
    {
        if (string.IsNullOrWhiteSpace(sessionStateJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(sessionStateJson);
            return await agent.DeserializeSessionAsync(doc.RootElement.Clone());
        }
        catch (Exception)
        {
            // 状态损坏/版本不兼容时放弃会话，从零开始
            return null;
        }
    }

    /// <summary>序列化会话状态（无会话时返回 null）。</summary>
    public static async Task<string?> SerializeSessionAsync(ChatClientAgent agent, Microsoft.Agents.AI.AgentSession? session)
    {
        if (session is null)
            return null;
        try
        {
            var element = await agent.SerializeSessionAsync(session);
            return element.GetRawText();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>记录一次 CodeAgent 调用到 AiUsageMetrics（与聊天/生成同一张统计表）。</summary>
    public async Task RecordUsageAsync(string providerId, string model, string operation,
        long latencyMs, long? inputTokens, long? outputTokens, string? error = null)
    {
        try
        {
            await using var db = await _aiDbFactory.CreateDbContextAsync();
            var providerName = _aiSettings.GetAiProvider(providerId)?.Name ?? providerId;
            // 与聊天/生成一致：输出 token / 秒（供性能监控页 TPS 指标）
            double? tps = null;
            if (latencyMs > 0 && outputTokens is > 0)
                tps = outputTokens.Value / (latencyMs / 1000.0);
            db.AiUsageMetrics.Add(new AiUsageMetric
            {
                ProviderId = providerId,
                ProviderName = providerName,
                ModelId = model,
                Operation = operation,
                LatencyMs = latencyMs,
                InputTokens = inputTokens is null ? null : (int)inputTokens,
                OutputTokens = outputTokens is null ? null : (int)outputTokens,
                TotalTokens = (int)((inputTokens ?? 0) + (outputTokens ?? 0)),
                TokensPerSecond = tps,
                ErrorMessage = error
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录 CodeAgent 用量失败");
        }
    }

    /// <summary>流水线（非流式）：调研 → 编码 → 审查，返回各阶段结果。</summary>
    public async Task<CodeAgentPipelineResponse> RunPipelineAsync(
        string providerId, string model, string prompt, string? language, string? context,
        bool skipResearch, bool skipReview, string? planModel = null, string? reviewModel = null, CancellationToken ct = default)
    {
        using var activity = CodeAgentActivity.StartActivity("PipelineRun");
        activity?.SetTag("provider", providerId);
        activity?.SetTag("model", model);

        var userMsg = BuildUserMessage(prompt, language, context);
        var planModelId = string.IsNullOrWhiteSpace(planModel) ? model : planModel!;
        var reviewModelId = string.IsNullOrWhiteSpace(reviewModel) ? model : reviewModel!;

        string? research = null;
        if (!skipResearch)
        {
            var researchAgent = CreateAgent(providerId, planModelId, CodeAgentToolMode.Search, ResearchInstructions);
            var sw = Stopwatch.StartNew();
            var result = await researchAgent.RunAsync(new[] { userMsg }, session: null, options: null, ct);
            sw.Stop();
            research = result.ToString();
            await RecordUsageAsync(providerId, planModelId, "codeagent-pipeline-research", sw.ElapsedMilliseconds,
                result.Usage?.InputTokenCount, result.Usage?.OutputTokenCount);
        }

        var codeContext = context;
        if (!string.IsNullOrWhiteSpace(research))
        {
            codeContext = $"{(string.IsNullOrWhiteSpace(context) ? "" : context + "\n\n")}【调研摘要】\n{research.Trim()}";
        }
        var coder = CreateAgent(providerId, model, CodeAgentToolMode.CodeGraph);
        var codeSw = Stopwatch.StartNew();
        var codeResult = await coder.RunAsync(new[] { BuildUserMessage(prompt, language, codeContext) }, session: null, options: null, ct);
        codeSw.Stop();
        var codeOutput = codeResult.ToString();
        var (code, fileName) = ExtractCode(codeOutput);
        await RecordUsageAsync(providerId, model, "codeagent-pipeline-code", codeSw.ElapsedMilliseconds,
            codeResult.Usage?.InputTokenCount, codeResult.Usage?.OutputTokenCount);

        string? review = null;
        if (!skipReview)
        {
            var reviewer = CreateAgent(providerId, reviewModelId, CodeAgentToolMode.None, ReviewInstructions);
            var reviewSw = Stopwatch.StartNew();
            var reviewResult = await reviewer.RunAsync(new[] { BuildUserMessage($"请审查以下代码：\n\n{code}", null, null) }, session: null, options: null, ct);
            reviewSw.Stop();
            review = reviewResult.ToString();
            await RecordUsageAsync(providerId, reviewModelId, "codeagent-pipeline-review", reviewSw.ElapsedMilliseconds,
                reviewResult.Usage?.InputTokenCount, reviewResult.Usage?.OutputTokenCount);
        }

        return new CodeAgentPipelineResponse
        {
            Success = true,
            Research = research,
            Code = string.IsNullOrWhiteSpace(code) ? codeOutput : code,
            FileName = fileName,
            Review = review,
            ProviderId = providerId,
            Model = model
        };
    }

    /// <summary>流水线（流式）：按阶段产出更新（stage/delta/tool），供 SSE 推送。</summary>
    public async IAsyncEnumerable<CodeAgentPipelineUpdate> RunPipelineStreamingAsync(
        string providerId, string model, string prompt, string? language, string? context,
        bool skipResearch, bool skipReview, string? planModel = null, string? reviewModel = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = CodeAgentActivity.StartActivity("PipelineRunStreaming");
        activity?.SetTag("provider", providerId);
        activity?.SetTag("model", model);

        var userMsg = BuildUserMessage(prompt, language, context);
        var planModelId = string.IsNullOrWhiteSpace(planModel) ? model : planModel!;
        var reviewModelId = string.IsNullOrWhiteSpace(reviewModel) ? model : reviewModel!;
        var codeText = new StringBuilder();

        if (!skipResearch)
        {
            yield return new StageUpdate("research");
            var researchAgent = CreateAgent(providerId, planModelId, CodeAgentToolMode.Search, ResearchInstructions);
            await foreach (var u in RunAgentUpdatesAsync(researchAgent, userMsg, ct))
            {
                if (u is DeltaUpdate d) codeText.AppendLine(d.Text);
                yield return u;
            }
        }

        yield return new StageUpdate("code");
        var codeContext = context;
        var research = codeText.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(research))
        {
            codeContext = $"{(string.IsNullOrWhiteSpace(context) ? "" : context + "\n\n")}【调研摘要】\n{research}";
        }
        var coder = CreateAgent(providerId, model, CodeAgentToolMode.CodeGraph);
        var fullCodeText = new StringBuilder();
        await foreach (var u in RunAgentUpdatesAsync(coder, BuildUserMessage(prompt, language, codeContext), ct))
        {
            if (u is DeltaUpdate d) fullCodeText.AppendLine(d.Text);
            yield return u;
        }

        if (!skipReview)
        {
            yield return new StageUpdate("review");
            var reviewer = CreateAgent(providerId, reviewModelId, CodeAgentToolMode.None, ReviewInstructions);
            await foreach (var u in RunAgentUpdatesAsync(reviewer, BuildUserMessage($"请审查以下代码：\n\n{fullCodeText}", null, null), ct))
            {
                yield return u;
            }
        }
    }

    /// <summary>把 MAF 流式更新映射为流水线更新（文本/工具调用/工具结果）。</summary>
    private static async IAsyncEnumerable<CodeAgentPipelineUpdate> RunAgentUpdatesAsync(
        ChatClientAgent agent, ChatMessage message, [EnumeratorCancellation] CancellationToken ct)
    {
        var toolNames = new Dictionary<string, string>();
        await foreach (var update in agent.RunStreamingAsync(new[] { message }, session: null, options: null, ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        yield return new DeltaUpdate(text.Text);
                        break;
                    case FunctionCallContent fc:
                        if (!string.IsNullOrEmpty(fc.CallId)) toolNames[fc.CallId] = fc.Name ?? "";
                        yield return new ToolCallUpdate(fc.Name ?? "",
                            fc.Arguments is null ? "" : JsonSerializer.Serialize(fc.Arguments));
                        break;
                    case FunctionResultContent fr:
                        toolNames.TryGetValue(fr.CallId ?? "", out var name);
                        yield return new ToolResultUpdate(name ?? fr.CallId ?? "",
                            Truncate(fr.Result?.ToString() ?? "", 200));
                        break;
                }
            }
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");
}

/// <summary>流水线流式更新类型。</summary>
public abstract record CodeAgentPipelineUpdate;

/// <summary>阶段切换（research / code / review）。</summary>
public sealed record StageUpdate(string Name) : CodeAgentPipelineUpdate;

/// <summary>文本增量。</summary>
public sealed record DeltaUpdate(string Text) : CodeAgentPipelineUpdate;

/// <summary>工具调用。</summary>
public sealed record ToolCallUpdate(string Name, string Detail) : CodeAgentPipelineUpdate;

/// <summary>工具结果。</summary>
public sealed record ToolResultUpdate(string Name, string Detail) : CodeAgentPipelineUpdate;
