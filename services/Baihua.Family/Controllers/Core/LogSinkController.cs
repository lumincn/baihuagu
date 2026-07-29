using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

/// <summary>
/// OpenObserve 日志后端配置 API
/// </summary>
[ApiController]
[Route("api/log-sink")]
public class LogSinkController : ControllerBase
{
    private readonly LogSinkConfigService _configService;

    public LogSinkController(LogSinkConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>获取当前 OpenObserve 配置</summary>
    [HttpGet]
    public ActionResult<OpenObserveConfig> GetConfig()
    {
        return Ok(_configService.GetConfig());
    }

    /// <summary>更新 OpenObserve 配置</summary>
    [HttpPut]
    public ActionResult UpdateConfig([FromBody] OpenObserveConfig config)
    {
        if (config == null)
            return BadRequest(new { error = "配置不能为空" });

        _configService.UpdateConfig(config);
        return Ok(new { message = "配置已更新" });
    }

    /// <summary>获取 OpenObserve Web UI 地址</summary>
    [HttpGet("web-url")]
    public ActionResult GetWebUrl()
    {
        var url = _configService.GetWebUrl();
        return Ok(new { url });
    }
}
