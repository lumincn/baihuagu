using System.Text.Json;
using Baihua.Core.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.Ai;
using Baihua.Family.Services.LocalAI;

namespace Baihua.Family.Controllers;

/// <summary>
/// 本地模型 AI 对话（GGUF / ONNX）
/// </summary>
[ApiController]
[Route("api/local-ai")]
public class LocalAIController : ControllerBase
{
    private readonly IEnumerable<ILocalModelInference> _inferences;
    private readonly ILogger<LocalAIController> _logger;
    private readonly IStringLocalizer<SharedResources> _loc;

    public LocalAIController(
        IEnumerable<ILocalModelInference> inferences,
        ILogger<LocalAIController> logger,
        IStringLocalizer<SharedResources> loc)
    {
        _inferences = inferences;
        _logger = logger;
        _loc = loc;
    }

    /// <summary>
    /// 流式本地模型对话（SSE）
    /// </summary>
    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] LocalChatRequest request)
    {
        var httpResponse = HttpContext.Response;
        httpResponse.ContentType = "text/event-stream";
        httpResponse.Headers["Cache-Control"] = "no-cache";
        httpResponse.Headers["X-Accel-Buffering"] = "no";

        async Task SendSse(string eventType, string data)
        {
            await httpResponse.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
            await httpResponse.Body.FlushAsync();
        }

        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                await SendSse("error", _loc["AiChat_MessageEmpty"].Value);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.ModelPath))
            {
                await SendSse("error", _loc["LocalAi_ModelPathRequired"].Value);
                return;
            }

            var inference = _inferences.FirstOrDefault(i =>
                i.ModelType.Equals(request.ModelType, StringComparison.OrdinalIgnoreCase));

            if (inference == null)
            {
                await SendSse("error", _loc["LocalAi_UnsupportedModelType", request.ModelType].Value);
                return;
            }

            if (!await inference.IsModelAvailableAsync(request.ModelPath))
            {
                await SendSse("error", _loc["LocalAi_ModelUnavailable", request.ModelPath].Value);
                return;
            }

            // 发送元信息
            var modelName = Path.GetFileName(request.ModelPath.TrimEnd('/', '\\'));
            await SendSse("meta", JsonSerializer.Serialize(new { provider = "本地模型", model = $"{request.ModelType}:{modelName}" }));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var history = request.History?.Select(h => (h.Role, h.Content)).ToList() ?? new List<(string Role, string Content)>();
            var toolPrompt = BuildToolPrompt(request.SystemPrompt);
            var fullFirst = new System.Text.StringBuilder();

            // 第一轮：收集完整回复（可能含 TOOL_CALL）
            await foreach (var text in inference.ChatAsync(
                request.ModelPath,
                request.Message,
                toolPrompt,
                history,
                linkedCts.Token))
            {
                fullFirst.Append(text);
            }

            var firstText = fullFirst.ToString();
            var toolCall = ParseToolCall(firstText);

            if (toolCall != null)
            {
                // 执行工具，把结果喂回模型进行第二轮
                var (toolName, toolArgs) = toolCall.Value;
                var toolResult = await ExecuteLocalToolAsync(toolName, toolArgs);
                _logger.LogInformation("本地模型调用工具: {Tool} -> {Result}", toolName, toolResult);
                await SendSse("tool_call", JsonSerializer.Serialize(new { tool = toolName, result = toolResult }));

                // 工具结果并入 assistant 消息（避免连续 user 消息，LlamaSharp 会拒绝）
                history.Add(("assistant", firstText + $"\n[工具 {toolName} 返回结果：{toolResult}]"));

                await foreach (var text in inference.ChatAsync(
                    request.ModelPath,
                    request.Message,
                    BuildToolPrompt(null),
                    history,
                    linkedCts.Token))
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        await SendSse("delta", JsonSerializer.Serialize(new { content = text }));
                    }
                }
            }
            else
            {
                // 无工具调用，直接输出
                if (!string.IsNullOrEmpty(firstText))
                {
                    await SendSse("delta", JsonSerializer.Serialize(new { content = firstText }));
                }
            }

            await SendSse("done", "");
        }
        catch (OperationCanceledException)
        {
            await SendSse("error", _loc["AiChat_Timeout"].Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地模型流式聊天失败: {ModelPath} ({ModelType})", request.ModelPath, request.ModelType);
            await SendSse("error", _loc["Ai_Chat_Failed", ex.Message].Value);
        }
    }

    private static string BuildToolPrompt(string? originalSystemPrompt)
    {
        const string toolDesc = """
你有以下工具可用：
- get_current_date: 获取当前日期时间（无参数）
- get_system_status: 获取本机系统运行状态（无参数）

如果需要调用工具，请在回复开头单独一行使用以下格式（严格 JSON，不要多余字符）：
TOOL_CALL: {"tool":"工具名","arguments":{}}
然后在下一行给出你的正常回复。
""";
        return string.IsNullOrWhiteSpace(originalSystemPrompt)
            ? toolDesc
            : originalSystemPrompt + "\n\n" + toolDesc;
    }

    private static (string Tool, JsonElement Arguments)? ParseToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var idx = text.IndexOf("TOOL_CALL:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var braceIdx = text.IndexOf('{', idx);
        if (braceIdx < 0) return null;

        // 平衡括号扫描（支持 arguments 嵌套对象）
        int depth = 0;
        int end = -1;
        for (int i = braceIdx; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) { end = i + 1; break; }
            }
        }
        if (end < 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(text.Substring(braceIdx, end - braceIdx));
            var root = doc.RootElement;
            var tool = root.TryGetProperty("tool", out var t) ? t.GetString() : "";
            var args = root.TryGetProperty("arguments", out var a) ? a.Clone() : default;
            return string.IsNullOrWhiteSpace(tool) ? null : (tool, args);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> ExecuteLocalToolAsync(string tool, JsonElement arguments)
    {
        try
        {
            switch (tool)
            {
                case "get_current_date":
                    return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss dddd");
                case "get_system_status":
                    var memMb = GC.GetTotalMemory(false) / 1024 / 1024;
                    var uptimeMin = Environment.TickCount64 / 1000 / 60;
                    return $"进程内存 {memMb:F0} MB；当前时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss}；进程已运行 {uptimeMin} 分钟；系统 {Environment.OSVersion}";
                default:
                    return $"未知工具 {tool}（可用：get_current_date, get_system_status）";
            }
        }
        catch (Exception ex)
        {
            return $"工具执行失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 扫描指定目录下的可用本地模型
    /// </summary>
    [HttpGet("scan")]
    public async Task<ActionResult<List<LocalModelInfo>>> ScanModels([FromQuery] string? directory = null)
    {
        var results = new List<LocalModelInfo>();
        var dirsToScan = new List<string>();

        if (!string.IsNullOrWhiteSpace(directory))
        {
            dirsToScan.Add(directory);
        }
        else
        {
            // 尝试多个常见位置
            dirsToScan.Add(Path.Combine(AppContext.BaseDirectory, "models"));
            dirsToScan.Add(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models"));
            // 推断项目根目录（从 services/task_runner_csharp/bin/Debug/net10.0 向上）
            var baseDir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                baseDir = Path.GetDirectoryName(baseDir) ?? baseDir;
                var candidate = Path.Combine(baseDir, "models");
                if (!dirsToScan.Contains(candidate))
                    dirsToScan.Add(candidate);
            }
        }

        // 扫描 GGUF 文件
        try
        {
            var ggufInference = _inferences.FirstOrDefault(i => i.ModelType == "gguf");
            if (ggufInference != null)
            {
                foreach (var scanDir in dirsToScan.Where(Directory.Exists))
                {
                    foreach (var file in Directory.EnumerateFiles(scanDir, "*.gguf", SearchOption.AllDirectories))
                    {
                        if (await ggufInference.IsModelAvailableAsync(file))
                        {
                            results.Add(new LocalModelInfo
                            {
                                Name = Path.GetFileName(file),
                                Path = file,
                                Type = "gguf",
                                Size = new FileInfo(file).Length
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫描 GGUF 模型失败");
        }

        // 扫描 ONNX 目录（包含 genai_config.json 的子目录）
        try
        {
            var onnxInference = _inferences.FirstOrDefault(i => i.ModelType == "onnx");
            if (onnxInference != null)
            {
                foreach (var scanDir in dirsToScan.Where(Directory.Exists))
                {
                    foreach (var dir in Directory.EnumerateDirectories(scanDir, "*", SearchOption.AllDirectories))
                    {
                        if (await onnxInference.IsModelAvailableAsync(dir))
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            results.Add(new LocalModelInfo
                            {
                                Name = dirInfo.Name,
                                Path = dir,
                                Type = "onnx",
                                Size = dirInfo.EnumerateFiles().Sum(f => f.Length)
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫描 ONNX 模型失败");
        }

        return Ok(results);
    }


}
