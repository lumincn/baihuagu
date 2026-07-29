using TaskRunner.Core.Shared;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Services
{
    /// <summary>
    /// mDNS服务实现
    /// Linux: 使用 avahi-publish-service (系统级mDNS，可靠处理多播)
    /// Windows/macOS: 使用 Makaretu.Dns.Multicast.New 库
    /// 遵循 RFC 6762/6763 标准，在局域网内发布 _http._tcp 服务
    /// 鸿蒙移动端通过 @ohos.net.mdns 原生API发现此服务
    /// </summary>
    public class MDnsService : IHostedService, IDisposable
    {
        private readonly ILogger<MDnsService> _logger;
        private readonly ServerAddressService _serverAddressService;
        private readonly string _serviceName = "baihua";
        private readonly string _serviceType = "_http._tcp";

        // Avahi (Linux)
        private Process? _avahiProcess;
        private int _avahiChildPid = 0;

        // Makaretu (Windows/macOS fallback)
        private MulticastService? _multicastService;
        private ServiceDiscovery? _serviceDiscovery;
        private ServiceProfile? _serviceProfile;

        private bool _isRunning = false;
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public MDnsService(ILogger<MDnsService> logger, ServerAddressService serverAddressService)
        {
            _logger = logger;
            _serverAddressService = serverAddressService;
        }

        public bool IsRunning => _isRunning;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var platform = IsLinux ? "Avahi" : "Makaretu.Dns";
            _logger.LogInformation("Starting mDNS service ({Platform})...", platform);

            try
            {
                var (httpUrl, hostName) = _serverAddressService.GetQrCodeAddresses();
                var localIp = GetLocalIpAddress();
                int port = 8788;
                if (!string.IsNullOrEmpty(httpUrl) && Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
                {
                    port = uri.Port;
                }

                // 优先尝试 Makaretu.Dns（纯 .NET，不依赖系统命令，Docker 容器内可用）
                // 如果需要在宿主机上使用 avahi，可配置环境变量 USE_AVAHI=1
                var useAvahi = IsLinux && Environment.GetEnvironmentVariable("USE_AVAHI") == "1";
                if (useAvahi)
                {
                    StartWithAvahi(hostName, localIp, port, httpUrl);
                }
                else
                {
                    StartWithMakaretu(hostName, localIp, port, httpUrl);
                }

                _isRunning = true;
                _logger.LogInformation(
                    "mDNS service started ({Platform}). Service: {Service}.{Type} at {Ip}:{Port}, HostName: {HostName}",
                    platform, _serviceName, _serviceType, localIp, port, hostName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start mDNS service, continuing without mDNS");
                _isRunning = false;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Linux: 使用 avahi-publish-service 发布mDNS服务
        /// Avahi是Linux标准的mDNS实现，能正确处理多播组加入和端口共享
        /// </summary>
        private void StartWithAvahi(string hostName, string localIp, int port, string httpUrl)
        {
            // Ensure avahi-daemon is running
            EnsureAvahiDaemonRunning();

            // 安全：mDNS 广播不再包含 serverId 和完整 apiUrl，减少信息泄露
            var args = $"-s {hostName} {_serviceType} {port} \"serviceId=com.lumin.huaji.sync\" \"hostName={hostName}\"";

            // Use nohup to keep the process running even if parent's stdout closes
            _avahiProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"avahi-publish-service {args} >/dev/null 2>&1 & echo $!\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            _avahiProcess.Start();
            var pidStr = _avahiProcess.StandardOutput.ReadLine();
            _avahiProcess.WaitForExit();
            if (int.TryParse(pidStr, out var childPid))
            {
                _avahiChildPid = childPid;
            }
            _logger.LogInformation("avahi-publish-service started (PID: {Pid})", pidStr);
        }

        private void EnsureAvahiDaemonRunning()
        {
            try
            {
                // Check if avahi-daemon is running
                using var check = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pgrep",
                        Arguments = "avahi-daemon",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                check.Start();
                var output = check.StandardOutput.ReadToEnd();
                check.WaitForExit();

                if (check.ExitCode != 0)
                {
                    _logger.LogInformation("avahi-daemon not running, starting it...");
                    // Try to start avahi-daemon
                    using var start = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "sudo",
                            Arguments = "systemctl start avahi-daemon",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    start.Start();
                    start.WaitForExit();
                    Thread.Sleep(1000); // Give avahi time to initialize
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check/start avahi-daemon");
            }
        }

        /// <summary>
        /// Windows/macOS: 使用 Makaretu.Dns.Multicast.New 库发布mDNS服务
        /// </summary>
        private void StartWithMakaretu(string hostName, string localIp, int port, string httpUrl)
        {
            ushort uport = (ushort)port;

            _serviceProfile = new ServiceProfile(
                instanceName: hostName,
                serviceName: _serviceType,
                port: uport
            );

            _serviceProfile.HostName = $"{hostName}.local";

            // Remove auto-detected A/AAAA records and add only our LAN IP
            _serviceProfile.Resources.RemoveAll(r => r is ARecord || r is AAAARecord);
            if (IPAddress.TryParse(localIp, out var lanIp))
            {
                _serviceProfile.Resources.Add(new ARecord
                {
                    Name = _serviceProfile.HostName,
                    Address = lanIp
                });
            }

            _serviceProfile.AddProperty("serviceId", "com.lumin.huaji.sync");
            _serviceProfile.AddProperty("hostName", hostName);

            _multicastService = new MulticastService();
            _serviceDiscovery = new ServiceDiscovery(_multicastService);
            MulticastService.EnableUnicastAnswers = true;

            _multicastService.QueryReceived += (s, e) =>
            {
            };

            _serviceDiscovery.Advertise(_serviceProfile);
            _multicastService.Start();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _isRunning = false;

            try
            {
                if (IsLinux)
                {
                    if (_avahiChildPid > 0)
                    {
                        try
                        {
                            using var killProc = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "kill",
                                    Arguments = _avahiChildPid.ToString(),
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            killProc.Start();
                            killProc.WaitForExit(3000);
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "操作失败"); }
                        _avahiChildPid = 0;
                    }
                    _avahiProcess?.Dispose();
                    _avahiProcess = null;
                }
                else
                {
                    if (_serviceDiscovery != null && _serviceProfile != null)
                    {
                        _serviceDiscovery.Unadvertise(_serviceProfile);
                    }
                    _multicastService?.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping mDNS service");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取本机局域网IP地址（优先选择真实网卡，排除虚拟网卡）
        /// </summary>
        private string GetLocalIpAddress()
        {
            try
            {
                var candidates = new List<(IPAddress Ip, int Priority)>();

                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;

                    var name = ni.Name.ToLowerInvariant();
                    if (name.Contains("veth") || name.Contains("docker") || name.Contains("vnic") ||
                        name.Contains("hyper-v") || name.Contains("wsl") || name.Contains("tunnel"))
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        var ip = addr.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                            continue;

                        var bytes = ip.GetAddressBytes();

                        if (bytes[0] == 192 && bytes[1] == 168)
                        {
                            candidates.Add((ip, 100));
                            continue;
                        }

                        if (bytes[0] == 10)
                        {
                            candidates.Add((ip, 80));
                            continue;
                        }

                        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        {
                            candidates.Add((ip, bytes[3] == 1 ? 20 : 60));
                            continue;
                        }

                        candidates.Add((ip, 40));
                    }
                }

                var best = candidates.OrderByDescending(c => c.Priority).FirstOrDefault();
                if (best.Ip != null)
                {
                    return best.Ip.ToString();
                }

                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting local IP address"); }
            return "127.0.0.1";
        }

        public void Dispose()
        {
            _avahiProcess?.Dispose();
            _serviceDiscovery?.Dispose();
            _multicastService?.Dispose();
        }
    }

    /// <summary>
    /// mDNS服务信息
    /// </summary>
    public class MDnsServiceInfo
    {
        public string FullName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public string ApiUrl { get; set; } = string.Empty;
        public string ServiceId { get; set; } = string.Empty;
        public DateTime DiscoveredTime { get; set; }

        public override string ToString() => $"{ServiceName} ({Address}:{Port}) - {ApiUrl}";
    }

    /// <summary>
    /// mDNS服务发现事件参数
    /// </summary>
    public class MDnsServiceDiscoveredEventArgs : EventArgs
    {
        public MDnsServiceInfo ServiceInfo { get; }
        public MDnsServiceDiscoveredEventArgs(MDnsServiceInfo serviceInfo) { ServiceInfo = serviceInfo; }
    }
}
