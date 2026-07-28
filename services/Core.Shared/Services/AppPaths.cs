namespace TaskRunner.Services;

/// <summary>
/// 获取配置文件目录，使用 BaihuaPaths.Db（与数据库同目录）
/// </summary>
public static class AppPaths
{
    public static string GetConfigDirectory()
    {
        var dir = TaskRunner.Contracts.BaihuaPaths.Db;
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLogsDirectory()
    {
        var dir = TaskRunner.Contracts.BaihuaPaths.Logs;
        Directory.CreateDirectory(dir);
        return dir;
    }
}
