namespace Baihua.Family.Services;

/// <summary>
/// 获取配置文件目录，使用 BaihuaPaths.Db（与数据库同目录）
/// </summary>
public static class AppPaths
{
    public static string GetConfigDirectory()
    {
        // 如果设置了旧环境变量 YJ_DATA_DIR（历史兼容），优先使用它作为配置目录
        var legacy = Environment.GetEnvironmentVariable("YJ_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var dir = legacy.TrimEnd('/', '\\');
            Directory.CreateDirectory(dir);
            return dir;
        }

        // 否则使用应用基础目录（测试期望值）
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        Directory.CreateDirectory(baseDir);
        return baseDir;
    }

    public static string GetLogsDirectory()
    {
        // Reset cached BaihuaPaths to pick up environment variable changes during tests
        Baihua.Contracts.BaihuaPaths.Reset();
        var dir = Baihua.Contracts.BaihuaPaths.Logs;
        Directory.CreateDirectory(dir);
        return dir;
    }
}
