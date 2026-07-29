using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services
{
    /// <summary>
    /// 服务器地址配置服务
    /// </summary>
    public class ServerAddressService
    {
        private readonly IDbContextFactory<FamilyDbContext> _dbContextFactory;
        private readonly ILogger<ServerAddressService> _logger;
        private readonly IConfiguration _configuration;

        public ServerAddressService(
            IDbContextFactory<FamilyDbContext> dbContextFactory,
            ILogger<ServerAddressService> logger,
            IConfiguration configuration)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _configuration = configuration;
            EnsureTableExists();
        }

        /// <summary>
        /// 确保表存在
        /// </summary>
        private void EnsureTableExists()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ServerAddressSettings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Domain TEXT NOT NULL DEFAULT '',
                        Url TEXT NOT NULL DEFAULT '',
                        ServerInstanceId TEXT NOT NULL DEFAULT '',
                        SharedSecret TEXT NOT NULL DEFAULT '',
                        DisplayName TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                        UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                    );
                ";
                command.ExecuteNonQuery();

                // 兼容迁移：为旧版本数据库添加 DisplayName 列
                try
                {
                    using var alterCmd = connection.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE ServerAddressSettings ADD COLUMN DisplayName TEXT NOT NULL DEFAULT '';";
                    alterCmd.ExecuteNonQuery();
                    _logger.LogInformation("已为旧数据库添加 DisplayName 列");
                }
                catch
                {
                    // 列已存在，忽略
                }

                try
                {
                    using var alterCmd2 = connection.CreateCommand();
                    alterCmd2.CommandText = "ALTER TABLE ServerAddressSettings ADD COLUMN SharedSecret TEXT NOT NULL DEFAULT '';";
                    alterCmd2.ExecuteNonQuery();
                    _logger.LogInformation("已为旧数据库添加 SharedSecret 列");
                }
                catch
                {
                    // 列已存在，忽略
                }

                _logger.LogDebug("ServerAddressSettings 表已确保存在");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 ServerAddressSettings 表失败");
            }
        }

        /// <summary>
        /// 获取服务器地址配置
        /// </summary>
        public ServerAddressSetting GetSettings()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                var setting = dbContext.ServerAddressSettings.OrderBy(s => s.Id).FirstOrDefault();
                if (setting == null)
                {
                    setting = new ServerAddressSetting
                    {
                        Domain = "",
                        Url = "",
                        ServerInstanceId = GenerateServerInstanceId(),
                        DisplayName = GenerateCulturalDisplayName()
                    };
                    dbContext.ServerAddressSettings.Add(setting);
                    dbContext.SaveChanges();
                }
                else if (string.IsNullOrWhiteSpace(setting.ServerInstanceId))
                {
                    setting.ServerInstanceId = GenerateServerInstanceId();
                    dbContext.SaveChanges();
                }
                else if (string.IsNullOrWhiteSpace(setting.DisplayName))
                {
                    setting.DisplayName = GenerateCulturalDisplayName();
                    dbContext.SaveChanges();
                }

                if (string.IsNullOrWhiteSpace(setting.SharedSecret))
                {
                    setting.SharedSecret = GenerateSharedSecret();
                    dbContext.SaveChanges();
                }
                return setting;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取服务器地址配置失败，返回默认配置");
                return new ServerAddressSetting { Domain = "", Url = "" };
            }
        }

        /// <summary>
        /// 更新服务器地址配置（使用域名）
        /// </summary>
        public async Task<ServerAddressSetting> UpdateSettings(string? domain, string? displayName = null)
        {
            var normalizedDomain = NormalizeDomain(domain ?? "");

            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                var setting = dbContext.ServerAddressSettings.OrderBy(s => s.Id).FirstOrDefault();
                if (setting == null)
                {
                    setting = new ServerAddressSetting
                    {
                        Domain = normalizedDomain,
                        DisplayName = displayName ?? GenerateCulturalDisplayName(),
                        Url = "",
                        ServerInstanceId = GenerateServerInstanceId(),
                        SharedSecret = GenerateSharedSecret()
                    };
                    dbContext.ServerAddressSettings.Add(setting);
                }
                else
                {
                    setting.Domain = normalizedDomain;
                    if (displayName != null)
                    {
                        setting.DisplayName = displayName;
                    }
                }

                await dbContext.SaveChangesAsync();
                _logger.LogInformation("服务器地址配置已更新: Domain={Domain}", setting.Domain);

                return setting;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新服务器地址配置失败，尝试直接执行 SQL");

                // 如果 EF Core 失败，使用原始 SQL
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                // 先确保表存在
                using (var createCmd = connection.CreateCommand())
                {
                    createCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ServerAddressSettings (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Domain TEXT NOT NULL DEFAULT '',
                            Url TEXT NOT NULL DEFAULT '',
                            ServerInstanceId TEXT NOT NULL DEFAULT '',
                            DisplayName TEXT NOT NULL DEFAULT '',
                            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                            UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                        );
                    ";
                    await createCmd.ExecuteNonQueryAsync();
                }

                // 检查是否有记录
                using (var checkCmd = connection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM ServerAddressSettings;";
                    var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    using var cmd = connection.CreateCommand();
                    if (count == 0)
                    {
                        var newId = GenerateServerInstanceId();
                        cmd.CommandText = @"
                            INSERT INTO ServerAddressSettings (Domain, Url, ServerInstanceId, CreatedAt, UpdatedAt)
                            VALUES (@domain, '', @serverInstanceId, datetime('now'), datetime('now'));";

                        var pId = cmd.CreateParameter();
                        pId.ParameterName = "@serverInstanceId";
                        pId.Value = newId;
                        cmd.Parameters.Add(pId);
                    }
                    else
                    {
                        cmd.CommandText = @"
                            UPDATE ServerAddressSettings
                            SET Domain = @domain, Url = '', UpdatedAt = datetime('now'),
                                ServerInstanceId = CASE WHEN ServerInstanceId = '' THEN @serverInstanceId ELSE ServerInstanceId END
                            WHERE Id = (SELECT Id FROM ServerAddressSettings LIMIT 1);";

                        var pId = cmd.CreateParameter();
                        pId.ParameterName = "@serverInstanceId";
                        pId.Value = GenerateServerInstanceId();
                        cmd.Parameters.Add(pId);
                    }

                    var p1 = cmd.CreateParameter();
                    p1.ParameterName = "@domain";
                    p1.Value = normalizedDomain;
                    cmd.Parameters.Add(p1);

                    await cmd.ExecuteNonQueryAsync();
                }

                return new ServerAddressSetting
                {
                    Domain = normalizedDomain,
                    Url = ""
                };
            }
        }

        /// <summary>
        /// 获取服务器实例唯一标识
        /// </summary>
        public string GetServerInstanceId()
        {
            return GetSettings().ServerInstanceId;
        }

        public string GetSharedSecret()
        {
            return GetSettings().SharedSecret;
        }

        /// <summary>
        /// 生成服务器实例唯一标识
        /// </summary>
        private static string GenerateServerInstanceId()
        {
            return $"srv-{Guid.NewGuid():N}";
        }

        private static string GenerateSharedSecret()
        {
            return $"sec-{Guid.NewGuid():N}";
        }

        private static readonly string[] CulturalNamePrefixes =
        [
            "听风", "望月", "拾光", "寻芳", "踏雪", "观云", "沐雨", "临水",
            "知秋", "迎春", "听雨", "看花", "折柳", "采菊", "抚琴", "煮茶",
            "清心", "静思", "悠然", "闲云", "素心", "雅韵", "墨香", "竹影",
            "松风", "梅骨", "兰心", "菊韵", "荷香", "桃夭", "杏雨", "梨云"
        ];

        private static readonly string[] CulturalNameSuffixes =
        [
            "阁", "轩", "斋", "居", "堂", "舍", "庐", "苑",
            "院", "楼", "亭", "台", "馆", "室", "房", "庄"
        ];

        private static string GenerateCulturalDisplayName()
        {
            var prefix = CulturalNamePrefixes[Random.Shared.Next(CulturalNamePrefixes.Length)];
            var suffix = CulturalNameSuffixes[Random.Shared.Next(CulturalNameSuffixes.Length)];
            return $"{prefix}{suffix}";
        }

        /// <summary>
        /// 获取用于二维码的服务器地址
        /// 如果配置了域名则使用 https://域名（广域网）
        /// 否则自动生成局域网地址（http://IP:端口）
        /// </summary>
        public (string url, string hostName) GetQrCodeAddresses()
        {
            try
            {
                var settings = GetSettings();
                // 优先使用配置的 DisplayName，否则使用系统 hostname
                var hostName = !string.IsNullOrWhiteSpace(settings.DisplayName)
                    ? settings.DisplayName
                    : System.Net.Dns.GetHostName();

                // 优先使用 Domain（广域网 HTTPS）
                if (!string.IsNullOrWhiteSpace(settings.Domain))
                {
                    var domain = NormalizeDomain(settings.Domain);
                    return ($"https://{domain}", hostName);
                }

                // 局域网：自动生成 HTTP 地址
                var localIp = GetLocalIpAddress();
                int httpPort = GetHttpPort();

                return ($"http://{localIp}:{httpPort}", hostName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取二维码地址失败，使用默认地址");
                var localIp = GetLocalIpAddress();
                var displayName = "";
                try { displayName = GetSettings().DisplayName ?? ""; } catch { }
                var hostName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : System.Net.Dns.GetHostName();
                return ($"http://{localIp}:8788", hostName);
            }
        }

        /// <summary>
        /// 获取配置的 HTTP 端口
        /// </summary>
        private int GetHttpPort()
        {
            var configuredHttpUrl = _configuration["Kestrel:Endpoints:Http:Url"];
            if (!string.IsNullOrWhiteSpace(configuredHttpUrl) &&
                Uri.TryCreate(configuredHttpUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogDebug("从配置中获取HTTP端口: {Port}", uri.Port);
                return uri.Port;
            }
            _logger.LogDebug("使用默认HTTP端口: 8788");
            return 8788;
        }

        /// <summary>
        /// 规范化域名：去掉协议前缀、路径、端口
        /// </summary>
        private static string NormalizeDomain(string domain)
        {
            domain = domain.Trim();
            if (string.IsNullOrEmpty(domain))
                return "";

            // 去掉协议前缀
            if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                domain = domain.Substring(7);
            else if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                domain = domain.Substring(8);

            // 去掉尾部斜杠和路径
            var slashIndex = domain.IndexOf('/');
            if (slashIndex >= 0)
                domain = domain.Substring(0, slashIndex);

            // 去掉端口（简单处理，IPv6 暂不支持）
            var colonIndex = domain.LastIndexOf(':');
            if (colonIndex > 0)
                domain = domain.Substring(0, colonIndex);

            return domain.Trim();
        }

        /// <summary>
        /// 获取本机局域网IP地址
        /// </summary>
        private string GetLocalIpAddress()
        {
            try
            {
                var hostName = System.Net.Dns.GetHostName();
                var addresses = System.Net.Dns.GetHostAddresses(hostName);

                // 优先选择非回环的IPv4地址
                foreach (var address in addresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(address))
                    {
                        return address.ToString();
                    }
                }

                return "127.0.0.1";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取本机IP失败");
                return "127.0.0.1";
            }
        }
    }
}
