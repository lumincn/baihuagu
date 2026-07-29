using System;

namespace TaskRunner.Services
{
    public static class ObsidianExecutableResolver
    {
        public static bool TryGetPath(out string exePath)
        {
            exePath = string.Empty;

            // 最高优先级：允许用户显式指定可执行文件路径（跨平台都适用）
            // 设置环境变量后，无需依赖 PATH/猜测安装路径。
            var overridePath =
                Environment.GetEnvironmentVariable("TASK_RUNNER_OBSIDIAN_EXE_PATH") ??
                Environment.GetEnvironmentVariable("TASK_RUNNER_OBSIDIAN_EXE");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                try
                {
                    var trimmed = overridePath.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(trimmed) && System.IO.File.Exists(trimmed))
                    {
                        exePath = trimmed;
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // Windows 上 Obsidian 不一定会加入 PATH。
            // 这里先找常见安装路径；再扫描 PATH；最后返回 false（调用方再做降级）。
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                System.IO.Path.Combine(localAppData, "Programs", "Obsidian", "Obsidian.exe"),
                System.IO.Path.Combine(localAppData, "Programs", "Obsidian", "obsidian.exe"),
                @"C:\Users\lumin\AppData\Local\Programs\Obsidian\Obsidian.exe",
                @"C:\Users\lumin\AppData\Local\Programs\Obsidian\obsidian.exe",
            };

            foreach (var c in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(c) && System.IO.File.Exists(c))
                    {
                        exePath = c;
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // 在非 Windows（或 Windows 未找到常见安装路径）时，继续尝试从 PATH 中解析。
            // 这一步保证 Linux/macOS 不会被 Windows 路径硬编码影响。
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathEnv.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Linux: obsidian（无扩展名）
                    var linuxCandidate = System.IO.Path.Combine(dir, "obsidian");
                    if (System.IO.File.Exists(linuxCandidate))
                    {
                        exePath = linuxCandidate;
                        return true;
                    }

                    // Windows 可能是 obsidian.exe
                    var winCandidate = System.IO.Path.Combine(dir, "obsidian.exe");
                    if (System.IO.File.Exists(winCandidate))
                    {
                        exePath = winCandidate;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        public static string Resolve()
        {
            return TryGetPath(out var p) ? p : "obsidian";
        }
    }
}

