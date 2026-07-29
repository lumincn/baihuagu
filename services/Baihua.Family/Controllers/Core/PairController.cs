using Baihua.Core;
using Baihua.Core.Localization;
using Baihua.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baihua.Data;
using Baihua.Family.Services;
using Baihua.Family.Services.Strategies;
using Baihua.Contracts.Vaults;
using Baihua.Contracts.Pairing;

namespace Baihua.Family.Controllers
{
    [ApiController]
    public class PairController : ControllerBase
    {
        private readonly DeviceService _deviceService;
        private readonly ILogger<PairController> _logger;
        private readonly IOneHopService _oneHopService;
        private readonly IPairingStrategy _pairingStrategy;
        private readonly IStringLocalizer<SharedResources> _loc;

        public PairController(DeviceService deviceService, ILogger<PairController> logger, IOneHopService oneHopService, IPairingStrategy pairingStrategy, IStringLocalizer<SharedResources> loc)
        {
            _deviceService = deviceService;
            _logger = logger;
            _oneHopService = oneHopService;
            _pairingStrategy = pairingStrategy;
            _loc = loc;
        }

        [HttpPost("/vault/pair")]
        [HttpPost("/pair")]
        [HttpPost("/mg/pair")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("pair")]
        public ActionResult<PairResponse> Pair([FromBody] PairRequest request)
        {
            if (string.IsNullOrEmpty(request?.PairCode))
            {
                return BadRequest(new { error = _loc["Pair_CodeEmpty"] });
            }

            if (!_deviceService.ValidatePairCode(request.PairCode))
            {
                return BadRequest(new { error = _loc["Pair_CodeInvalid"] });
            }

            var deviceName = request.DeviceName ?? _loc["Pair_UnknownDevice"];
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
                    Message = _loc["Device_AlreadyAuthorized"]
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
            return Ok(new { pairCode = newCode, message = _loc["Pair_CodeRefreshed"] });
        }
    }
}