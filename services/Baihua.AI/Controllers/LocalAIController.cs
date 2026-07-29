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
            await SendSse("error", _loc["AiChat_Timeout"].Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地模型流式聊天失败: {ModelPath} ({ModelType})", request.ModelPath, request.ModelType);
            await SendSse("error", _loc["Ai_Chat_Failed", ex.Message].Value);
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
