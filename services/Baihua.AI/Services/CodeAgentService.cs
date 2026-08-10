using Baihua.Family.Models;
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
    private readonly AiSettingsService _aiSettings;
    private readonly IStringLocalizer<SharedResources> _loc;
    private readonly ILogger<CodeAgentService> _logger;

    public CodeAgentService(
        AiSettingsService aiSettings,
        IStringLocalizer<SharedResources> loc,
        ILogger<CodeAgentService> logger)
    {
        _aiSettings = aiSettings;
        _loc = loc;
        _logger = logger;
    }

    /// <summary>系统提示词：专注代码生成，输出纯净代码</summary>
    private const string DefaultInstructions =
        """
        你是一名资深软件工程师。根据用户需求生成完整、可直接运行的代码。
        规则：
        1. 只输出代码本身，用 ``` 代码块包裹，不要输出解释、评论性前言或后语。
        2. 优先选择最简单可靠的实现，遵循目标语言的主流最佳实践。
        3. 如有多个文件，按逻辑顺序依次输出，每个文件用注释标明文件名（如 // File: Program.cs）。
        4. 不要假设环境里有未安装的库；控制台程序优先用 .NET 内置 / Python 标准库实现。
        """;

    /// <summary>
    /// 创建 MAF ChatClientAgent（OpenAI 兼容端点）。
    /// </summary>
    public ChatClientAgent CreateAgent(string providerId, string model)
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

        return new ChatClientAgent(chatClient, instructions: DefaultInstructions, name: "CodeAgent");
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
}
