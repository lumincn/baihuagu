using Baihua.Core;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers
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
}
