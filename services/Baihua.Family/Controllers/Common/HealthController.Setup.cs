using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Health;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

public partial class HealthController
{
        [HttpPost("setup/openclaw")]
        public async Task<ActionResult<dynamic>> SetupOpenClaw(CancellationToken cancellationToken)
        {
            try
            {
                // 1. 检查 openclaw 是否已安装
                var checkPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "openclaw",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var checkProcess = System.Diagnostics.Process.Start(checkPsi);
                if (checkProcess == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "OpenClaw 未安装。请运行: npm install -g openclaw"
                    });
                }

                await checkProcess.WaitForExitAsync(cancellationToken);
                if (checkProcess.ExitCode != 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "OpenClaw 安装异常，无法执行配置"
                    });
                }

                var version = await checkProcess.StandardOutput.ReadToEndAsync(cancellationToken);

                // 2. 后台运行 openclaw doctor --fix（不阻塞等待完成）
                var doctorPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "openclaw",
                    Arguments = "doctor --fix",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var doctorProcess = System.Diagnostics.Process.Start(doctorPsi);
                if (doctorProcess == null)
                {
                    return StatusCode(500, new { success = false, message = "无法启动 openclaw doctor" });
                }

                // 异步读取输出，不等待进程退出（doctor 可能需要较长时间）
                var doctorTask = Task.Run(async () =>
                {
                    var stdout = await doctorProcess.StandardOutput.ReadToEndAsync();
                    var stderr = await doctorProcess.StandardError.ReadToEndAsync();
                    await doctorProcess.WaitForExitAsync();
                    return (stdout, stderr, doctorProcess.ExitCode);
                }, cancellationToken);

                // 3. 获取模型列表（快速操作）
                var modelsPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "openclaw",
                    Arguments = "models list --json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var modelsJson = "";
                using var modelsProcess = System.Diagnostics.Process.Start(modelsPsi);
                if (modelsProcess != null)
                {
                    using var modelsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    modelsCts.CancelAfter(TimeSpan.FromSeconds(10));
                    try
                    {
                        await modelsProcess.WaitForExitAsync(modelsCts.Token);
                        modelsJson = await modelsProcess.StandardOutput.ReadToEndAsync(cancellationToken);
                    }
                    catch { /* ignore timeout */ }
                }

                // 尝试等待 doctor 最多 15 秒获取即时结果
                var doctorCompleted = doctorTask.Wait(TimeSpan.FromSeconds(15));
                var (doctorStdout, doctorStderr, exitCode) = doctorCompleted
                    ? doctorTask.Result
                    : ("", "", -1);

                return Ok(new
                {
                    success = exitCode == 0 || !doctorCompleted,
                    version = version.Trim(),
                    doctorCompleted,
                    doctorExitCode = exitCode,
                    doctorOutput = doctorStdout.Trim(),
                    doctorErrors = doctorStderr.Trim(),
                    modelsJson = modelsJson.Trim(),
                    message = doctorCompleted
                        ? (exitCode == 0 ? "OpenClaw 配置修复完成" : "OpenClaw doctor 返回非零退出码")
                        : "OpenClaw 配置修复已在后台启动（可能需要 30-60 秒完成）"
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, new { success = false, message = "OpenClaw 配置超时（超过 60 秒）" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenClaw 配置失败");
                return StatusCode(500, new { success = false, message = $"配置失败: {ex.Message}" });
            }
        }
}
