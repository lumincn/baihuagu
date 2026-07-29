using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class OneHopController
{
        [HttpPost("register-device")]
        public ActionResult RegisterDevice([FromBody] OneHopRegisterDeviceRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.DeviceId))
                {
                    return BadRequest(new { error = "DeviceId is required" });
                }

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var deviceName = string.IsNullOrEmpty(request.DeviceName) ? "移动端设备" : request.DeviceName;
                var deviceType = string.IsNullOrEmpty(request.DeviceType) ? null : request.DeviceType;

                _oneHopManager.RegisterDevice(request.DeviceId, deviceName, ipAddress, deviceType);

                // 检查设备是否已授权，已授权则不再创建待授权请求
                var serverName = Environment.MachineName;

                // 安全验证：优先通过 deviceId 查找授权设备，防止 deviceName 碰撞攻击
                var authorizedDevice = _deviceService.GetAuthorizedDeviceById(request.DeviceId);
                if (authorizedDevice != null)
                {
                    return Ok(new
                    {
                        message = "设备已授权",
                        deviceId = request.DeviceId,
                        deviceName = deviceName,
                        serverName = serverName,
                        ipAddress = ipAddress,
                        requestId = authorizedDevice.DeviceId,
                        authorized = true,
                        accessToken = authorizedDevice.AccessToken,
                        sharedSecret = _signatureService.GetSharedSecret()
                    });
                }

                // 兼容旧设备：通过 deviceName 查找
                var deviceByName = _deviceService.GetAuthorizedDeviceByName(deviceName);
                if (deviceByName != null)
                {
                    // 旧数据兼容：如果数据库中 DeviceId 为空，允许恢复并更新 DeviceId
                    if (string.IsNullOrEmpty(deviceByName.DeviceId))
                    {
                        _logger.LogInformation("Legacy device recovery: name={DeviceName} updating DeviceId from empty to {RequestId}",
                            deviceName, request.DeviceId);
                        _deviceService.UpdateDeviceId(deviceByName.DeviceId ?? "", request.DeviceId, deviceName);
                        return Ok(new
                        {
                            message = "设备已授权（已更新设备标识）",
                            deviceId = request.DeviceId,
                            deviceName = deviceName,
                            serverName = serverName,
                            ipAddress = ipAddress,
                            requestId = request.DeviceId,
                            authorized = true,
                            accessToken = deviceByName.AccessToken,
                            sharedSecret = _signatureService.GetSharedSecret()
                        });
                    }

                    // deviceName 匹配且已授权，但 deviceId 不匹配（手机重装/换设备后新ID）
                    // 自动更新 DeviceId 并放行，无需重新授权
                    _logger.LogInformation("Device ID updated for authorized device: name={DeviceName} oldId={OldId} newId={NewId}",
                        deviceName, deviceByName.DeviceId, request.DeviceId);
                    _deviceService.UpdateDeviceId(deviceByName.DeviceId, request.DeviceId, deviceName);
                    return Ok(new
                    {
                        message = "设备已授权（已更新设备标识）",
                        deviceId = request.DeviceId,
                        deviceName = deviceName,
                        serverName = serverName,
                        ipAddress = ipAddress,
                        requestId = request.DeviceId,
                        authorized = true,
                        accessToken = deviceByName.AccessToken,
                        sharedSecret = _signatureService.GetSharedSecret()
                    });
                }

                // 检查是否有同名的已撤销设备：未被明确撤销过的设备不应出现在待授权列表
                // 如果设备之前被撤销（用户主动取消授权），仍需重新授权
                var anyNameDevice = _deviceService.GetDeviceByNameAnyStatus(deviceName);
                if (anyNameDevice != null && anyNameDevice.Status == DeviceStatus.Revoked)
                {
                    _logger.LogWarning("Revoked device re-registering: name={DeviceName} oldId={OldId} newId={NewId}, creating new pair request",
                        deviceName, anyNameDevice.DeviceId, request.DeviceId);
                    var pairRequest2 = _deviceService.SubmitLanDiscoveryRequest(deviceName, ipAddress, request.DeviceId);
                    return Ok(new
                    {
                        message = "设备标识已变更，请在 WebUI 中重新授权",
                        deviceId = request.DeviceId,
                        deviceName = deviceName,
                        serverName = serverName,
                        ipAddress = ipAddress,
                        requestId = pairRequest2.RequestId,
                        authorized = false
                    });
                }

                // 自动创建局域网发现待授权请求（无需扫码）
                var pairRequest = _deviceService.SubmitLanDiscoveryRequest(deviceName, ipAddress, request.DeviceId);

                return Ok(new
                {
                    message = "设备已注册，请在 WebUI 中授权",
                    deviceId = request.DeviceId,
                    deviceName = deviceName,
                    serverName = serverName,
                    ipAddress = ipAddress,
                    requestId = pairRequest.RequestId,
                    authorized = false,
                    accessToken = (string?)null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering device");
                return StatusCode(500, new { error = "Failed to register device", message = ex.Message });
            }
        }
}
