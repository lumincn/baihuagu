using TaskRunner.Core.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Services
{
    /// <summary>
    /// 基于TCP的OneHop服务实现
    /// 提供局域网设备发现和连接功能，服务发现由mDNS负责
    /// </summary>
    public class OneHopService : IOneHopService, IDisposable
    {
        private readonly ILogger<OneHopService> _logger;
        private readonly ServerAddressService _serverAddressService;
        private readonly IConfiguration _configuration;
        private readonly DeviceService _deviceService;
        private readonly Random _random = new();
        private readonly ConcurrentDictionary<string, OneHopDeviceInfo> _discoveredDevices = new();
        private readonly ConcurrentDictionary<string, Timer> _deviceTimers = new();
        private Timer? _discoveryTimer;
        private TcpListener? _tcpListener;
        private TcpClient? _connectedClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;
        private int _servicePort = 0;
        private string _serviceId = "com.lumin.huaji.sync";
        private string _deviceId;
        private string _deviceName;
        private readonly bool _enableSimulatedDiscovery;

        public string DeviceId => _deviceId;
        public bool IsAvailable => true;
        public bool IsRunning { get; private set; }
        public int Port => _servicePort;
        public OneHopConnectionInfo? CurrentConnection { get; private set; }
        public IReadOnlyList<OneHopDeviceInfo> DiscoveredDevices => _discoveredDevices.Values.ToList();

        public event EventHandler<OneHopEventArgs>? DeviceDiscovered;
        public event EventHandler<OneHopEventArgs>? DeviceLost;
        public event EventHandler<OneHopEventArgs>? ConnectionEstablished;
        public event EventHandler<OneHopEventArgs>? ConnectionLost;

        private Action<byte[], string>? _dataReceivedCallback;
        private Action<string, string>? _textReceivedCallback;

        public OneHopService(
            ILogger<OneHopService> logger,
            ServerAddressService serverAddressService,
            IConfiguration configuration,
            DeviceService deviceService)
        {
            _logger = logger;
            _serverAddressService = serverAddressService;
            _configuration = configuration;
            _deviceService = deviceService;
            _deviceId = serverAddressService.GetServerInstanceId();
            _deviceName = Environment.MachineName;
            _enableSimulatedDiscovery = _configuration.GetValue<bool>("OneHop:EnableSimulatedDiscovery", false);
            _logger.LogInformation("OneHopService initialized. DeviceId: {DeviceId}, DeviceName: {DeviceName}, SimulatedDiscovery: {SimDiscovery}",
                _deviceId, _deviceName, _enableSimulatedDiscovery);
        }

        public Task<bool> InitializeAsync()
        {
            _logger.LogInformation("OneHopService initialized successfully");
            return Task.FromResult(true);
        }

        public async Task<bool> StartServiceAsync(string serviceId, Dictionary<string, string> serviceData)
        {
            try
            {
                _serviceId = serviceId;

                _servicePort = GetAvailablePort();
                _logger.LogInformation("Starting OneHop service on port {Port}", _servicePort);

                _tcpListener = new TcpListener(IPAddress.Any, _servicePort);
                _tcpListener.Start();
                _logger.LogInformation("TCP listener started on port {Port}", _servicePort);

                if (_enableSimulatedDiscovery)
                {
                    _discoveryTimer = new Timer(SimulateDeviceDiscovery, null, 0, 10000);
                    _logger.LogInformation("Simulated device discovery enabled");
                }

                IsRunning = true;
                _logger.LogInformation("OneHopService started. ServiceId: {ServiceId}, Port: {Port}",
                    serviceId, _servicePort);

                _ = Task.Run(AcceptConnectionsAsync);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start OneHopService");
                return false;
            }
        }

        public Task StopServiceAsync()
        {
            try
            {
                _logger.LogInformation("Stopping OneHopService");

                _discoveryTimer?.Dispose();
                _discoveryTimer = null;

                _tcpListener?.Stop();
                _tcpListener = null;

                DisconnectAsync().Wait();

                IsRunning = false;
                _logger.LogInformation("OneHopService stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping OneHopService");
            }

            return Task.CompletedTask;
        }

        public Task StartDiscoveryAsync()
        {
            _logger.LogInformation("Starting device discovery");
            if (_enableSimulatedDiscovery && _discoveryTimer == null)
            {
                _discoveryTimer = new Timer(SimulateDeviceDiscovery, null, 0, 10000);
            }
            return Task.CompletedTask;
        }

        public Task StopDiscoveryAsync()
        {
            _logger.LogInformation("Stopping device discovery");
            _discoveryTimer?.Dispose();
            _discoveryTimer = null;
            return Task.CompletedTask;
        }

        public async Task<OneHopConnectionInfo?> ConnectToDeviceAsync(string deviceId)
        {
            try
            {
                if (!_discoveredDevices.TryGetValue(deviceId, out var device))
                {
                    _logger.LogWarning("Device not found: {DeviceId}", deviceId);
                    return null;
                }

                _logger.LogInformation("Connecting to device: {DeviceName} ({DeviceId})",
                    device.DeviceName, deviceId);

                await Task.Delay(1000);

                var ipAddress = GetLocalIpAddress();

                CurrentConnection = new OneHopConnectionInfo
                {
                    DeviceId = deviceId,
                    DeviceName = device.DeviceName,
                    IpAddress = ipAddress,
                    Port = _servicePort,
                    ConnectedAt = DateTime.Now,
                    Status = ConnectionStatus.Connected
                };

                ConnectionEstablished?.Invoke(this, new OneHopEventArgs
                {
                    Device = device,
                    Connection = CurrentConnection,
                    Message = $"Connected to {device.DeviceName}"
                });

                _logger.LogInformation("Connected to device: {DeviceName}", device.DeviceName);
                return CurrentConnection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to device: {DeviceId}", deviceId);
                return null;
            }
        }

        public Task DisconnectAsync()
        {
            try
            {
                if (CurrentConnection != null)
                {
                    var deviceName = CurrentConnection.DeviceName;

                    _receiveCts?.Cancel();
                    _receiveTask?.Wait(1000);

                    _networkStream?.Close();
                    _connectedClient?.Close();

                    ConnectionLost?.Invoke(this, new OneHopEventArgs
                    {
                        Connection = CurrentConnection,
                        Message = $"Disconnected from {deviceName}"
                    });

                    _logger.LogInformation("Disconnected from device: {DeviceName}", deviceName);
                }

                CurrentConnection = null;
                _connectedClient = null;
                _networkStream = null;
                _receiveCts = null;
                _receiveTask = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting");
            }

            return Task.CompletedTask;
        }

        public Task<bool> SendDataAsync(byte[] data)
        {
            if (CurrentConnection == null || _networkStream == null)
            {
                _logger.LogWarning("No active connection to send data");
                return Task.FromResult(false);
            }

            try
            {
                _logger.LogDebug("Sending {Length} bytes of data", data.Length);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send data");
                return Task.FromResult(false);
            }
        }

        public Task<bool> SendTextAsync(string text)
        {
            if (CurrentConnection == null)
            {
                _logger.LogWarning("No active connection to send text");
                return Task.FromResult(false);
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(text);
                _logger.LogDebug("Sending text: {Text}", text);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send text");
                return Task.FromResult(false);
            }
        }

        public void SetDataReceivedCallback(Action<byte[], string> callback) { _dataReceivedCallback = callback; }
        public void SetTextReceivedCallback(Action<string, string> callback) { _textReceivedCallback = callback; }

        #region 私有方法

        private int GetAvailablePort()
        {
            int oneHopPort = 8792;

            try
            {
                var (httpUrl, _) = _serverAddressService.GetQrCodeAddresses();
                if (!string.IsNullOrEmpty(httpUrl) && Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
                {
                    oneHopPort = uri.Port + 1;
                    _logger.LogInformation("API port: {ApiPort}, OneHop port: {OneHopPort}", uri.Port, oneHopPort);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get port from ServerAddressService, using default: {Port}", oneHopPort);
            }

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, oneHopPort);
                listener.Start();
                listener.Stop();
                return oneHopPort;
            }
            catch (SocketException)
            {
                _logger.LogWarning("OneHop port {Port} is in use, falling back to dynamic port", oneHopPort);
            }

            for (int port = oneHopPort + 1; port < oneHopPort + 100; port++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (SocketException) { }
            }

            return oneHopPort;
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to get local IP address"); }
            return "127.0.0.1";
        }

        private void SimulateDeviceDiscovery(object? state)
        {
            try
            {
                if (!IsRunning) return;

                var simulatedDevices = new[]
                {
                    new { Id = "SIM-PHONE-001", Name = "华为手机" },
                    new { Id = "SIM-TABLET-001", Name = "华为平板" },
                    new { Id = "SIM-PC-001", Name = "另一台电脑" }
                };

                foreach (var device in simulatedDevices)
                {
                    if (!_discoveredDevices.ContainsKey(device.Id))
                    {
                        var deviceInfo = new OneHopDeviceInfo
                        {
                            DeviceId = device.Id,
                            DeviceName = device.Name,
                            ServiceId = _serviceId,
                            SignalStrength = _random.Next(70, 100),
                            DiscoveredAt = DateTime.Now,
                            ExtraData = new Dictionary<string, string>
                            {
                                { "type", "simulated" },
                                { "ip", $"192.168.1.{_random.Next(100, 200)}" }
                            }
                        };

                        _discoveredDevices[device.Id] = deviceInfo;

                        var timer = new Timer(RemoveDevice, device.Id, 30000, Timeout.Infinite);
                        _deviceTimers[device.Id] = timer;

                        DeviceDiscovered?.Invoke(this, new OneHopEventArgs
                        {
                            Device = deviceInfo,
                            Message = $"Discovered device: {device.Name}"
                        });
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error in device discovery simulation"); }
        }

        private void RemoveDevice(object? deviceIdObj)
        {
            if (deviceIdObj is string deviceId)
            {
                if (_discoveredDevices.TryRemove(deviceId, out var device))
                {
                    _deviceTimers.TryRemove(deviceId, out var timer);
                    timer?.Dispose();

                    DeviceLost?.Invoke(this, new OneHopEventArgs
                    {
                        Device = device,
                        Message = $"Device lost: {device.DeviceName}"
                    });
                }
            }
        }

        private async Task AcceptConnectionsAsync()
        {
            if (_tcpListener == null) return;

            try
            {
                while (IsRunning)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    _logger.LogInformation("Accepted connection from {RemoteEndpoint}", client.Client.RemoteEndPoint);
                    _ = Task.Run(() => HandleClientConnectionAsync(client));
                }
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException ||
                ex is InvalidOperationException ||
                (ex is System.Net.Sockets.SocketException sockEx && sockEx.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted))
            {
                _logger.LogInformation("TCP listener stopped");
            }
            catch (Exception ex) { _logger.LogError(ex, "Error accepting connections"); }
        }

        private async Task HandleClientConnectionAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        _logger.LogDebug("Received message: {Message}", message);

                        // Try parse device registration heartbeat from mobile client
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(message);
                            if (json != null && json.TryGetValue("type", out var msgType) && msgType == "device-register")
                            {
                                var deviceId = json.GetValueOrDefault("deviceId", $"MOBILE-{Guid.NewGuid():N}");
                                var deviceName = json.GetValueOrDefault("deviceName", "移动端设备");
                                var remoteEp = client.Client.RemoteEndPoint as System.Net.IPEndPoint;
                                var ipAddress = remoteEp?.Address.ToString() ?? "unknown";
                                RegisterDiscoveredDevice(deviceId, deviceName, ipAddress);

                                // 自动创建待授权请求（局域网发现模式，无需扫码）
                                // 已授权设备不再重复创建待授权请求
                                try
                                {
                                    var authorized = _deviceService?.GetAuthorizedDeviceByName(deviceName);
                                    if (authorized == null)
                                    {
                                        _deviceService?.SubmitLanDiscoveryRequest(deviceName, ipAddress, deviceId);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("设备 {DeviceName} 已授权，跳过重复创建待授权请求", deviceName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "自动创建局域网配对请求失败: {DeviceName}", deviceName);
                                }

                                // Echo back an acknowledgement
                                var ack = System.Text.Json.JsonSerializer.Serialize(new { type = "device-register-ack", success = true });
                                var ackBytes = Encoding.UTF8.GetBytes(ack);
                                await stream.WriteAsync(ackBytes, 0, ackBytes.Length);
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            _logger.LogDebug(jsonEx, "Failed to parse registration message");
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error handling client connection"); }
        }

        public void RegisterDevice(string deviceId, string deviceName, string ipAddress, string? deviceType = null)
        {
            RegisterDiscoveredDevice(deviceId, deviceName, ipAddress, deviceType);
        }

        private void RegisterDiscoveredDevice(string deviceId, string deviceName, string ipAddress, string? deviceType = null)
        {
            var effectiveType = string.IsNullOrEmpty(deviceType) ? "mobile" : deviceType;
            if (_discoveredDevices.TryGetValue(deviceId, out var existing))
            {
                // Refresh existing device
                existing.DiscoveredAt = DateTime.Now;
                existing.SignalStrength = _random.Next(70, 100);
                if (existing.ExtraData.ContainsKey("ip"))
                    existing.ExtraData["ip"] = ipAddress;
                else
                    existing.ExtraData.Add("ip", ipAddress);
                if (existing.ExtraData.ContainsKey("deviceType"))
                    existing.ExtraData["deviceType"] = effectiveType;
                else
                    existing.ExtraData.Add("deviceType", effectiveType);

                // Reset expiration timer
                if (_deviceTimers.TryRemove(deviceId, out var oldTimer))
                {
                    oldTimer?.Dispose();
                }
                var timer = new Timer(RemoveDevice, deviceId, 60000, Timeout.Infinite);
                _deviceTimers[deviceId] = timer;

                _logger.LogInformation("Refreshed discovered device: {DeviceName} ({DeviceId}) type={DeviceType} from {IpAddress}",
                    deviceName, deviceId, effectiveType, ipAddress);
            }
            else
            {
                var deviceInfo = new OneHopDeviceInfo
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    ServiceId = _serviceId,
                    SignalStrength = _random.Next(70, 100),
                    DiscoveredAt = DateTime.Now,
                    ExtraData = new Dictionary<string, string>
                    {
                        { "type", "mobile" },
                        { "deviceType", effectiveType },
                        { "ip", ipAddress }
                    }
                };

                _discoveredDevices[deviceId] = deviceInfo;

                var timer = new Timer(RemoveDevice, deviceId, 60000, Timeout.Infinite);
                _deviceTimers[deviceId] = timer;

                DeviceDiscovered?.Invoke(this, new OneHopEventArgs
                {
                    Device = deviceInfo,
                    Message = $"Discovered mobile device: {deviceName}"
                });

                _logger.LogInformation("Registered discovered device: {DeviceName} ({DeviceId}) type={DeviceType} from {IpAddress}",
                    deviceName, deviceId, effectiveType, ipAddress);
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
                    StopServiceAsync().Wait();
                    _discoveryTimer?.Dispose();
                    _tcpListener?.Stop();
                    _connectedClient?.Dispose();
                    _networkStream?.Dispose();
                    _receiveCts?.Dispose();

                    foreach (var timer in _deviceTimers.Values) { timer?.Dispose(); }
                    _deviceTimers.Clear();
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
