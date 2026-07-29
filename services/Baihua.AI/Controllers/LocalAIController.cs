using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Ai;
using TaskRunner.Services.LocalAI;

namespace TaskRunner.Controllers;

/// <summary>
/// 本地模型 AI 对话（GGUF / ONNX）
/// </summary>
[ApiController]
[Route("api/local-ai")]
public class LocalAIController : ControllerBase
{
    private readonly IEnumerable<ILocalModelInference> _inferences;
    private readonly ILogger<LocalAIController> _logger;

    public LocalAIController(
        IEnumerable<ILocalModelInference> inferences,
        ILogger<LocalAIController> logger)
    {
        _inferences = inferences;
        _logger = logger;
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
                await SendSse("error", "消息不能为空");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.ModelPath))
            {
                await SendSse("error", "模型路径不能为空");
                return;
            }

            var inference = _inferences.FirstOrDefault(i =>
                i.ModelType.Equals(request.ModelType, StringComparison.OrdinalIgnoreCase));

            if (inference == null)
            {
                await SendSse("error", $"不支持的本地模型类型: {request.ModelType}");
                return;
            }

            if (!await inference.IsModelAvailableAsync(request.ModelPath))
            {
                await SendSse("error", $"模型不可用: {request.ModelPath}");
                return;
            }

            // 发送元信息
            var modelName = Path.GetFileName(request.ModelPath.TrimEnd('/', '\\'));
            await SendSse("meta", JsonSerializer.Serialize(new { provider = "本地模型", model = $"{request.ModelType}:{modelName}" }));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

            var history = request.History?.Select(h => (h.Role, h.Content)).ToList();
            await foreach (var text in inference.ChatAsync(
                request.ModelPath,
                request.Message,
                request.SystemPrompt,
                history,
                linkedCts.Token))
            {
                if (!string.IsNullOrEmpty(text))
                {
                    await SendSse("delta", JsonSerializer.Serialize(new { content = text }));
                }
            }

            await SendSse("done", "");
        }
        catch (OperationCanceledException)
        {
            await SendSse("error", "AI 调用超时或已被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地模型流式聊天失败: {ModelPath} ({ModelType})", request.ModelPath, request.ModelType);
            await SendSse("error", $"聊天失败：{ex.Message}");
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
