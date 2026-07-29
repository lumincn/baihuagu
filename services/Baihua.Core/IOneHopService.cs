using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TaskRunner.Services
{
    /// <summary>
    /// OneHop设备信息
    /// </summary>
    public class OneHopDeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string ServiceId { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public DateTime DiscoveredAt { get; set; }
        public Dictionary<string, string> ExtraData { get; set; } = new();
    }

    /// <summary>
    /// OneHop连接信息
    /// </summary>
    public class OneHopConnectionInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public DateTime ConnectedAt { get; set; }
        public ConnectionStatus Status { get; set; }
    }

    /// <summary>
    /// 连接状态
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    /// <summary>
    /// OneHop服务事件参数
    /// </summary>
    public class OneHopEventArgs : EventArgs
    {
        public OneHopDeviceInfo? Device { get; set; }
        public OneHopConnectionInfo? Connection { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
    }

    /// <summary>
    /// OneHop服务接口
    /// </summary>
    public interface IOneHopService
    {
        /// <summary>
        /// 服务端唯一设备标识
        /// </summary>
        string DeviceId { get; }

        /// <summary>
        /// 服务是否可用
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 服务是否正在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 当前连接的设备
        /// </summary>
        OneHopConnectionInfo? CurrentConnection { get; }

        /// <summary>
        /// 发现到的设备列表
        /// </summary>
        IReadOnlyList<OneHopDeviceInfo> DiscoveredDevices { get; }

        /// <summary>
        /// 设备发现事件
        /// </summary>
        event EventHandler<OneHopEventArgs> DeviceDiscovered;

        /// <summary>
        /// 设备丢失事件
        /// </summary>
        event EventHandler<OneHopEventArgs> DeviceLost;

        /// <summary>
        /// 连接建立事件
        /// </summary>
        event EventHandler<OneHopEventArgs> ConnectionEstablished;

        /// <summary>
        /// 连接断开事件
        /// </summary>
        event EventHandler<OneHopEventArgs> ConnectionLost;

        /// <summary>
        /// 初始化OneHop服务
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 启动服务发布
        /// </summary>
        /// <param name="serviceId">服务ID</param>
        /// <param name="serviceData">服务数据（包含端口等信息）</param>
        Task<bool> StartServiceAsync(string serviceId, Dictionary<string, string> serviceData);

        /// <summary>
        /// 停止服务发布
        /// </summary>
        Task StopServiceAsync();

        /// <summary>
        /// 开始发现附近的设备
        /// </summary>
        Task StartDiscoveryAsync();

        /// <summary>
        /// 停止发现设备
        /// </summary>
        Task StopDiscoveryAsync();

        /// <summary>
        /// 连接到指定设备
        /// </summary>
        Task<OneHopConnectionInfo?> ConnectToDeviceAsync(string deviceId);

        /// <summary>
        /// 断开当前连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 发送数据到已连接的设备
        /// </summary>
        Task<bool> SendDataAsync(byte[] data);

        /// <summary>
        /// 发送文本到已连接的设备
        /// </summary>
        Task<bool> SendTextAsync(string text);

        /// <summary>
        /// 设置接收数据回调
        /// </summary>
        void SetDataReceivedCallback(Action<byte[], string> callback);

        /// <summary>
        /// 设置接收文本回调
        /// </summary>
        void SetTextReceivedCallback(Action<string, string> callback);

        /// <summary>
        /// 注册发现的设备（通过HTTP或其他方式）
        /// </summary>
        void RegisterDevice(string deviceId, string deviceName, string ipAddress, string? deviceType = null);
    }
}