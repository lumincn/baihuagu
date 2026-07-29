using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Data.Entities;
using TaskRunner.Models;

namespace TaskRunner.Services;

public partial class LocalModelDeploymentService
{
        #region Deploy

        public async Task<DeployLocalModelResult> DeployAsync(DeployLocalModelRequest request)
        {
            var model = ModelDatabase.GetById(request.ModelId);
            if (model == null)
            {
                return new DeployLocalModelResult
                {
                    Success = false,
                    Message = $"未找到模型: {request.ModelId}"
                };
            }

            var taskId = Guid.NewGuid().ToString("N")[..12];
            var cts = new CancellationTokenSource();
            _taskCancellations[taskId] = cts;

            var taskStatus = new DeployTaskStatusDto
            {
                TaskId = taskId,
                ModelId = model.Id,
                ModelName = model.Name,
                Status = "pending",
                ProgressPercent = 0,
                CurrentStep = "准备部署",
                CreatedAt = DateTime.Now,
                Logs = new List<string> { $"[{DateTime.Now:HH:mm:ss}] 开始部署: {model.Name} ({model.OllamaModelName})" }
            };
            _tasks[taskId] = taskStatus;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (request.TargetTool.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        await DeployToOllamaAsync(taskStatus, model, cts.Token);
                    }
                    else if (request.TargetTool.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                    {
                        await DeployToLmStudioAsync(taskStatus, model, cts.Token);
                    }
                    else
                    {
                        throw new NotSupportedException($"不支持的部署工具: {request.TargetTool}");
                    }
                }
                catch (OperationCanceledException)
                {
                    taskStatus.Status = "failed";
                    taskStatus.ErrorMessage = "部署已取消";
                    taskStatus.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 部署已取消");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "模型部署失败: {ModelId}", model.Id);
                    taskStatus.Status = "failed";
                    taskStatus.ErrorMessage = ex.Message;
                    taskStatus.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}");
                }
                finally
                {
                    taskStatus.CompletedAt = DateTime.Now;
                    _taskCancellations.TryRemove(taskId, out _);
                }
            }, cts.Token);

            return new DeployLocalModelResult
            {
                Success = true,
                TaskId = taskId,
                Message = "部署任务已启动"
            };
        }

        private async Task DeployToOllamaAsync(DeployTaskStatusDto task, ModelEntry model, CancellationToken ct)
        {
            task.Status = "running";

            task.CurrentStep = "检查 Ollama 安装";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 检查 Ollama 安装...");
            var ollamaVersion = await _ollama.GetVersionAsync(ct);
            if (string.IsNullOrEmpty(ollamaVersion))
            {
                throw new InvalidOperationException(
                    "Ollama 未安装。请访问 https://ollama.com 下载安装。");
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Ollama 版本: {ollamaVersion}");

            task.CurrentStep = "启动 Ollama 服务";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 检查 Ollama 服务状态...");
            var running = await _autoStarter.TryEnsureRunningAsync("ollama", "http://localhost:11434/v1");
            if (!running)
            {
                throw new InvalidOperationException("Ollama 服务启动失败，请手动启动后重试。");
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Ollama 服务已就绪");

            var requiredBytes = (long)(model.SizeGiB * 1.2 * 1024 * 1024 * 1024);
            var availableBytes = _ollama.GetModelsDirFreeSpace();
            if (availableBytes > 0 && availableBytes < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"磁盘空间不足。模型需要约 {model.SizeGiB * 1.2:F1} GB，可用空间仅 {availableBytes / (1024.0 * 1024 * 1024):F1} GB。");
            }

            task.CurrentStep = "下载模型";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 开始下载: ollama pull {model.OllamaModelName}");
            await _ollama.PullModelAsync(task, model.OllamaModelName, ct);

            task.CurrentStep = "验证部署";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 验证模型...");
            var verified = await _ollama.VerifyModelAsync(model.OllamaModelName, ct);
            if (!verified)
            {
                throw new InvalidOperationException("模型下载完成但验证失败，请检查 Ollama 日志。");
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 模型验证通过");

            task.CurrentStep = "配置 AI Provider";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 添加到 AI 服务商配置...");
            ConfigureOllamaProvider(model);
            task.AutoConfiguredProvider = true;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] AI Provider 配置完成");

            task.Status = "completed";
            task.ProgressPercent = 100;
            task.CurrentStep = "部署完成";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] ✅ 部署成功！模型已可用。");
        }

        private async Task DeployToLmStudioAsync(DeployTaskStatusDto task, ModelEntry model, CancellationToken ct)
        {
            task.Status = "running";

            task.CurrentStep = "检查 LM Studio";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 检查 LM Studio 安装...");
            var lmsVersion = await _lmStudio.GetVersionAsync(ct);
            if (string.IsNullOrEmpty(lmsVersion))
            {
                throw new InvalidOperationException(
                    "LM Studio CLI (lms) 未安装。请访问 https://lmstudio.ai 下载安装，并确保 lms 命令在 PATH 中。");
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] LM Studio CLI: {lmsVersion}");

            task.CurrentStep = "启动 LM Studio 服务";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 检查 LM Studio 服务状态...");
            var running = await _autoStarter.TryEnsureRunningAsync("lmstudio", "http://localhost:1234/v1");
            if (!running)
            {
                throw new InvalidOperationException("LM Studio 服务启动失败，请手动启动后重试。");
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] LM Studio 服务已就绪");

            var requiredBytes = (long)(model.SizeGiB * 1.2 * 1024 * 1024 * 1024);
            var availableBytes = _lmStudio.GetModelsDirFreeSpace();
            if (availableBytes > 0 && availableBytes < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"磁盘空间不足。模型需要约 {model.SizeGiB * 1.2:F1} GB，可用空间仅 {availableBytes / (1024.0 * 1024 * 1024):F1} GB。");
            }

            var searchName = model.LmStudioSearchName ?? model.Id;
            var preferredSource = _localModelSettings.PreferredDownloadSource;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 搜索名称: {searchName}, 下载源偏好: {preferredSource}");

            task.CurrentStep = "下载模型";
            await _lmStudioDownload.PullModelAsync(task, model, preferredSource, ct);

            task.CurrentStep = "验证部署";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 验证模型...");
            var verified = await _lmStudioDownload.VerifyModelAsync(searchName, ct);
            if (!verified)
            {
                task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ 无法自动验证模型是否下载成功，请检查 LM Studio 界面。");
            }
            else
            {
                task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 模型验证通过");
            }

            task.CurrentStep = "配置 AI Provider";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 添加到 AI 服务商配置...");
            ConfigureLmStudioProvider(model);
            task.AutoConfiguredProvider = true;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] AI Provider 配置完成");

            task.Status = "completed";
            task.ProgressPercent = 100;
            task.CurrentStep = "部署完成";
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] ✅ 部署成功！模型已可用。");
        }

        #endregion

}
