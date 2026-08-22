using Baihua.Contracts.ComputePool;
using Baihua.Contracts.Draw;
using Baihua.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers.ComputePool;

/// <summary>
/// 算力池绘图网关（/mg/pool/v1/draw/*，X-Server-Token 或 Bearer 鉴权）。
/// 让局域网内其它百花服务器 / DSH 插件能跨机调用本机的文生图/文生视频（本地 ComfyUI）。
/// 与 /mg/capabilities 广播的 DrawCapability 配套：对端发现本机可绘图后再调用。
/// </summary>
[ApiController]
public class DrawGatewayController : ControllerBase
{
    private readonly ComfyDrawService _draw;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DrawGatewayController> _logger;

    public DrawGatewayController(
        ComfyDrawService draw,
        IConfiguration configuration,
        ILogger<DrawGatewayController> logger)
    {
        _draw = draw;
        _configuration = configuration;
        _logger = logger;
    }

    private bool Authorize()
    {
        var expected = _configuration["BAIHUA_AI_EXTERNAL_TOKEN"] ?? "";
        if (string.IsNullOrEmpty(expected)) return true;
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return string.Equals(auth["Bearer ".Length..].Trim(), expected, StringComparison.Ordinal);
        var token = Request.Headers["X-Server-Token"].FirstOrDefault();
        return string.Equals(token, expected, StringComparison.Ordinal);
    }

    /// <summary>绘图能力（ComfyUI 在线 + 支持图像/视频 + checkpoint）。对端发现用。</summary>
    [HttpGet("/mg/pool/v1/draw/capabilities")]
    public async Task<ActionResult<DrawCapabilityDto>> Capabilities(CancellationToken ct)
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });
        var online = await _draw.IsAvailableAsync(ct);
        var dto = new DrawCapabilityDto
        {
            ComfyOnline = online,
            Image = online,
            Video = online,
            ImageCheckpoint = ComfyWorkflowBuilder.DefaultImageCheckpoint,
            VideoCheckpoint = ComfyWorkflowBuilder.DefaultVideoCheckpoint
        };
        return Ok(dto);
    }

    /// <summary>文生图（跨机调用）。</summary>
    [HttpPost("/mg/pool/v1/draw/image")]
    public async Task<ActionResult<DrawResultDto>> Image([FromBody] DrawImageRequest request, CancellationToken ct)
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new DrawResultDto { Success = false, Error = "prompt 不能为空" });
        _logger.LogInformation("[ComputePool] 对端文生图请求: Prompt={Prompt}", request.Prompt);
        return Ok(await _draw.GenerateImageAsync(request, ct));
    }

    /// <summary>文生视频（跨机调用）。</summary>
    [HttpPost("/mg/pool/v1/draw/video")]
    public async Task<ActionResult<DrawResultDto>> Video([FromBody] DrawVideoRequest request, CancellationToken ct)
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new DrawResultDto { Success = false, Error = "prompt 不能为空" });
        _logger.LogInformation("[ComputePool] 对端文生视频请求: Prompt={Prompt}", request.Prompt);
        return Ok(await _draw.GenerateVideoAsync(request, ct));
    }

    /// <summary>取生成的文件（图片/视频字节），经本机中转（对端客户端无需直连 ComfyUI）。</summary>
    [HttpGet("/mg/pool/v1/draw/file")]
    public async Task<IActionResult> File(string filename, string subfolder = "", string type = "output", CancellationToken ct = default)
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });
        if (string.IsNullOrWhiteSpace(filename))
            return BadRequest("filename 不能为空");
        try
        {
            var bytes = await _draw.GetFileAsync(filename, subfolder, type, ct);
            return File(bytes, MimeFor(filename));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[ComputePool] 读取绘图文件失败: {File} ({Msg})", filename, ex.Message);
            return NotFound(new { error = "文件不存在或 ComfyUI 不可达" });
        }
    }

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
