using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Models;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class ChatCompletionsController
{
        private async Task<OpenAiChatResponse?> HandleToolCallingAsync(
            AiProviderConfig provider, string model,
            List<ChatMessage> messages, ChatOptions options,
            List<OpenAiTool> tools, CancellationToken ct)
        {
            try
            {
                // 在系统提示中注入工具描述
                var toolDescriptions = string.Join("\n", tools.Select(t =>
                    $"- {t.Function.Name}: {t.Function.Description}"));

                var systemIndex = messages.FindIndex(m => m.Role == ChatRole.System);
                if (systemIndex >= 0)
                {
                    var original = messages[systemIndex].Text;
                    messages[systemIndex] = new ChatMessage(ChatRole.System,
                        original + $"\n\n你有以下工具可用：\n{toolDescriptions}\n\n" +
                        "如果需要调用工具，请在回复开头使用以下格式（严格 JSON）：\n" +
                        "TOOL_CALL: {\"tool\":\"工具名\",\"arguments\":{参数对象}}\n" +
                        "然后在新的一行给出你的正常回复。");
                }

                // 第一次调用：让模型决定是否需要工具
                var firstResponse = await _aiClientService.GetChatResponseWithAutoStartAsync(
                    provider, model, messages, options, ct);

                var text = firstResponse.Text ?? "";

                // 解析工具调用
                var toolCallMatch = System.Text.RegularExpressions.Regex.Match(text,
                    @"TOOL_CALL:\s*(\{.*\})\s*", System.Text.RegularExpressions.RegexOptions.Singleline);

                if (!toolCallMatch.Success)
                {
                    // 没有工具调用，返回正常回复
                    return null;
                }

                var toolCallJson = toolCallMatch.Groups[1].Value;
                var remainingText = text.Substring(toolCallMatch.Index + toolCallMatch.Length).Trim();

                using var doc = System.Text.Json.JsonDocument.Parse(toolCallJson);
                var root = doc.RootElement;
                var toolName = root.GetProperty("tool").GetString() ?? "";
                var arguments = root.GetProperty("arguments");

                _logger.LogInformation("模型请求调用工具: {ToolName}", toolName);

                // 执行 MCP 工具
                var toolResult = await _mcpServerService.CallToolAsync(
                    new TaskRunner.Contracts.Mcp.McpToolCallRequest
                    {
                        Name = toolName,
                        Arguments = arguments
                    }, ct);

                var resultText = toolResult.Content?.ToString() ?? "工具执行完成，无返回内容";

                // 将工具结果注入消息列表
                messages.Add(new ChatMessage(ChatRole.Assistant,
                    $"我将调用工具 {toolName} 来帮助你。"));
                messages.Add(new ChatMessage(ChatRole.Tool, resultText));

                // 第二次调用：生成最终回复
                var finalResponse = await _aiClientService.GetChatResponseWithAutoStartAsync(
                    provider, model, messages, options, ct);

                var finalText = finalResponse.Text ?? "";
                if (!string.IsNullOrWhiteSpace(remainingText) && string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = remainingText;
                }

                return new OpenAiChatResponse
                {
                    Id = $"chatcmpl-{Guid.NewGuid().ToString("N")[..12]}",
                    Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Model = $"{provider.Id}/{model}",
                    Choices =
                    [
                        new OpenAiChoice
                        {
                            Index = 0,
                            Message = new OpenAiMessage
                            {
                                Role = "assistant",
                                Content = finalText
                            },
                            FinishReason = "stop"
                        }
                    ],
                    Usage = new OpenAiUsage
                    {
                        PromptTokens = (int)(firstResponse.Usage?.InputTokenCount ?? 0 + finalResponse.Usage?.InputTokenCount ?? 0),
                        CompletionTokens = (int)(firstResponse.Usage?.OutputTokenCount ?? 0 + finalResponse.Usage?.OutputTokenCount ?? 0),
                        TotalTokens = (int)((firstResponse.Usage?.TotalTokenCount ?? 0) + (finalResponse.Usage?.TotalTokenCount ?? 0))
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工具调用处理失败");
                return null;
            }
        }

        private static (string ProviderId, string ModelId) ParseModel(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return ("", "");

            // 支持格式: "provider/model" 或 "model"
            if (model.Contains('/'))
            {
                var parts = model.Split('/', 2);
                return (parts[0], parts[1]);
            }

            // 也支持 openclaw 的格式: "ollama/biancang:latest"
            return ("", model);
        }

        private static bool IsLocalProvider(AiProviderConfig provider)
        {
            if (string.IsNullOrWhiteSpace(provider?.AiBaseUrl))
                return false;
            var url = provider.AiBaseUrl.ToLowerInvariant();
            return url.Contains("localhost") || url.Contains("127.0.0.1") || url.Contains("0.0.0.0");
        }

        private static ChatRole ParseRole(string? role)
        {
            return role?.ToLowerInvariant() switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                "user" => ChatRole.User,
                "tool" => ChatRole.Tool,
                _ => ChatRole.User
            };
        }
}
