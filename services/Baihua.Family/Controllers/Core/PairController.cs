using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskRunner.Data;
using TaskRunner.Services;
using TaskRunner.Services.Strategies;
using TaskRunner.Contracts.Vaults;
using TaskRunner.Contracts.Pairing;

namespace TaskRunner.Controllers
{
    [ApiController]
    public class PairController : ControllerBase
    {
        private readonly DeviceService _deviceService;
        private readonly ILogger<PairController> _logger;
        private readonly IOneHopService _oneHopService;
        private readonly IPairingStrategy _pairingStrategy;

        public PairController(DeviceService deviceService, ILogger<PairController> logger, IOneHopService oneHopService, IPairingStrategy pairingStrategy)
        {
            _deviceService = deviceService;
            _logger = logger;
            _oneHopService = oneHopService;
            _pairingStrategy = pairingStrategy;
        }

        [HttpPost("/vault/pair")]
        [HttpPost("/pair")]
        [HttpPost("/mg/pair")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("pair")]
        public ActionResult<PairResponse> Pair([FromBody] PairRequest request)
        {
            if (string.IsNullOrEmpty(request?.PairCode))
            {
                return BadRequest(new { error = "配对码不能为空" });
            }

            if (!_deviceService.ValidatePairCode(request.PairCode))
            {
                return BadRequest(new { error = "配对码错误" });
            }

            var deviceName = request.DeviceName ?? "未知设备";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            var existingDevice = _deviceService.GetAuthorizedDeviceByName(deviceName);
            if (existingDevice != null)
            {
                _logger.LogInformation("已授权设备重新配对: {DeviceName}", deviceName);
                return Ok(new PairResponse
                {
                    AccessToken = existingDevice.AccessToken,
                    ExpiresIn = 3600 * 24 * 365,
                    Status = "authorized",
                    Message = "设备已授权"
                });
            }

            return _pairingStrategy.Pair(deviceName, ipAddress, request.PairCode);
        }

        [HttpGet("/vault/pair/code")]
        [HttpGet("/pair/code")]
        [HttpGet("/mg/pair/code")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("pair")]
        public IActionResult GetPairCode()
        {
            var code = _deviceService.GetPairCode();
            return Ok(new { pairCode = code, deviceId = _oneHopService.DeviceId });
        }

        [HttpPost("/vault/pair/code/refresh")]
        [HttpPost("/pair/code/refresh")]
        [HttpPost("/mg/pair/code/refresh")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("pair")]
        public IActionResult RefreshPairCode()
        {
            var newCode = _deviceService.RefreshPairCode();
            return Ok(new { pairCode = newCode, message = "配对码已刷新" });
        }
    }
}