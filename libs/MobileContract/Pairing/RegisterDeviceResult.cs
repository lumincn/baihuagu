namespace MobileContract.Pairing;

/// <summary>
/// 移动设备通过 HTTP 注册到服务器的结果。
/// 与 Kotlin DeviceRegistrationService / ArkTS ServerRegistrationHelper 对齐。
/// </summary>
public record RegisterDeviceResult
{
    public bool Success { get; init; }

    public bool Authorized { get; init; }

    public string? SharedSecret { get; init; }

    public string? RequestId { get; init; }

    public string? DeviceName { get; init; }

    public string? AccessToken { get; init; }

    /// <summary>失败时的可读错误信息（调试用）</summary>
    public string? ErrorMessage { get; init; }
}
