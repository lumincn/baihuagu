using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class OllamaService
{
        #region Deploy

        public async Task PullModelAsync(DeployTaskStatusDto task, string modelName, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = $"pull {modelName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var customDir = localModelSettings.LocalModelDownloadDirectory;
            if (!string.IsNullOrEmpty(customDir))
            {
                psi.EnvironmentVariables["OLLAMA_MODELS"] = customDir;
            }

            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("无法启动 ollama pull 进程");

            var stdoutReader = Task.Run(async () =>
            {
                while (!process.HasExited && !ct.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (line == null) break;
                    ParsePullOutput(task, line);
                }
            }, ct);

            var stderrReader = Task.Run(async () =>
            {
                while (!process.HasExited && !ct.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync(ct);
                    if (line == null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line.Trim()}");
                    }
                }
            }, ct);

            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdoutReader, stderrReader);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ollama pull 退出码 {process.ExitCode}");
            }
        }

        private void ParsePullOutput(DeployTaskStatusDto task, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line.Trim()}");

            var progressMatch = Regex.Match(line, @"(\d+)%");
            if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var percent))
            {
                task.ProgressPercent = Math.Min(100, Math.Max(0, percent));
            }

            if (line.Contains("pulling manifest", StringComparison.OrdinalIgnoreCase))
                task.CurrentStep = "下载清单";
            else if (line.Contains("pulling", StringComparison.OrdinalIgnoreCase))
                task.CurrentStep = "下载模型文件";
            else if (line.Contains("verifying", StringComparison.OrdinalIgnoreCase))
                task.CurrentStep = "验证文件";
            else if (line.Contains("writing", StringComparison.OrdinalIgnoreCase))
                task.CurrentStep = "写入模型";
        }

        public async Task<bool> VerifyModelAsync(string modelName, CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "list",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0) return false;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                var modelBase = modelName.Split(':')[0];
                return output.Contains(modelBase, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        #endregion

}
