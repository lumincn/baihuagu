using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Contracts.Devices;

namespace TaskRunner.Core.Shared;
    /// <summary>
    /// 设备状态
    /// </summary>
    public enum DeviceStatus
    {
        Pending,
        Authorized,
        Revoked
    }

    /// <summary>
    /// 设备信息
    /// </summary>
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public DeviceStatus Status { get; set; }
        public DateTime FirstRequestTime { get; set; }
        public DateTime? AuthorizedTime { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string? AccessToken { get; set; }
        public string? IpAddress { get; set; }
        public int SyncCount { get; set; }
        public DateTime? FirstSyncTime { get; set; }
        /// <summary>最近30天内同步过的知识库ID列表</summary>
        public List<string> SyncedVaultIds { get; set; } = new();
        /// <summary>已同步知识库的 ID→名称 映射</summary>
        public Dictionary<string, string> SyncedVaultNames { get; set; } = new();
    }

    /// <summary>
    /// 配对请求信息
    /// </summary>
    public class PairRequestInfo
    {
        public string RequestId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string PairCode { get; set; } = string.Empty;
        public DateTime RequestTime { get; set; }
        public string? IpAddress { get; set; }
        /// <summary>移动端真实设备标识（如 ANDROID_ID），授权时写入 AuthorizedDevice</summary>
        public string DeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 设备授权服务 - 管理设备配对和授权（使用 SQLite + EF Core 存储）
    /// </summary>
    public class DeviceService
    {
        private readonly ILogger<DeviceService> _logger;
        private string _pairCode;
        private readonly IHubContext<Hubs.DeviceHub>? _deviceHub;
        private readonly WebSocket.DeviceWebSocketHub? _wsHub;
        private readonly IDbContextFactory<FamilyDbContext> _dbContextFactory;
        private readonly IDbContextFactory<VaultDbContext>? _vaultDbContextFactory;
        
        // 待授权设备队列（内存中，不持久化）
        private readonly ConcurrentDictionary<string, PairRequestInfo> _pendingRequests = new();
        
        // 请求处理结果记录（内存中，短期缓存）
        private readonly ConcurrentDictionary<string, string> _requestResults = new();

        // 局域网发现请求锁，防止同名设备创建多个 pending 请求
        private readonly object _lanDiscoveryLock = new();

        public DeviceService(
            IConfiguration configuration, 
            ILogger<DeviceService> logger, 
            IDbContextFactory<FamilyDbContext> dbContextFactory,
            IDbContextFactory<VaultDbContext>? vaultDbContextFactory = null,
            IHubContext<Hubs.DeviceHub>? deviceHub = null,
            WebSocket.DeviceWebSocketHub? wsHub = null)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _vaultDbContextFactory = vaultDbContextFactory;
            _deviceHub = deviceHub;
            _wsHub = wsHub;
            
            // 优先使用配置中的配对码，如果没有则生成随机配对码
            _pairCode = configuration["PairCode"] ?? GenerateRandomPairCode();
            

        }

        /// <summary>
        /// 生成随机配对码（8位字母数字混合）
        /// </summary>
        public static string GenerateRandomPairCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 排除易混淆字符 I,1,O,0
            return new string(Enumerable.Range(0, 8)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }

        /// <summary>
        /// 获取当前配对码
        /// </summary>
        public string GetPairCode() => _pairCode;

        /// <summary>
        /// 刷新配对码（生成新的随机配对码）
        /// </summary>
        public string RefreshPairCode()
        {
            _pairCode = GenerateRandomPairCode();
            _logger.LogInformation("配对码已刷新");
            return _pairCode;
        }


        public bool ValidatePairCode(string? pairCode)
        {
            if (string.IsNullOrWhiteSpace(pairCode)) return false;
            return pairCode.Trim() == _pairCode;
        }

        public PairRequestInfo SubmitPairRequest(string deviceName, string pairCode, string? ipAddress = null, string? requestId = null, string? deviceId = null)
        {
            // 如果指定了 requestId（如 challenge），检查是否已存在
            if (!string.IsNullOrEmpty(requestId) && _pendingRequests.ContainsKey(requestId))
            {
                _logger.LogInformation("设备配对请求已存在: {DeviceName}, 请求ID: {RequestId}", 
                    deviceName, requestId);
                return _pendingRequests[requestId];
            }

            var existingRequest = _pendingRequests.Values
                .FirstOrDefault(r => r.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            
            if (existingRequest != null)
            {
                _logger.LogInformation("设备配对请求已存在: {DeviceName}, 请求ID: {RequestId}", 
                    deviceName, existingRequest.RequestId);
                return existingRequest;
            }

            var request = new PairRequestInfo
            {
                RequestId = requestId ?? Guid.NewGuid().ToString("N"),
                DeviceName = deviceName,
                PairCode = pairCode,
                RequestTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                DeviceId = deviceId ?? ""
            };

            _pendingRequests[request.RequestId] = request;
            _logger.LogInformation("设备请求配对: {DeviceName}, 请求ID: {RequestId}", deviceName, request.RequestId);
            
            _ = NotifyDeviceStatusChangedAsync("pair_request", deviceName, request.RequestId)
                .ContinueWith(t => 
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "发送配对通知失败");
                    }
                });
            
            return request;
        }

        /// <summary>
        /// 局域网自动发现请求：无需配对码，直接创建待授权请求
        /// </summary>
        public PairRequestInfo SubmitLanDiscoveryRequest(string deviceName, string? ipAddress = null, string? deviceId = null)
        {
            lock (_lanDiscoveryLock)
            {
                var existingRequest = _pendingRequests.Values
                    .FirstOrDefault(r => r.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
                
                if (existingRequest != null)
                {
                    _logger.LogInformation("局域网发现请求已存在: {DeviceName}, 请求ID: {RequestId}", 
                        deviceName, existingRequest.RequestId);
                    return existingRequest;
                }

                var request = new PairRequestInfo
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    DeviceName = deviceName,
                    PairCode = "LAN", // 标记为局域网发现，不参与配对码验证
                    RequestTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    DeviceId = deviceId ?? ""
                };

                _pendingRequests[request.RequestId] = request;
                _logger.LogInformation("局域网发现设备: {DeviceName}, 请求ID: {RequestId}, IP: {Ip}", 
                    deviceName, request.RequestId, ipAddress);
                
                _ = NotifyDeviceStatusChangedAsync("lan_discovered", deviceName, request.RequestId)
                    .ContinueWith(t => 
                    {
                        if (t.IsFaulted)
                        {
                            _logger.LogError(t.Exception, "发送局域网发现通知失败");
                        }
                    });
                
                return request;
            }
        }

        public IReadOnlyList<PairRequestInfo> GetPendingRequests()
        {
            return _pendingRequests.Values.ToList();
        }

        public string? GetRequestResult(string requestId)
        {
            if (_pendingRequests.ContainsKey(requestId))
            {
                return "pending";
            }
            
            _requestResults.TryGetValue(requestId, out var result);
            return result;
        }

        public (bool success, string? accessToken, string? error) AuthorizeDevice(string requestId)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            if (!_pendingRequests.TryRemove(requestId, out var request))
            {
                return (false, null, "请求不存在或已处理");
            }

            // 清除同名设备的其他待授权请求，防止授权后仍然显示在待授权列表中
            var keysToRemove = _pendingRequests
                .Where(kv => kv.Value.DeviceName.Equals(request.DeviceName, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in keysToRemove)
            {
                _pendingRequests.TryRemove(key, out _);
            }

            // 去重：同名设备已授权则直接返回现有令牌
            var existingAuthorized = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.DeviceName == request.DeviceName && d.Status == "Authorized");
            if (existingAuthorized != null)
            {
                // 如果配对请求携带了真实 DeviceId，且与现有记录不一致，更新为最新标识
                if (!string.IsNullOrEmpty(request.DeviceId) && existingAuthorized.DeviceId != request.DeviceId)
                {
                    _logger.LogInformation("设备 {DeviceName} 已授权，更新 DeviceId: {OldDeviceId} -> {NewDeviceId}",
                        request.DeviceName, existingAuthorized.DeviceId, request.DeviceId);
                    existingAuthorized.DeviceId = request.DeviceId;
                    existingAuthorized.IpAddress = request.IpAddress ?? existingAuthorized.IpAddress;
                    existingAuthorized.UpdatedAt = DateTime.UtcNow;
                    dbContext.SaveChanges();
                }

                _requestResults[requestId] = "authorized";
                _ = NotifyDeviceStatusChangedAsync("authorized", request.DeviceName, requestId);
                return (true, existingAuthorized.AccessToken, null);
            }

            var newDeviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
            var accessToken = Guid.NewGuid().ToString("N");

            dbContext.AuthorizedDevices.Add(new AuthorizedDevice
            {
                DeviceId = newDeviceId,
                DeviceName = request.DeviceName,
                AccessToken = accessToken,
                Status = "Authorized",
                IpAddress = request.IpAddress,
                AuthorizedTime = DateTime.UtcNow
            });
            dbContext.SaveChanges();
            
            _requestResults[requestId] = "authorized";
            
            _logger.LogInformation("设备已授权: {DeviceName}, DeviceId: {DeviceId}", request.DeviceName, newDeviceId);
            
            _ = NotifyDeviceStatusChangedAsync("authorized", request.DeviceName, requestId);

            return (true, accessToken, null);
        }

        public bool RejectRequest(string requestId)
        {
            if (_pendingRequests.TryRemove(requestId, out var request))
            {
                _requestResults[requestId] = "rejected";
                
                _logger.LogInformation("设备配对请求已拒绝: {DeviceName}, RequestId: {RequestId}", 
                    request.DeviceName, requestId);
                
                _ = NotifyDeviceStatusChangedAsync("rejected", request.DeviceName, requestId);
                
                return true;
            }
            return false;
        }

        public bool RevokeDevice(string deviceId)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.DeviceId == deviceId);
                
            if (device != null)
            {
                device.Status = "Revoked";
                dbContext.SaveChanges();
                
                _logger.LogInformation("设备授权已撤销: {DeviceName}, DeviceId: {DeviceId}", 
                    device.DeviceName, deviceId);
                
                _ = NotifyDeviceStatusChangedAsync("revoked", device.DeviceName, null);
                
                return true;
            }
            return false;
        }

        public bool ValidateAccessToken(string? accessToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            if (string.IsNullOrWhiteSpace(accessToken)) return false;
            
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.AccessToken == accessToken && d.Status == "Authorized");
                
            return device != null;
        }

        public DeviceInfo? GetDeviceByToken(string accessToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.AccessToken == accessToken);
                
            if (device == null) return null;
            
            return MapToDeviceInfo(device);
        }

        public void UpdateLastSyncTime(string accessToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.AccessToken == accessToken);
                
            if (device != null)
            {
                device.LastSyncTime = DateTime.UtcNow;
                device.SyncCount++;
                if (device.FirstSyncTime == null)
                {
                    device.FirstSyncTime = DateTime.UtcNow;
                }
                dbContext.SaveChanges();
                
                _ = NotifyDeviceStatusChangedAsync("sync_updated", device.DeviceName, null);
            }
        }

        public IReadOnlyList<DeviceInfo> GetAuthorizedDevices()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var devices = dbContext.AuthorizedDevices
                .Where(d => d.Status == "Authorized")
                .OrderBy(d => d.AuthorizedTime)
                .AsEnumerable()
                .GroupBy(d => d.DeviceName)
                .Select(g => g.First())
                .ToList();

            // 查询每个设备最近30天同步过的知识库
            var since = DateTime.UtcNow.AddDays(-30);
            var syncLogs = dbContext.DeviceSyncLogs
                .Where(l => l.SyncTime >= since && !string.IsNullOrEmpty(l.VaultId))
                .ToList();

            // 构建知识库 ID→名称 映射（包含已删除的知识库，确保名称可解析）
            var allVaultIds = syncLogs.Select(l => l.VaultId!).Distinct().ToHashSet();
            var vaultNameMap = new Dictionary<string, string>();
            if (_vaultDbContextFactory != null && allVaultIds.Count > 0)
            {
                using var vaultDb = _vaultDbContextFactory.CreateDbContext();
                vaultNameMap = vaultDb.Vaults
                    .Where(v => allVaultIds.Contains(v.VaultId))
                    .ToDictionary(v => v.VaultId, v => v.Name);
            }

            var result = new List<DeviceInfo>();
            foreach (var device in devices)
            {
                var info = MapToDeviceInfo(device);
                var syncedIds = syncLogs
                    .Where(l => l.DeviceId == device.DeviceId)
                    .Select(l => l.VaultId!)
                    .Distinct()
                    .ToList();
                info.SyncedVaultIds = syncedIds;
                info.SyncedVaultNames = syncedIds
                    .Where(id => vaultNameMap.ContainsKey(id))
                    .ToDictionary(id => id, id => vaultNameMap[id]);
                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// 自动授权设备（无需等待 WebUI 审批）
        /// </summary>
        public (bool success, string? accessToken, string? error) AutoAuthorizeDevice(string deviceName, string? ipAddress = null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();

            // 检查是否已存在同名授权设备
            var existing = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.DeviceName == deviceName && d.Status == "Authorized");
            if (existing != null)
            {
                return (true, existing.AccessToken, null);
            }

            var deviceId = Guid.NewGuid().ToString("N");
            var accessToken = Guid.NewGuid().ToString("N");

            dbContext.AuthorizedDevices.Add(new AuthorizedDevice
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                AccessToken = accessToken,
                Status = "Authorized",
                IpAddress = ipAddress,
                AuthorizedTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SyncCount = 0
            });
            dbContext.SaveChanges();

            _logger.LogInformation("设备已自动授权: {DeviceName}, DeviceId: {DeviceId}", deviceName, deviceId);
            return (true, accessToken, null);
        }

        /// <summary>
        /// 记录设备同步活动
        /// </summary>
        public void RecordSyncActivity(string deviceId, string deviceName, string? vaultId, int fileCount, string syncType, string? ipAddress = null)
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();

                // 更新设备同步计数
                var device = dbContext.AuthorizedDevices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (device != null)
                {
                    device.SyncCount++;
                    device.LastSyncTime = DateTime.UtcNow;
                    if (device.FirstSyncTime == null)
                    {
                        device.FirstSyncTime = DateTime.UtcNow;
                    }
                    device.UpdatedAt = DateTime.UtcNow;
                }

                // 记录同步日志
                dbContext.DeviceSyncLogs.Add(new DeviceSyncLog
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    IpAddress = ipAddress,
                    VaultId = vaultId,
                    FileCount = fileCount,
                    SyncType = syncType,
                    SyncTime = DateTime.UtcNow
                });

                dbContext.SaveChanges();

                // 查询知识库名称用于推送
                string? vaultName = null;
                if (!string.IsNullOrEmpty(vaultId) && _vaultDbContextFactory != null)
                {
                    try
                    {
                        using var vaultDb = _vaultDbContextFactory.CreateDbContext();
                        vaultName = vaultDb.Vaults
                            .Where(v => v.VaultId == vaultId)
                            .Select(v => v.Name)
                            .FirstOrDefault();
                    }
                    catch { /* best effort */ }
                }

                _ = NotifyDeviceStatusChangedAsync("sync_updated", deviceName, null, vaultId, vaultName)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception, "发送同步推送通知失败");
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录同步活动失败: {DeviceId}", deviceId);
            }
        }

        /// <summary>
        /// 获取移动端统计信息
        /// </summary>
        public MobileStats GetMobileStats()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var devices = dbContext.AuthorizedDevices.Where(d => d.Status == "Authorized").ToList();
            var syncLogs = dbContext.DeviceSyncLogs.ToList();

            return new MobileStats
            {
                TotalDevices = devices.Count,
                TotalSyncs = syncLogs.Count,
                TotalSyncFiles = syncLogs.Sum(l => l.FileCount),
                ActiveDevices7Days = devices.Count(d => d.LastSyncTime.HasValue && d.LastSyncTime.Value > DateTime.UtcNow.AddDays(-7)),
                ActiveDevices30Days = devices.Count(d => d.LastSyncTime.HasValue && d.LastSyncTime.Value > DateTime.UtcNow.AddDays(-30)),
                Devices = devices.Select(d => new DeviceStat
                {
                    DeviceId = d.DeviceId,
                    DeviceName = d.DeviceName,
                    IpAddress = d.IpAddress,
                    SyncCount = d.SyncCount,
                    FirstSyncTime = d.FirstSyncTime,
                    LastSyncTime = d.LastSyncTime,
                    AuthorizedTime = d.AuthorizedTime
                }).OrderByDescending(d => d.LastSyncTime).ToList()
            };
        }

        public IReadOnlyList<DeviceInfo> GetAllDevices()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            // 获取所有设备（包括已撤销的）
            return dbContext.AuthorizedDevices
                .OrderBy(d => d.AuthorizedTime)
                .Select(MapToDeviceInfo)
                .ToList();
        }

        public bool IsDeviceNameAuthorized(string deviceName)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            return dbContext.AuthorizedDevices
                .Any(d => d.DeviceName == deviceName && d.Status == "Authorized");
        }

        public DeviceInfo? GetAuthorizedDeviceByName(string deviceName)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.DeviceName == deviceName && d.Status == "Authorized");
                
            return device == null ? null : MapToDeviceInfo(device);
        }

        public DeviceInfo? GetAuthorizedDeviceById(string deviceId)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .FirstOrDefault(d => d.DeviceId == deviceId && d.Status == "Authorized");

            return device == null ? null : MapToDeviceInfo(device);
        }

        public DeviceInfo? GetDeviceByNameAnyStatus(string deviceName)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .Where(d => d.DeviceName == deviceName)
                .OrderByDescending(d => d.UpdatedAt)
                .FirstOrDefault();

            return device == null ? null : MapToDeviceInfo(device);
        }

        public bool ReactivateRevokedDevice(string deviceName, string newDeviceId, string? ipAddress = null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var device = dbContext.AuthorizedDevices
                .Where(d => d.DeviceName == deviceName && d.Status == "Revoked")
                .OrderByDescending(d => d.UpdatedAt)
                .FirstOrDefault();

            if (device == null) return false;

            device.Status = "Authorized";
            device.DeviceId = newDeviceId;
            if (ipAddress != null) device.IpAddress = ipAddress;
            device.UpdatedAt = DateTime.UtcNow;
            dbContext.SaveChanges();

            _logger.LogInformation("已撤销设备重新激活: {DeviceName}, 新DeviceId: {NewDeviceId}", deviceName, newDeviceId);

            _ = NotifyDeviceStatusChangedAsync("authorized", device.DeviceName, null);
            return true;
        }

        /// <summary>
        /// 更新旧设备的 DeviceId（兼容恢复场景）
        /// </summary>
        public bool UpdateDeviceId(string oldDeviceId, string newDeviceId, string? deviceName = null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            AuthorizedDevice? device = null;

            // 优先通过 deviceId 查找
            if (!string.IsNullOrEmpty(oldDeviceId))
            {
                device = dbContext.AuthorizedDevices
                    .FirstOrDefault(d => d.DeviceId == oldDeviceId && d.Status == "Authorized");
            }

            // 如果找不到且提供了 deviceName，通过 deviceName + 空 DeviceId 查找
            if (device == null && !string.IsNullOrEmpty(deviceName))
            {
                device = dbContext.AuthorizedDevices
                    .FirstOrDefault(d => d.DeviceName == deviceName &&
                        (d.DeviceId == null || d.DeviceId == "") &&
                        d.Status == "Authorized");
            }

            if (device == null) return false;
            device.DeviceId = newDeviceId;
            device.UpdatedAt = DateTime.UtcNow;
            dbContext.SaveChanges();
            return true;
        }

        private static DeviceInfo MapToDeviceInfo(AuthorizedDevice device)
        {
            return new DeviceInfo
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                Status = device.Status == "Authorized" ? DeviceStatus.Authorized : DeviceStatus.Revoked,
                AuthorizedTime = device.AuthorizedTime,
                LastSyncTime = device.LastSyncTime,
                AccessToken = device.AccessToken,
                IpAddress = device.IpAddress,
                SyncCount = device.SyncCount,
                FirstSyncTime = device.FirstSyncTime,
                SyncedVaultIds = new List<string>()
            };
        }

        private async Task NotifyDeviceStatusChangedAsync(string action, string deviceName, string? requestId,
            string? vaultId = null, string? vaultName = null)
        {
            var pushType = action switch
            {
                "authorized" => "Authorized",
                "revoked" => "Revoked",
                "rejected" => "Rejected",
                "sync_updated" => "SyncRequest",
                "pair_request" or "lan_discovered" => "PairRequest",
                _ => null
            };

            // 推送到 SignalR Hub（WebUI 使用）
            if (_deviceHub != null)
            {
                try
                {
                    await _deviceHub.Clients.All.SendAsync("DeviceStatusChanged", new
                    {
                        action = action,
                        deviceName = deviceName,
                        requestId = requestId,
                        type = pushType,
                        vaultId = vaultId,
                        vaultName = vaultName,
                        timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送 SignalR 通知失败: {Action} - {DeviceName}", action, deviceName);
                }
            }

            // 推送到纯 WebSocket Hub（移动端使用）
            if (_wsHub != null)
            {
                try
                {
                    await _wsHub.BroadcastAsync(action, deviceName, requestId, pushType, vaultId, vaultName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送纯 WebSocket 通知失败: {Action} - {DeviceName}", action, deviceName);
                }
            }
        }
    }