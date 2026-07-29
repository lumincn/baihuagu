using TaskRunner.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TaskRunner.Services;

namespace TaskRunner.Controllers
{
    /// <summary>
    /// 配对控制器 - 简化版，只提供二维码
    /// </summary>
    [ApiController]
    [Route("api/pairing")]
    public partial class PairingController : ControllerBase
    {
        private readonly PairingService _pairingService;
        private readonly ServerAddressService _serverAddressService;
        private readonly IOneHopService _oneHopService;
        private readonly ILogger<PairingController> _logger;

        public PairingController(
            PairingService pairingService, 
            ServerAddressService serverAddressService,
            IOneHopService oneHopService,
            ILogger<PairingController> logger)
        {
            _pairingService = pairingService;
            _serverAddressService = serverAddressService;
            _oneHopService = oneHopService;
            _logger = logger;
        }

        /// <summary>
        /// 生成二维码(WebUI调用)
        /// </summary>
        [HttpGet("qrcode")]
        public IActionResult GetQRCode()
            => HandleGetQRCode();
    }

    /// <summary>
    /// 服务发现控制器 - HTTP发现端点（mDNS的备选方案）
    /// 移动端可以通过HTTP直接查询此端点发现服务，不依赖mDNS多播
    /// </summary>
    [ApiController]
    [Route("api/discovery")]
    public class DiscoveryController : ControllerBase
    {
        private readonly ServerAddressService _serverAddressService;
        private readonly MDnsService _mdnsService;
        private readonly IOneHopService _oneHopService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscoveryController> _logger;

        public DiscoveryController(
            ServerAddressService serverAddressService,
            MDnsService mdnsService,
            IOneHopService oneHopService,
            IConfiguration configuration,
            ILogger<DiscoveryController> logger)
        {
            _serverAddressService = serverAddressService;
            _mdnsService = mdnsService;
            _oneHopService = oneHopService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 服务发现端点 - 移动端通过HTTP GET发现服务
        /// </summary>
        [HttpGet]
        public IActionResult Discover()
        {
            var (url, hostName) = _serverAddressService.GetQrCodeAddresses();
            
            // Determine actual HTTP port from configuration
            var configuredHttpUrl = _configuration["Kestrel:Endpoints:Http:Url"];
            int httpPort = 8788;
            if (!string.IsNullOrWhiteSpace(configuredHttpUrl) && 
                Uri.TryCreate(configuredHttpUrl, UriKind.Absolute, out var uri))
            {
                httpPort = uri.Port;
            }
            else if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri2))
            {
                httpPort = uri2.Port;
            }

            // Extract server IP from url
            string serverIp = "";
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri3))
            {
                serverIp = uri3.Host;
            }

            return Ok(new
            {
                serviceName = "baihua",
                serviceType = "_http._tcp.local",
                serviceId = "com.lumin.huaji.sync",
                serverId = _oneHopService.DeviceId,
                url = url,
                hostName = hostName,
                serverIp = serverIp,
                port = httpPort,
                oneHopPort = httpPort + 1,
                oneHopEnabled = _oneHopService.IsAvailable && _oneHopService.IsRunning,
                deviceId = _oneHopService.DeviceId,
                mdnsRunning = _mdnsService.IsRunning,
                timestamp = DateTime.UtcNow.ToString("o")
            });
        }

        /// <summary>
        /// 移动端局域网扫描兼容端点（/mg/discovery）
        /// 移动端后台扫描会探测此路径，返回与 /api/discovery 相同的内容
        /// 但字段名兼容移动端 LanScanUtils 的期望（baseUrl / httpUrl）
        /// </summary>
        [HttpGet("/mg/discovery")]
        public IActionResult DiscoverMobile()
        {
            var (url, hostName) = _serverAddressService.GetQrCodeAddresses();
            
            var configuredHttpUrl = _configuration["Kestrel:Endpoints:Http:Url"];
            int httpPort = 8788;
            if (!string.IsNullOrWhiteSpace(configuredHttpUrl) && 
                Uri.TryCreate(configuredHttpUrl, UriKind.Absolute, out var uri))
            {
                httpPort = uri.Port;
            }
            else if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri2))
            {
                httpPort = uri2.Port;
            }

            string serverIp = "";
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri3))
            {
                serverIp = uri3.Host;
            }

            return Ok(new
            {
                serviceId = "com.lumin.huaji.sync",
                serverId = _oneHopService.DeviceId,
                baseUrl = url,
                httpUrl = url,
                hostName = hostName,
                serverIp = serverIp,
                port = httpPort,
                oneHopPort = httpPort + 1,
                oneHopEnabled = _oneHopService.IsAvailable && _oneHopService.IsRunning,
                deviceId = _oneHopService.DeviceId,
                timestamp = DateTime.UtcNow.ToString("o")
            });
        }
    }
}
