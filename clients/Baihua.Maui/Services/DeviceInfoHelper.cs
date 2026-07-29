namespace MobileApp.Maui.Services;

/// <summary>
/// 设备信息辅助类。跨平台获取设备 ID 和名称。
/// ID 在首次初始化时从安全存储读取或生成，后续从内存缓存返回。
/// </summary>
public static class DeviceInfoHelper
{
    private const string DeviceIdKey = "persistent_device_id";
    private static string? _cachedDeviceId;

    /// <summary>
    /// 异步初始化设备 ID。应在 App 启动早期调用一次。
    /// </summary>
    public static async Task InitializeAsync()
    {
        if (_cachedDeviceId != null)
            return;

        var stored = await SecureStorage.Default.GetAsync(DeviceIdKey).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(stored))
        {
            _cachedDeviceId = stored;
            return;
        }

        var newId = Guid.NewGuid().ToString("N")[..16];
        await SecureStorage.Default.SetAsync(DeviceIdKey, newId).ConfigureAwait(false);
        _cachedDeviceId = newId;
    }

    /// <summary>
    /// 获取设备 ID。首次调用时会自动完成初始化（同步等待）。
    /// </summary>
    public static string GetDeviceId()
    {
        if (_cachedDeviceId == null)
        {
            // 在 MauiProgram 中改为后台延迟初始化；若仍有同步调用场景，
            // 通过 Task.Run 在线程池上执行以避免 UI SynchronizationContext 死锁。
            Task.Run(() => InitializeAsync()).GetAwaiter().GetResult();
        }

        if (_cachedDeviceId == null)
            throw new InvalidOperationException("DeviceInfoHelper 初始化失败，无法获取设备 ID。");

        return _cachedDeviceId;
    }

    public static string GetDeviceName()
    {
#if ANDROID
        return Android.OS.Build.Model ?? "Android Device";
#elif IOS
        return UIKit.UIDevice.CurrentDevice.Name ?? "iPhone";
#else
        return "Unknown Device";
#endif
    }
}
