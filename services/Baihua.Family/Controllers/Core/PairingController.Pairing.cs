using Microsoft.AspNetCore.Mvc;
using Baihua.Family.Services;
using Baihua.Contracts.Pairing;

namespace Baihua.Family.Controllers;

public partial class PairingController : ControllerBase
{
    private IActionResult HandleGetQRCode()
    {
        var (url, hostName) = _serverAddressService.GetQrCodeAddresses();
        
        var (baseUrl, name, qrCodeData) = _pairingService.GenerateQRCodeContent(
            url, hostName, _oneHopService.DeviceId);
        
        return Ok(new ServerQRResponse
        {
            Url = baseUrl,
            HostName = name,
            ServerId = _oneHopService.DeviceId,
            DeviceId = _oneHopService.DeviceId,
            QrCodeData = qrCodeData
        });
    }
}
