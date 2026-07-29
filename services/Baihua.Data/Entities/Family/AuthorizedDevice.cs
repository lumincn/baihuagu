namespace TaskRunner.Data.Entities;

/// <summary>
/// 已授权设备（存储在 SQLite 中）
/// </summary>
public class AuthorizedDevice
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string Status { get; set; } = "Authorized"; // Authorized, Revoked
    public string? IpAddress { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public DateTime AuthorizedTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 同步次数统计
    /// </summary>
    public int SyncCount { get; set; }

    /// <summary>
    /// 首次同步时间
    /// </summary>
    public DateTime? FirstSyncTime { get; set; }
}
