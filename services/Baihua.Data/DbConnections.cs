namespace Baihua.Data;

/// <summary>
/// PostgreSQL 连接配置（一服务一数据库：family / vault / ai 三库独立）。
/// k8s 部署经 configmap 注入 PG_HOST/PG_USER、secret 注入 PG_PASSWORD；
/// 本地开发缺省 localhost/baihua。
/// </summary>
public static class DbConnections
{
    public static string For(string dbName)
    {
        var host = Environment.GetEnvironmentVariable("PG_HOST") ?? "localhost";
        var user = Environment.GetEnvironmentVariable("PG_USER") ?? "baihua";
        var pw = Environment.GetEnvironmentVariable("PG_PASSWORD") ?? "Baihua2026Pg!";
        return $"Host={host};Port=5432;Database={dbName};Username={user};Password={pw};";
    }
}
