using System.Diagnostics;
using System.Runtime.InteropServices;
using ComponentStatus = TaskRunner.Contracts.Health.ComponentStatusDto;

namespace TaskRunner.Services
{
    public partial class SystemHealthService
    {
        private async Task<ComponentStatus> CheckGitAsync(CancellationToken cancellationToken)
        {
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                    return new ComponentStatus { Name = "Git", Status = "critical", Message = "Git 未安装" };

                var (ok, exitCode, output) = await WaitForProcessAsync(process, 4000, cancellationToken);
                if (!ok)
                    return new ComponentStatus { Name = "Git", Status = "critical", Message = "Git 检测超时" };
                if (exitCode != 0)
                    return new ComponentStatus { Name = "Git", Status = "critical", Message = "Git 检测失败" };

                return new ComponentStatus
                {
                    Name = "Git",
                    Status = "healthy",
                    Version = HealthCheckHelper.ExtractVersion(output),
                    Message = "Git 已安装"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Git 检测失败");
                return new ComponentStatus { Name = "Git", Status = "critical", Message = "Git 检测异常" };
            }
        }

        private Task<ComponentStatus> CheckObsidianAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var obsidianRunning = Process.GetProcessesByName("Obsidian").Length > 0;

                if (obsidianRunning)
                {
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "Obsidian",
                        Status = "healthy",
                        Message = "Obsidian 桌面客户端运行中"
                    });
                }

                // Linux 上 Obsidian 桌面客户端不是必须的，FTS5 全文搜索已替代 CLI 搜索功能
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "Obsidian",
                        Status = "healthy",
                        Message = "Obsidian 桌面客户端未运行（Linux 上 FTS5 搜索已替代 CLI 功能）"
                    });
                }

                return Task.FromResult(new ComponentStatus
                {
                    Name = "Obsidian",
                    Status = "warning",
                    Message = "Obsidian 桌面客户端未运行（CLI 功能需要桌面客户端）"
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Obsidian 检测失败");
                return Task.FromResult(new ComponentStatus
                {
                    Name = "Obsidian",
                    Status = "warning",
                    Message = "Obsidian 检测异常"
                });
            }
        }

        /// <summary>
        /// 启动时初始化 Obsidian（仅调用一次）
        /// 如果 CLI 已安装但 Obsidian 未运行，则启动 Obsidian
        /// </summary>
        public async Task InitializeObsidianAsync()
        {
            try
            {
                var obsidianRunning = Process.GetProcessesByName("Obsidian").Length > 0;
                if (obsidianRunning)
                {
                    _logger.LogInformation("Obsidian 已在运行，跳过启动");
                    return;
                }

                // 过去用 `obsidian help` 做“CLI 可用性探测”，在 Windows 上可能表现为“打开又关闭”，
                // 反而干扰用户观察。这里不再探测，直接尝试启动桌面端；失败则记录并降级为文件扫描。
                var obsidianExe = ObsidianExecutableResolver.Resolve();
                _logger.LogInformation("Obsidian 未运行，尝试启动：{Exe}", obsidianExe);

                var startInfo = new ProcessStartInfo
                {
                    FileName = obsidianExe,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.EnvironmentVariables["ELECTRON_DISABLE_AUTO_UPDATE"] = "1";
                startInfo.EnvironmentVariables["OBSIDIAN_DISABLE_AUTO_UPDATE"] = "1";

                Process.Start(startInfo);
                _logger.LogInformation("Obsidian 启动成功");

                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "启动 Obsidian 失败");
            }
        }

    }
}
