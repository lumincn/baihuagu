namespace TaskRunner.Contracts.OneHop;

public class OneHopDeviceResponse
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public int SignalStrength { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public Dictionary<string, string> ExtraData { get; set; } = new();
}

public class OneHopConnectionResponse
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime ConnectedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OneHopConnectRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

public class OneHopStatusResponse
{
    public bool IsAvailable { get; set; }
    public bool IsRunning { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public int Port { get; set; }
    public int DiscoveredDevicesCount { get; set; }
    public OneHopConnectionResponse? CurrentConnection { get; set; }
}

public class OneHopRegisterDeviceRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
}