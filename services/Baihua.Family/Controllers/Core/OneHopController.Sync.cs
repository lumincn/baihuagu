using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class OneHopController
{
        [HttpPost("sync")]
        public async Task<ActionResult> SendSyncRequest([FromQuery] string? vaultId, [FromQuery] long since = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(vaultId))
                {
                    return BadRequest(new { error = "必须指定知识库" });
                }

                var success = await _oneHopManager.SendSyncRequestAsync(vaultId, since);
                if (!success)
                {
                    return BadRequest(new { error = "No active connection or failed to send sync request" });
                }

                return Ok(new { 
                    message = "Sync request sent", 
                    vaultId = vaultId,
                    since = since 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending sync request");
                return StatusCode(500, new { error = "Failed to send sync request", message = ex.Message });
            }
        }

        /// <summary>
        /// 获取OneHop服务信息（用于二维码生成）
        /// </summary>
        [HttpGet("info")]
        public IActionResult GetServiceInfo()
        {
            try
            {
                // 获取本机IP地址
                var ipAddress = GetLocalIpAddress();
                var hostName = Environment.MachineName;
                var httpUrl = $"http://{ipAddress}:8788";
                var httpsUrl = $"https://{ipAddress}:8792";

                return Ok(new
                {
                    serviceId = "com.lumin.huaji.sync",
                    deviceName = hostName,
                    ipAddress = ipAddress,
                    port = 8788,
                    httpUrl = httpUrl,
                    httpsUrl = httpsUrl,
                    oneHopEnabled = _oneHopService.IsAvailable && _oneHopService.IsRunning,
                    supportedProtocols = new[] { "onehop", "http", "https" },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OneHop service info");
                return StatusCode(500, new { error = "Failed to get service info", message = ex.Message });
            }
        }

        /// <summary>
        /// 获取增强的二维码数据（包含OneHop信息）
        /// </summary>
        [HttpGet("qrcode")]
        public IActionResult GetEnhancedQrCode()
        {
            try
            {
                // 获取本机IP地址
                var ipAddress = GetLocalIpAddress();
                var hostName = Environment.MachineName;
                var httpUrl = $"http://{ipAddress}:8788";
                var httpsUrl = $"https://{ipAddress}:8792";

                var qrData = new
                {
                    // 传统HTTP信息
                    httpUrl = httpUrl,
                    httpsUrl = httpsUrl,
                    hostName = hostName,
                    
                    // OneHop信息
                    oneHop = new
                    {
                        enabled = _oneHopService.IsAvailable && _oneHopService.IsRunning,
                        serviceId = "com.lumin.huaji.sync",
                        deviceId = Environment.MachineName,
                        deviceName = hostName,
                        ipAddress = ipAddress,
                        port = 8788,
                        capabilities = new[] { "file-sync", "manifest", "health-check" }
                    },
                    
                    // 时间戳
                    timestamp = DateTime.UtcNow.Ticks,
                    version = "2.0" // 二维码格式版本
                };

                return Ok(qrData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating enhanced QR code");
                // 回退到简单信息
                return Ok(new
                {
                    httpUrl = $"http://{GetLocalIpAddress()}:8788",
                    httpsUrl = $"https://{GetLocalIpAddress()}:8792",
                    hostName = Environment.MachineName
                });
            }
        }

}
