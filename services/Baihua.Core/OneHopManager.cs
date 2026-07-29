using TaskRunner.Core.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Services
{
    /// <summary>
    /// OneHop服务管理器
    /// 管理OneHop服务的启动、停止和设备连接
    /// </summary>
    public class OneHopManager : IHostedService, IDisposable
    {
        private readonly ILogger<OneHopManager> _logger;
        private readonly IOneHopService _oneHopService;
        private readonly ServerAddressService _serverAddressService;
        private bool _isInitialized = false;
        private string _serviceId = "com.lumin.huaji.sync";

        public OneHopManager(
            ILogger<OneHopManager> logger,
            IOneHopService oneHopService,
            ServerAddressService serverAddressService)
        {
            _logger = logger;
            _oneHopService = oneHopService;
            _serverAddressService = serverAddressService;

            // 注册事件处理
            _oneHopService.DeviceDiscovered += OnDeviceDiscovered;
            _oneHopService.DeviceLost += OnDeviceLost;
            _oneHopService.ConnectionEstablished += OnConnectionEstablished;
            _oneHopService.ConnectionLost += OnConnectionLost;
        }

        /// <summary>
        /// 启动OneHop服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting OneHop manager...");

                // 初始化OneHop服务
                var initialized = await _oneHopService.InitializeAsync();
                if (!initialized)
                {
                    _logger.LogWarning("OneHop service initialization failed. OneHop features will be disabled.");
                    return;
                }

                _isInitialized = true;

                // 获取服务器地址信息
                var (httpUrl, hostName) = _serverAddressService.GetQrCodeAddresses();
                var ipAddress = ExtractIpFromUrl(httpUrl);
                var port = ExtractPortFromUrl(httpUrl);

                // 准备服务数据
                var serviceData = new Dictionary<string, string>
                {
                    { "deviceId", _oneHopService.DeviceId },
                    { "deviceName", hostName },
                    { "ipAddress", ipAddress },
                    { "httpPort", port.ToString() },
                    { "serviceType", "baihua" },
                    { "version", "1.0.0" },
                    { "capabilities", "file-sync,manifest,health-check" }
                };

                // 启动服务发布
                var started = await _oneHopService.StartServiceAsync(_serviceId, serviceData);
                if (started)
                {
                    _logger.LogInformation("OneHop service started successfully. ServiceId: {ServiceId}", _serviceId);
                    
                    // 开始设备发现
                    await _oneHopService.StartDiscoveryAsync();
                    _logger.LogInformation("Device discovery started");
                }
                else
                {
                    _logger.LogError("Failed to start OneHop service");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting OneHop manager");
            }
        }

        /// <summary>
        /// 停止OneHop服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_isInitialized)
                {
                    _logger.LogInformation("Stopping OneHop manager...");
                    
                    await _oneHopService.StopDiscoveryAsync();
                    await _oneHopService.StopServiceAsync();
                    
                    _logger.LogInformation("OneHop manager stopped");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping OneHop manager");
            }
        }

        /// <summary>
        /// 获取当前连接状态
        /// </summary>
        public OneHopConnectionInfo? GetConnectionStatus()
        {
            return _oneHopService.CurrentConnection;
        }

        /// <summary>
        /// 获取发现的设备列表
        /// </summary>
        public IReadOnlyList<OneHopDeviceInfo> GetDiscoveredDevices()
        {
            return _oneHopService.DiscoveredDevices;
        }

        /// <summary>
        /// 注册发现的设备（供移动端通过HTTP注册）
        /// </summary>
        public void RegisterDevice(string deviceId, string deviceName, string ipAddress, string? deviceType = null)
        {
            _oneHopService.RegisterDevice(deviceId, deviceName, ipAddress, deviceType);
            _logger.LogInformation("Device registered via HTTP: {DeviceName} ({DeviceId}) type={DeviceType} from {IpAddress}",
                deviceName, deviceId, deviceType ?? "unknown", ipAddress);
        }

        /// <summary>
        /// 连接到指定设备
        /// </summary>
        public async Task<OneHopConnectionInfo?> ConnectToDeviceAsync(string deviceId)
        {
            try
            {
                _logger.LogInformation("Attempting to connect to device: {DeviceId}", deviceId);
                return await _oneHopService.ConnectToDeviceAsync(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to device: {DeviceId}", deviceId);
                return null;
            }
        }

        /// <summary>
        /// 断开当前连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                await _oneHopService.DisconnectAsync();
                _logger.LogInformation("Disconnected from device");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from device");
            }
        }

        /// <summary>
        /// 发送同步请求到已连接的设备
        /// </summary>
        public async Task<bool> SendSyncRequestAsync(string vaultId, long sinceCursor = 0)
        {
            if (_oneHopService.CurrentConnection == null)
            {
                _logger.LogWarning("No active connection to send sync request");
                return false;
            }

            try
            {
                var syncRequest = new
                {
                    type = "sync-request",
                    vaultId = vaultId,
                    sinceCursor = sinceCursor,
                    timestamp = DateTime.UtcNow.Ticks
                };

                var json = System.Text.Json.JsonSerializer.Serialize(syncRequest);
                return await _oneHopService.SendTextAsync(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send sync request");
                return false;
            }
        }

        #region 事件处理

        private void OnDeviceDiscovered(object? sender, OneHopEventArgs e)
        {
            if (e.Device != null)
            {
                _logger.LogInformation("Device discovered: {DeviceName} ({DeviceId}), Signal: {SignalStrength}%",
                    e.Device.DeviceName, e.Device.DeviceId, e.Device.SignalStrength);

                // 在这里可以添加自动连接逻辑
                // 例如：如果设备是已知的，自动连接
            }
        }

        private void OnDeviceLost(object? sender, OneHopEventArgs e)
        {
            if (e.Device != null)
            {
                _logger.LogInformation("Device lost: {DeviceName} ({DeviceId})",
                    e.Device.DeviceName, e.Device.DeviceId);
            }
        }

        private void OnConnectionEstablished(object? sender, OneHopEventArgs e)
        {
            if (e.Connection != null)
            {
                _logger.LogInformation("Connection established with {DeviceName} at {IpAddress}:{Port}",
                    e.Connection.DeviceName, e.Connection.IpAddress, e.Connection.Port);

                // 设置数据接收回调
                _oneHopService.SetTextReceivedCallback(OnTextReceived);
                _oneHopService.SetDataReceivedCallback(OnDataReceived);
            }
        }

        private void OnConnectionLost(object? sender, OneHopEventArgs e)
        {
            if (e.Connection != null)
            {
                _logger.LogInformation("Connection lost with {DeviceName}",
                    e.Connection.DeviceName);
            }
        }

        private void OnTextReceived(string text, string deviceId)
        {
            try
            {
                _logger.LogDebug("Received text from {DeviceId}: {Text}", deviceId, text);

                // 处理接收到的文本消息
                // 这里可以解析JSON消息并执行相应的操作
                // 例如：处理同步请求、文件传输等
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received text from {DeviceId}", deviceId);
            }
        }

        private void OnDataReceived(byte[] data, string deviceId)
        {
            try
            {
                _logger.LogDebug("Received {Length} bytes of data from {DeviceId}", data.Length, deviceId);

                // 处理接收到的二进制数据
                // 这里可以处理文件传输等
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received data from {DeviceId}", deviceId);
            }
        }

        #endregion

        #region 工具方法

        private string ExtractIpFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "127.0.0.1";
            
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                // 尝试从URL中提取IP地址
                var match = System.Text.RegularExpressions.Regex.Match(url, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
                return match.Success ? match.Value : "127.0.0.1";
            }
        }

        private int ExtractPortFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return 8788;
            
            try
            {
                var uri = new Uri(url);
                return uri.Port;
            }
            catch
            {
                return 8788;
            }
        }

        #endregion

        #region IDisposable实现

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 取消事件注册
                    if (_oneHopService != null)
                    {
                        _oneHopService.DeviceDiscovered -= OnDeviceDiscovered;
                        _oneHopService.DeviceLost -= OnDeviceLost;
                        _oneHopService.ConnectionEstablished -= OnConnectionEstablished;
                        _oneHopService.ConnectionLost -= OnConnectionLost;
                    }

                    // 停止服务
                    StopAsync(CancellationToken.None).Wait();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}