using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baihua.Core.Data;

/// <summary>
/// SQLite 启动初始化：启用 WAL 日志模式（多进程并发读写安全）。
/// journal_mode 持久化在数据库文件头，幂等可重复调用；失败时降级为默认模式并告警。
/// busy_timeout 通过连接串 "Default Timeout" 配置（见各 DbContext）。
/// </summary>
public static class SqliteSetup
{
    public static void EnableWal(DbContext dbContext, ILogger logger)
    {
        try
        {
            var conn = dbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            var mode = cmd.ExecuteScalar()?.ToString();

            logger.LogInformation("[SqliteSetup] {Database} journal_mode={Mode}", conn.Database, mode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SqliteSetup] 启用 WAL 失败（继续使用默认 journal_mode）: {Database}",
                dbContext.Database.GetDbConnection().Database);
        }
    }
}
