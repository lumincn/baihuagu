using Baihua.Core;
using Baihua.Core.Localization;
using Baihua.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

public partial class OneHopController
{
        [HttpPost("register-device")]
        public ActionResult RegisterDevice([FromBody] OneHopRegisterDeviceRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.DeviceId))
                {
                    return BadRequest(new { error = _loc["OneHop_DeviceIdRequired"] });
                }

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var deviceName = string.IsNullOrEmpty(request.DeviceName) ? _loc["OneHop_DefaultDeviceName"] : request.DeviceName;
                var deviceType = string.IsNullOrEmpty(request.DeviceType) ? null : request.DeviceType;

                _oneHopManager.RegisterDevice(request.DeviceId, deviceName, ipAddress, deviceType);

                // 检查设备是否已授权，已授权则不再创建待授权请求
                // 服务器名用 WebUI 配置的显示名（与 /health 一致），而非机器名，保证移动端显示统一
                var serverName = string.IsNullOrWhiteSpace(_serverAddressService.GetSettings().DisplayName)
                    ? Environment.MachineName
                    : _serverAddressService.GetSettings().DisplayName;

                // 安全验证：优先通过 deviceId 查找授权设备，防止 deviceName 碰撞攻击
                var authorizedDevice = _deviceService.GetAuthorizedDeviceById(request.DeviceId);
                if (authorizedDevice != null)
                {
                    return Ok(new
                    {
                        message = _loc["OneHop_DeviceAuthorized"],
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

                // 已撤销设备重注册：一律走人工授权流程（不再自动恢复）
                var anyNameDevice = _deviceService.GetDeviceByNameAnyStatus(deviceName);
                if (anyNameDevice != null && anyNameDevice.Status == DeviceStatus.Revoked)
                {
                    _logger.LogInformation("Revoked device re-registering, creating pending request: {DeviceName} {DeviceId}", deviceName, request.DeviceId);
                    var pairRequest2 = _deviceService.SubmitLanDiscoveryRequest(deviceName, ipAddress, request.DeviceId, request.SystemDeviceName);
                    return Ok(new { message = _loc["OneHop_DeviceIdChangedReauthorize"], deviceId = request.DeviceId, deviceName, serverName, ipAddress, requestId = pairRequest2.RequestId, authorized = false });
                }

                // 自动创建局域网发现待授权请求（无需扫码）
                _logger.LogInformation("[AUTH-DIAG] Creating pending request for device: {DeviceName} ({DeviceId})",
                    deviceName, request.DeviceId);
                var pairRequest = _deviceService.SubmitLanDiscoveryRequest(deviceName, ipAddress, request.DeviceId, request.SystemDeviceName);
                _logger.LogInformation("[AUTH-DIAG] Pending request created: RequestId={RequestId}",
                    pairRequest.RequestId);

                // 自动授权模式：跳过等待，直接批准设备
                _logger.LogInformation("[AUTH-DIAG] AutoAuthorizeEnabled={Enabled}, deviceId={DeviceId}, deviceName={DeviceName}",
                    _deviceService.AutoAuthorizeEnabled, request.DeviceId, deviceName);

                if (_deviceService.AutoAuthorizeEnabled)
                {
                    _logger.LogInformation("[AUTH-DIAG] Auto-authorizing device: {DeviceName} ({DeviceId}) @ {IpAddress}",
                        deviceName, request.DeviceId, ipAddress);
                    var (success, accessToken, error) = _deviceService.AutoAuthorizeDevice(deviceName, ipAddress, request.DeviceId, request.SystemDeviceName);
                    _logger.LogInformation("[AUTH-DIAG] AutoAuthorize result: Success={Success}, AccessToken={AccessToken}, Error={Error}",
                        success, accessToken, error);
                    if (success)
                    {
                        // 清理刚创建的 pending 请求
                        _logger.LogInformation("[AUTH-DIAG] Cleaning pending request: {RequestId}", pairRequest.RequestId);
                        _deviceService.RejectRequest(pairRequest.RequestId);
                        _logger.LogInformation("[AUTH-DIAG] Returning authorized: DeviceId={DeviceId}, AccessToken={AccessToken}",
                            request.DeviceId, accessToken);
                        return Ok(new
                        {
                            message = _loc["OneHop_DeviceAuthorized"],
                            deviceId = request.DeviceId,
                            deviceName = deviceName,
                            serverName = serverName,
                            ipAddress = ipAddress,
                            requestId = request.DeviceId,
                            authorized = true,
                            accessToken = accessToken,
                            sharedSecret = _signatureService.GetSharedSecret()
                        });
                    }
                    _logger.LogWarning("Auto-authorize failed for {DeviceName}: {Error}", deviceName, error);
                }

                return Ok(new
                {
                    message = _loc["OneHop_DeviceRegistered"],
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
