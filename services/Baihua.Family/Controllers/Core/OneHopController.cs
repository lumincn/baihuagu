using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

[ApiController]
[Route("api/onehop")]
[Route("mg/onehop")]
public partial class OneHopController : ControllerBase
{
    private readonly ILogger<OneHopController> _logger;
    private readonly OneHopManager _oneHopManager;
    private readonly IOneHopService _oneHopService;
    private readonly DeviceService _deviceService;
    private readonly RequestSignatureService _signatureService;

    public OneHopController(
        ILogger<OneHopController> logger,
        OneHopManager oneHopManager,
        IOneHopService oneHopService,
        DeviceService deviceService,
        RequestSignatureService signatureService)
    {
        _logger = logger;
        _oneHopManager = oneHopManager;
        _oneHopService = oneHopService;
        _deviceService = deviceService;
        _signatureService = signatureService;
    }

    [HttpGet("status")]
    public ActionResult<OneHopStatusResponse> GetStatus()
    {
        try
        {
            var connection = _oneHopManager.GetConnectionStatus();
            var devices = _oneHopManager.GetDiscoveredDevices();

            return Ok(new OneHopStatusResponse
            {
                IsAvailable = _oneHopService.IsAvailable,
                IsRunning = _oneHopService.IsRunning,
                ServiceId = "com.lumin.huaji.sync",
                Port = _oneHopService is OneHopService ohs ? ohs.Port : 0,
                DiscoveredDevicesCount = devices.Count,
                CurrentConnection = connection != null ? new OneHopConnectionResponse
                {
                    DeviceId = connection.DeviceId,
                    DeviceName = connection.DeviceName,
                    IpAddress = connection.IpAddress,
                    Port = connection.Port,
                    ConnectedAt = connection.ConnectedAt,
                    Status = connection.Status.ToString()
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OneHop status");
            return StatusCode(500, new { error = "Failed to get OneHop status", message = ex.Message });
        }
    }

    [HttpGet("devices")]
    public ActionResult<IEnumerable<OneHopDeviceResponse>> GetDevices()
    {
        try
        {
            var devices = _oneHopManager.GetDiscoveredDevices();
            var response = devices.Select(d => new OneHopDeviceResponse
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                ServiceId = d.ServiceId,
                SignalStrength = d.SignalStrength,
                DiscoveredAt = d.DiscoveredAt,
                ExtraData = d.ExtraData
            }).ToList();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OneHop devices");
            return StatusCode(500, new { error = "Failed to get devices", message = ex.Message });
        }
    }

    [HttpPost("connect")]
    public async Task<ActionResult<OneHopConnectionResponse>> Connect([FromBody] OneHopConnectRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId))
                return BadRequest(new { error = "DeviceId is required" });

            var connection = await _oneHopManager.ConnectToDeviceAsync(request.DeviceId);
            if (connection == null)
                return NotFound(new { error = $"Device not found or connection failed: {request.DeviceId}" });

            return Ok(new OneHopConnectionResponse
            {
                DeviceId = connection.DeviceId,
                DeviceName = connection.DeviceName,
                IpAddress = connection.IpAddress,
                Port = connection.Port,
                ConnectedAt = connection.ConnectedAt,
                Status = connection.Status.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to device: {DeviceId}", request.DeviceId);
            return StatusCode(500, new { error = "Connection failed", message = ex.Message });
        }
    }

    [HttpPost("disconnect")]
    public async Task<ActionResult> Disconnect()
    {
        try
        {
            await _oneHopManager.DisconnectAsync();
            return Ok(new { message = "Disconnected successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting");
            return StatusCode(500, new { error = "Disconnect failed", message = ex.Message });
        }
    }

    [HttpPost("discovery/start")]
    public async Task<ActionResult> StartDiscovery()
    {
        try
        {
            await _oneHopService.StartDiscoveryAsync();
            return Ok(new { message = "Discovery started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting discovery");
            return StatusCode(500, new { error = "Failed to start discovery", message = ex.Message });
        }
    }

    [HttpPost("discovery/stop")]
    public async Task<ActionResult> StopDiscovery()
    {
        try
        {
            await _oneHopService.StopDiscoveryAsync();
            return Ok(new { message = "Discovery stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping discovery");
            return StatusCode(500, new { error = "Failed to stop discovery", message = ex.Message });
        }
    }
}
