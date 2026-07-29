using TaskRunner.Services;
using MobileContract.Admin;
using MobileContract.OneHop;
using MCDeviceInfo = MobileContract.OneHop.OneHopDeviceInfo;
using MCConnectionResult = MobileContract.OneHop.OneHopConnectionResult;

namespace TaskRunner.Services.Adapters;

/// <summary>
/// OneHop 管理服务适配器 —— 将 TaskRunner.Services.IOneHopService 适配到 MobileContract.Admin.IOneHopAdminService。
/// 仅由服务端管理后台调用。
/// </summary>
public class OneHopAdminServiceAdapter : IOneHopAdminService
{
    private readonly IOneHopService _oneHopService;

    public OneHopAdminServiceAdapter(IOneHopService oneHopService)
    {
        _oneHopService = oneHopService;
    }

    public Task<OneHopStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = new OneHopStatus
        {
            IsRunning = _oneHopService.IsRunning,
            ServiceName = "OneHop",
            IpAddress = _oneHopService.CurrentConnection?.IpAddress,
            Port = _oneHopService.CurrentConnection?.Port ?? 0,
            SupportedProtocols = new[] { "TCP" }
        };
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MCDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = _oneHopService.DiscoveredDevices
            .Select(d => new MCDeviceInfo
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                IpAddress = d.ExtraData.TryGetValue("ip", out var ip) ? ip : null,
                Port = d.ExtraData.TryGetValue("port", out var port) && int.TryParse(port, out var p) ? p : null,
                LastSeen = d.DiscoveredAt != default ? new DateTimeOffset(d.DiscoveredAt) : null
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<MCDeviceInfo>>(result);
    }

    public async Task<MCConnectionResult> ConnectAsync(OneHopConnectRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await _oneHopService.ConnectToDeviceAsync(request.DeviceId);
        return connection != null
            ? new MCConnectionResult { Success = true, ConnectionId = connection.DeviceId }
            : new MCConnectionResult { Success = false, Message = "连接失败" };
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _oneHopService.DisconnectAsync();
        return true;
    }

    public async Task<bool> StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await _oneHopService.StartDiscoveryAsync();
        return true;
    }

    public async Task<bool> StopDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await _oneHopService.StopDiscoveryAsync();
        return true;
    }
}
