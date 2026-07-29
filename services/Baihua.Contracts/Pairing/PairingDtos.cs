namespace TaskRunner.Contracts.Pairing;

public class ServerQRResponse
{
    public string Url { get; set; } = "";
    public string HostName { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string QrCodeData { get; set; } = "";
}

public class AiKeyQRResponse
{
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string ApiKey { get; set; } = "";
}