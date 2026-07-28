namespace TaskRunner.Contracts;

/// <summary>
/// 百花统一数据路径 — 替代旧的环境变量 YJ_DATA_DIR / TASKRUNNER_VAULT_ROOT
///
/// 优先级：
///   1. BAIHUA_HOME 环境变量（最高）
///   2. 旧变量 YJ_DATA_DIR / TASKRUNNER_VAULT_ROOT（兼容，打印警告）
///   3. 平台默认值
///
/// 默认值：
///   Windows:  %USERPROFILE%\.baihua
///   Linux:    ~/.baihua  （非 Docker）
///             /opt/baihua/data（Docker / systemd 生产部署）
///   macOS:    ~/.baihua
///
/// 目录结构：
///   $BAIHUA_HOME/
///   ├── vaults/     知识库文件（原 ~/.yj-vaults）
///   ├── db/         数据库 + 密钥（原 data/）
///   └── logs/       运行日志（可选）
/// </summary>
public static class BaihuaPaths
{
    private static string? _home;
    private static readonly object _lock = new();

    /// <summary>数据根目录</summary>
    public static string Home
    {
        get
        {
            if (_home != null) return _home;
            lock (_lock)
            {
                if (_home != null) return _home;

                // 1. 新变量
                var home = Environment.GetEnvironmentVariable("BAIHUA_HOME");
                if (!string.IsNullOrWhiteSpace(home))
                {
                    _home = home.TrimEnd('/', '\\');
                    return _home;
                }

                // 2. 兼容旧变量 YJ_DATA_DIR
                var yjDataDir = Environment.GetEnvironmentVariable("YJ_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(yjDataDir))
                {
                    // 旧 data/ 目录 → 往上一级设为 Home
                    var trimmed = yjDataDir.TrimEnd('/', '\\');
                    var dirName = Path.GetFileName(trimmed);
                    if (string.Equals(dirName, "data", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = Path.GetDirectoryName(trimmed);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            _home = parent;
                            Console.Error.WriteLine(
                                $"[BaihuaPaths] 使用旧变量 YJ_DATA_DIR={yjDataDir}，推荐设置 BAIHUA_HOME={_home}");
                            return _home;
                        }
                    }
                    _home = trimmed;
                    Console.Error.WriteLine(
                        $"[BaihuaPaths] 使用旧变量 YJ_DATA_DIR={yjDataDir}，推荐设置 BAIHUA_HOME={_home}");
                    return _home;
                }

                // 3. 兼容旧变量 TASKRUNNER_VAULT_ROOT → 取父目录
                var vaultRoot = Environment.GetEnvironmentVariable("TASKRUNNER_VAULT_ROOT");
                if (!string.IsNullOrWhiteSpace(vaultRoot))
                {
                    var parent = Path.GetDirectoryName(vaultRoot.TrimEnd('/', '\\'));
                    if (!string.IsNullOrEmpty(parent))
                    {
                        _home = parent;
                        Console.Error.WriteLine(
                            $"[BaihuaPaths] 使用旧变量 TASKRUNNER_VAULT_ROOT={vaultRoot}，推断 BAIHUA_HOME={_home}");
                        return _home;
                    }
                }

                // 4. 平台默认
                _home = GetDefaultHome();
                return _home;
            }
        }
    }

    /// <summary>知识库目录 = $HOME/vaults</summary>
    public static string Vaults => Path.Combine(Home, "vaults");

    /// <summary>数据库目录 = $HOME/db</summary>
    public static string Db => Path.Combine(Home, "db");

    /// <summary>日志目录 = $HOME/logs（可选）</summary>
    public static string Logs => Path.Combine(Home, "logs");

    /// <summary>加密密钥文件路径</summary>
    public static string KeyFile => Path.Combine(Db, ".baihua-key");

    /// <summary>重置缓存（用于测试）</summary>
    public static void Reset() { lock (_lock) { _home = null; } }

    private static string GetDefaultHome()
    {
        var isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                       || File.Exists("/.dockerenv");

        if (OperatingSystem.IsLinux())
        {
            if (isDocker)
                return "/opt/baihua/data";

            // systemd 生产部署或桌面 Linux
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".baihua");

            return "/opt/baihua/data";
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".baihua");
            return Path.Combine("/Users", Environment.UserName, ".baihua");
        }

        // Windows
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".baihua");
    }
}
