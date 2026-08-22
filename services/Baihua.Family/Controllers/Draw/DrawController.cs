using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Draw;
using Baihua.Core.Services;

namespace Baihua.Family.Controllers;

/// <summary>
/// 本地 AI 绘图 API：文生图（SD）与文生视频（LTX Video），走本机 ComfyUI。
/// 生成同步等待完成（图片约 20-60s，视频 1-5 分钟），完成后经 /api/draw/file 取文件。
/// </summary>
[ApiController]
[Route("api/draw")]
public class DrawController : ControllerBase
{
    private const int PollIntervalMs = 3000;
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan VideoTimeout = TimeSpan.FromMinutes(5.5);

    private readonly ComfyUiClient _comfy;
    private readonly ILogger<DrawController> _logger;

    public DrawController(ComfyUiClient comfy, ILogger<DrawController> logger)
    {
        _comfy = comfy;
        _logger = logger;
    }

    /// <summary>绘图能力状态：ComfyUI 在线与否 + 可用 checkpoint。</summary>
    [HttpGet("status")]
    public async Task<ActionResult<DrawStatusDto>> GetStatus(CancellationToken ct)
    {
        var dto = new DrawStatusDto();
        dto.ComfyUiOnline = await _comfy.IsAvailableAsync(ct);
        if (dto.ComfyUiOnline)
        {
            var checkpoints = await _comfy.GetCheckpointsAsync(ct);
            // 按是否视频模型归类（LTX/Wan/Hunyuan 等视为视频 checkpoint）
            foreach (var ck in checkpoints)
            {
                var lower = ck.ToLowerInvariant();
                if (lower.Contains("ltx") || lower.Contains("wan") || lower.Contains("hunyuan") || lower.Contains("cog") || lower.Contains("mochi"))
                    dto.VideoCheckpoints.Add(ck);
                else
                    dto.ImageCheckpoints.Add(ck);
            }
            if (dto.VideoCheckpoints.Count == 0)
                dto.VideoCheckpoints.Add(ComfyWorkflowBuilder.DefaultVideoCheckpoint);
            if (dto.ImageCheckpoints.Count == 0)
                dto.ImageCheckpoints.Add(ComfyWorkflowBuilder.DefaultImageCheckpoint);
        }
        return Ok(dto);
    }

    /// <summary>文生图（txt2img）。</summary>
    [HttpPost("image")]
    public async Task<ActionResult<DrawResultDto>> GenerateImage([FromBody] DrawImageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new DrawResultDto { Success = false, Error = "prompt 不能为空" });

        var seed = Random.Shared.NextInt64(0, long.MaxValue);
        var width = request.Width is > 0 and <= 2048 ? request.Width.Value : 512;
        var height = request.Height is > 0 and <= 2048 ? request.Height.Value : 512;
        var steps = request.Steps is > 0 and <= 100 ? request.Steps.Value : 20;
        var checkpoint = string.IsNullOrWhiteSpace(request.Checkpoint)
            ? ComfyWorkflowBuilder.DefaultImageCheckpoint
            : request.Checkpoint!;

        var workflow = ComfyWorkflowBuilder.BuildTxt2Image(request.Prompt, request.NegativePrompt, width, height, steps, seed, checkpoint);
        return await GenerateAndWaitAsync(workflow, ImageTimeout, ct);
    }

    /// <summary>文生视频（txt2video，LTX）。</summary>
    [HttpPost("video")]
    public async Task<ActionResult<DrawResultDto>> GenerateVideo([FromBody] DrawVideoRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new DrawResultDto { Success = false, Error = "prompt 不能为空" });

        var seed = Random.Shared.NextInt64(0, long.MaxValue);
        var width = request.Width is > 0 and <= 768 ? request.Width.Value : 512;
        var height = request.Height is > 0 and <= 768 ? request.Height.Value : 512;
        var length = request.Length is > 0 and <= 257 ? request.Length.Value : 97;
        var fps = request.Fps is > 0 and <= 60 ? request.Fps.Value : 25;
        var steps = request.Steps is > 0 and <= 100 ? request.Steps.Value : 20;
        var checkpoint = string.IsNullOrWhiteSpace(request.Checkpoint)
            ? ComfyWorkflowBuilder.DefaultVideoCheckpoint
            : request.Checkpoint!;

        var workflow = ComfyWorkflowBuilder.BuildTxt2Video(request.Prompt, request.NegativePrompt, width, height, length, fps, steps, seed, checkpoint);
        return await GenerateAndWaitAsync(workflow, VideoTimeout, ct);
    }

    /// <summary>取生成的文件（图片/视频字节），经百花中转避免客户端直连 ComfyUI。</summary>
    [HttpGet("file")]
    public async Task<IActionResult> GetFile(string filename, string subfolder = "", string type = "output", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return BadRequest("filename 不能为空");

        byte[] bytes;
        try
        {
            bytes = await _comfy.GetFileAsync(filename, subfolder, type, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 ComfyUI 文件失败: {File}", filename);
            return NotFound(new { error = "文件不存在或 ComfyUI 不可达" });
        }
        return File(bytes, MimeFor(filename));
    }

    /// <summary>提交工作流并同步轮询到完成，返回结果 DTO。</summary>
    private async Task<ActionResult<DrawResultDto>> GenerateAndWaitAsync(Dictionary<string, object> workflow, TimeSpan timeout, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            var promptId = await _comfy.SubmitAsync(workflow, ct);
            _logger.LogInformation("ComfyUI 生成已提交: promptId={PromptId}", promptId);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);
            while (true)
            {
                var result = await _comfy.GetResultAsync(promptId, deadline.Token);
                if (result == null)
                {
                    await Task.Delay(PollIntervalMs, deadline.Token);
                    continue;
                }
                if (result.IsError)
                    return Ok(new DrawResultDto { Success = false, Error = result.Error ?? "ComfyUI 执行出错", ElapsedSeconds = Elapsed(started) });

                var file = result.Files.FirstOrDefault();
                if (file == null)
                    return Ok(new DrawResultDto { Success = false, Error = "生成完成但未找到输出文件", ElapsedSeconds = Elapsed(started) });

                return Ok(new DrawResultDto
                {
                    Success = true,
                    FileName = file.Filename,
                    ContentType = MimeFor(file.Filename),
                    ElapsedSeconds = Elapsed(started)
                });
            }
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new DrawResultDto { Success = false, Error = "生成超时（请稍后重试或调低分辨率/帧数）", ElapsedSeconds = Elapsed(started) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文生图/文生视频失败");
            return StatusCode(StatusCodes.Status502BadGateway,
                new DrawResultDto { Success = false, Error = $"ComfyUI 调用失败：{ex.Message}", ElapsedSeconds = Elapsed(started) });
        }
    }

    private static double Elapsed(DateTime started) => Math.Round((DateTime.UtcNow - started).TotalSeconds, 1);

    private static string MimeFor(string filename)
    {
        var ext = System.IO.Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }
}
