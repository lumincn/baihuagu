using Baihua.Contracts.Ai;
using Baihua.AI.Provider;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers;

/// <summary>
/// 本地视觉识别（Qwen2.5-VL + OpenVINO）
/// </summary>
[ApiController]
[Route("api/local-ai/vision")]
public class LocalVisionController : ControllerBase
{
    private readonly OpenVinoVisionService _vision;
    private readonly ILogger<LocalVisionController> _logger;

    public LocalVisionController(OpenVinoVisionService vision, ILogger<LocalVisionController> logger)
    {
        _vision = vision;
        _logger = logger;
    }

    /// <summary>
    /// 视觉服务状态与可用模型
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<VisionStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        return Ok(await _vision.GetStatusAsync(cancellationToken));
    }

    /// <summary>
    /// 启动视觉服务（手动触发；正常会自动拉起）
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<VisionStatusDto>> Start(CancellationToken cancellationToken)
    {
        try
        {
            await _vision.EnsureServerRunningAsync(cancellationToken);
            return Ok(await _vision.GetStatusAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动本地视觉服务失败");
            return StatusCode(503, new VisionStatusDto
            {
                Enabled = _vision.Enabled,
                ServerRunning = false,
                Message = ex.Message,
            });
        }
    }

    /// <summary>
    /// 识别图片（JSON：imageBase64 + prompt + model）
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<VisionResultDto>> Recognize([FromBody] VisionRequestDto request, CancellationToken cancellationToken)
    {
        if (!_vision.Enabled)
            return StatusCode(503, new VisionResultDto { Text = "本地视觉功能未启用", ServerRunning = false });

        if (string.IsNullOrWhiteSpace(request.ImageBase64))
            return BadRequest(new VisionResultDto { Text = "图片内容为空" });

        try
        {
            var imageBytes = Convert.FromBase64String(request.ImageBase64);
            if (imageBytes.Length == 0)
                return BadRequest(new VisionResultDto { Text = "图片内容为空" });
            if (imageBytes.Length > 20 * 1024 * 1024)
                return BadRequest(new VisionResultDto { Text = "图片过大（>20MB）" });

            var result = await _vision.RecognizeAsync(imageBytes, request.Prompt, request.Model, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地视觉识别失败");
            return StatusCode(500, new VisionResultDto
            {
                Text = $"识别失败: {ex.Message}",
                Model = request.Model,
                ServerRunning = false,
            });
        }
    }
}
