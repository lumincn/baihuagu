using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskRunner.Core.Shared.Security;
using ComponentStatus = TaskRunner.Contracts.Health.ComponentStatusDto;

namespace TaskRunner.Services
{
    public partial class SystemHealthService
    {
        private async Task<ComponentStatus> CheckPythonAsync(CancellationToken cancellationToken)
        {
            // Windows 上优先尝试 py 启动器，然后是 python/python3
            var pythonCmds = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "py", "python", "python3" }
                : new[] { "python3", "python" };

            foreach (var cmd in pythonCmds)
            {
                Process? process = null;
                try
                {
                    process = Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    if (process is null) continue;

                    var (ok, exitCode, output) = await WaitForProcessAsync(process, 4000, cancellationToken);
                    if (ok && exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        return new ComponentStatus
                        {
                            Name = "Python",
                            Status = "healthy",
                            Version = HealthCheckHelper.ExtractVersion(output),
                            Message = $"Python 已安装 ({cmd})"
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 尝试下一个命令
                    continue;
                }
            }

            return new ComponentStatus
            {
                Name = "Python",
                Status = "warning",
                Message = "Python 未安装（可选，用于脚本扩展）"
            };
        }

        private async Task<ComponentStatus> CheckNodeAsync(CancellationToken cancellationToken)
        {
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    return new ComponentStatus
                    {
                        Name = "Node.js",
                        Status = "warning",
                        Message = "Node.js 未安装"
                    };
                }

                var (ok, exitCode, output) = await WaitForProcessAsync(process, 4000, cancellationToken);
                if (!ok)
                    return new ComponentStatus { Name = "Node.js", Status = "critical", Message = "Node.js 检测超时" };
                if (exitCode != 0)
                    return new ComponentStatus { Name = "Node.js", Status = "critical", Message = "Node.js 检测失败" };

                return new ComponentStatus
                {
                    Name = "Node.js",
                    Status = "healthy",
                    Version = HealthCheckHelper.ExtractVersion(output),
                    Message = "Node.js 已安装"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Node.js 检测失败");
                return new ComponentStatus
                {
                    Name = "Node.js",
                    Status = "warning",
                    Message = "Node.js 检测异常"
                };
            }
        }

        private async Task<ComponentStatus> CheckPipAsync(CancellationToken cancellationToken)
        {
            // Windows 上优先尝试 py 启动器，然后是 python/python3
            var pythonCmds = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "py", "python", "python3" }
                : new[] { "python3", "python" };

            foreach (var cmd in pythonCmds)
            {
                Process? process = null;
                try
                {
                    process = Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "-m pip --version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    if (process is null) continue;

                    var (ok, exitCode, output) = await WaitForProcessAsync(process, 4000, cancellationToken);
                    if (ok && exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        return new ComponentStatus
                        {
                            Name = "PIP",
                            Status = "healthy",
                            Version = HealthCheckHelper.ExtractVersion(output),
                            Message = $"PIP 已安装 ({cmd})"
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 尝试下一个命令
                    continue;
                }
            }

            return new ComponentStatus
            {
                Name = "PIP",
                Status = "warning",
                Message = "PIP 未安装（可选，用于 Python 包管理）"
            };
        }

        /// <summary>
        /// 检查 AI API Key 配置状态
        /// </summary>
        private Task<ComponentStatus> CheckApiKeyAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var providers = _aiConfigService.GetProviders();
                if (providers.Count == 0)
                {
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "API Key",
                        Status = "warning",
                        Message = "未配置任何 AI 提供商"
                    });
                }

                var mainProvider = providers.FirstOrDefault(p => p.IsMain) ?? providers.First();
                var apiKey = _aiConfigService.GetApiKey(mainProvider.Id);
                var isLocalProvider = HealthCheckHelper.IsLocalAiProvider(mainProvider);

                if (string.IsNullOrEmpty(apiKey))
                {
                    // 本地 AI 服务不需要 API Key
                    if (isLocalProvider)
                    {
                        return Task.FromResult(new ComponentStatus
                        {
                            Name = "API Key",
                            Status = "healthy",
                            Message = $"主提供商 {mainProvider.Name} 为本地服务，无需 API Key"
                        });
                    }

                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "API Key",
                        Status = "critical",
                        Message = $"主提供商 {mainProvider.Name} 未配置 API Key"
                    });
                }

                var summaries = _aiConfigService.GetApiKeySummaries();
                var summary = summaries.FirstOrDefault(s => 
                    s.ProviderId.Equals(mainProvider.Id, StringComparison.OrdinalIgnoreCase));

                if (summary?.HasApiKey == true)
                {
                    var scheme = summary.Scheme switch
                    {
                        EncryptionScheme.AesGcm => "AES加密",
                        _ => "已加密"
                    };
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "API Key",
                        Status = "healthy",
                        Message = $"{mainProvider.Name} API Key 已配置（{scheme}）"
                    });
                }

                return Task.FromResult(new ComponentStatus
                {
                    Name = "API Key",
                    Status = "critical",
                    Message = $"{mainProvider.Name} 未配置 API Key"
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "API Key 检测失败");
                return Task.FromResult(new ComponentStatus
                {
                    Name = "API Key",
                    Status = "warning",
                    Message = "API Key 检测异常"
                });
            }
        }

        /// <summary>
        /// 检查知识库路径配置状态
        /// </summary>
        private Task<ComponentStatus> CheckVaultPathAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var vaultPath = _vaultSettings.VaultPath;
                
                if (string.IsNullOrWhiteSpace(vaultPath))
                {
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "知识库",
                        Status = "critical",
                        Message = "未配置知识库路径。请前往设置页面配置 Obsidian 知识库路径"
                    });
                }

                if (!Directory.Exists(vaultPath))
                {
                    return Task.FromResult(new ComponentStatus
                    {
                        Name = "知识库",
                        Status = "critical",
                        Message = $"知识库路径不存在: {vaultPath}。该目录可能被删除或移动，请重新配置"
                    });
                }

                // 只要路径存在即视为有效，不再强制要求 .obsidian 目录或 .md 文件
                // 用户可以通过 WebUI 的"在 Obsidian 中打开"按钮来初始化该目录
                var hasObsidianDir = Directory.Exists(Path.Combine(vaultPath, ".obsidian"));
                var mdFiles = Directory.GetFiles(vaultPath, "*.md", SearchOption.TopDirectoryOnly);
                
                return Task.FromResult(new ComponentStatus
                {
                    Name = "知识库",
                    Status = "healthy",
                    Message = hasObsidianDir 
                        ? $"知识库已配置: {Path.GetFileName(vaultPath)} ({mdFiles.Length} 个文档)"
                        : $"知识库路径已配置: {Path.GetFileName(vaultPath)}。点击[在 Obsidian 中打开]按钮初始化"
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "知识库路径检测失败");
                return Task.FromResult(new ComponentStatus
                {
                    Name = "知识库",
                    Status = "warning",
                    Message = "知识库路径检测异常"
                });
            }
        }
    }
}

