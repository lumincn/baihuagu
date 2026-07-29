using TaskRunner.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;
using TaskRunner.Core.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using TaskRunner.Contracts.Devices;

namespace TaskRunner.Controllers
{
    [ApiController]
    [Route("api/devices")]
    [Route("mg/devices")]
    public partial class DevicesController : ControllerBase
    {
        private readonly DeviceService _deviceService;
        private readonly IHubContext<DeviceHub> _hubContext;
        private readonly Services.VaultSettingsService _vaultSettings;
        private readonly ILogger<DevicesController> _logger;

        public DevicesController(DeviceService deviceService, IHubContext<DeviceHub> hubContext, Services.VaultSettingsService vaultSettings, ILogger<DevicesController> logger)
        {
            _deviceService = deviceService;
            _hubContext = hubContext;
            _vaultSettings = vaultSettings;
            _logger = logger;
        }

        [HttpGet("pending")]
        public ActionResult<List<PendingDeviceDto>> GetPendingDevices()
        {
            var pendingRequests = _deviceService.GetPendingRequests();
            var dtos = pendingRequests.Select(r => new PendingDeviceDto
            {
                RequestId = r.RequestId,
                DeviceName = r.DeviceName,
                RequestTime = r.RequestTime,
                IpAddress = r.IpAddress
            }).ToList();
            return Ok(dtos);
        }

        [HttpGet("authorized")]
        public ActionResult<List<AuthorizedDeviceDto>> GetAuthorizedDevices()
        {
            var devices = _deviceService.GetAuthorizedDevices();
            var dtos = devices.Select(d => new AuthorizedDeviceDto
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                AuthorizedTime = d.AuthorizedTime,
                LastSyncTime = d.LastSyncTime,
                IpAddress = d.IpAddress,
                SyncCount = d.SyncCount,
                FirstSyncTime = d.FirstSyncTime,
                SyncedVaultIds = d.SyncedVaultIds,
                SyncedVaultNames = d.SyncedVaultNames
            }).ToList();
            return Ok(dtos);
        }

        [HttpGet]
        public ActionResult<List<DeviceDto>> GetAllDevices()
        {
            var devices = _deviceService.GetAllDevices();
            var dtos = devices.Select(d => new DeviceDto
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                Status = d.Status.ToString(),
                FirstRequestTime = d.FirstRequestTime,
                AuthorizedTime = d.AuthorizedTime,
                LastSyncTime = d.LastSyncTime,
                IpAddress = d.IpAddress,
                SyncCount = d.SyncCount,
                FirstSyncTime = d.FirstSyncTime
            }).ToList();
            return Ok(dtos);
        }

        [HttpPost("authorize")]
        public IActionResult AuthorizeDevice([FromBody] AuthorizeDeviceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RequestId))
                return BadRequest(new { error = "请求ID不能为空" });

            var (success, accessToken, error) = _deviceService.AuthorizeDevice(request.RequestId);
            if (!success)
                return BadRequest(new { error });

            _logger.LogInformation("设备已授权，请求ID: {RequestId}", request.RequestId);
            return Ok(new { success = true, message = "设备已授权", accessToken });
        }

        [HttpPost("reject")]
        public IActionResult RejectDevice([FromBody] RejectDeviceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RequestId))
                return BadRequest(new { error = "请求ID不能为空" });

            var success = _deviceService.RejectRequest(request.RequestId);
            if (!success)
                return BadRequest(new { error = "请求不存在或已处理" });

            _logger.LogInformation("设备配对已拒绝，请求ID: {RequestId}", request.RequestId);
            return Ok(new { success = true, message = "已拒绝设备配对" });
        }

        [HttpPost("revoke")]
        public IActionResult RevokeDevice([FromBody] RevokeDeviceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.DeviceId))
                return BadRequest(new { error = "设备ID不能为空" });

            var success = _deviceService.RevokeDevice(request.DeviceId);
            if (!success)
                return BadRequest(new { error = "设备不存在" });

            _logger.LogInformation("设备授权已撤销，设备ID: {DeviceId}", request.DeviceId);
            return Ok(new { success = true, message = "已撤销设备授权" });
        }

        [HttpGet("stats")]
        public ActionResult<MobileStats> GetMobileStats()
        {
            var stats = _deviceService.GetMobileStats();
            return Ok(stats);
        }
    }
}
