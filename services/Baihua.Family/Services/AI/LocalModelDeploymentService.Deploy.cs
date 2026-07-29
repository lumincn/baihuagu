using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Baihua.Contracts.LocalModels;
using Baihua.Contracts.OpenClaw;
using Baihua.Data.Entities;
using Baihua.Family.Models;

namespace Baihua.Family.Services;

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
                    Message = string.Format(_loc["LocalModel_ModelNotFound"], request.ModelId)
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
                CurrentStep = _loc["LocalModel_PreparingDeploy"],
                CreatedAt = DateTime.Now,
                Logs = new List<string> { $"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_DeployStarted"], model.Name, model.OllamaModelName)}" }
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
                        throw new NotSupportedException(string.Format(_loc["LocalModel_UnsupportedTool"], request.TargetTool));
                    }
                }
                catch (OperationCanceledException)
                {
                    taskStatus.Status = "failed";
                    taskStatus.ErrorMessage = _loc["LocalModel_DeployCancelled"];
                    taskStatus.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_DeployCancelled"]}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "模型部署失败: {ModelId}", model.Id);
                    taskStatus.Status = "failed";
                    taskStatus.ErrorMessage = ex.Message;
                    taskStatus.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_Error"], ex.Message)}");
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
                Message = _loc["LocalModel_DeployTaskStarted"]
            };
        }

        private async Task DeployToOllamaAsync(DeployTaskStatusDto task, ModelEntry model, CancellationToken ct)
        {
            task.Status = "running";

            task.CurrentStep = _loc["LocalModel_CheckOllama"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_CheckOllamaEllipsis"]}");
            var ollamaVersion = await _ollama.GetVersionAsync(ct);
            if (string.IsNullOrEmpty(ollamaVersion))
            {
                throw new InvalidOperationException(
                    _loc["LocalModel_OllamaNotInstalled"]);
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_OllamaVersion"], ollamaVersion)}");

            task.CurrentStep = _loc["LocalModel_StartOllama"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_CheckOllamaStatus"]}");
            var running = await _autoStarter.TryEnsureRunningAsync("ollama", "http://localhost:11434/v1");
            if (!running)
            {
                throw new InvalidOperationException(_loc["LocalModel_OllamaStartFailed"]);
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_OllamaReady"]}");

            var requiredBytes = (long)(model.SizeGiB * 1.2 * 1024 * 1024 * 1024);
            var availableBytes = _ollama.GetModelsDirFreeSpace();
            if (availableBytes > 0 && availableBytes < requiredBytes)
            {
                throw new InvalidOperationException(
                    string.Format(_loc["LocalModel_DiskSpaceInsufficient"], (model.SizeGiB * 1.2), (availableBytes / (1024.0 * 1024 * 1024))));
            }

            task.CurrentStep = _loc["LocalModel_DownloadModel"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_OllamaPullStarting"], model.OllamaModelName)}");
            await _ollama.PullModelAsync(task, model.OllamaModelName, ct);

            task.CurrentStep = _loc["LocalModel_VerifyDeploy"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_VerifyingModel"]}");
            var verified = await _ollama.VerifyModelAsync(model.OllamaModelName, ct);
            if (!verified)
            {
                throw new InvalidOperationException(_loc["LocalModel_OllamaVerifyFailed"]);
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_ModelVerified"]}");

            task.CurrentStep = _loc["LocalModel_ConfigureAiProvider"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_AddingProvider"]}");
            ConfigureOllamaProvider(model);
            task.AutoConfiguredProvider = true;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_ProviderConfigured"]}");

            task.Status = "completed";
            task.ProgressPercent = 100;
            task.CurrentStep = _loc["LocalModel_DeployComplete"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_DeploySuccess"]}");
        }

        private async Task DeployToLmStudioAsync(DeployTaskStatusDto task, ModelEntry model, CancellationToken ct)
        {
            task.Status = "running";

            task.CurrentStep = _loc["LocalModel_CheckLmStudio"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_CheckLmStudioEllipsis"]}");
            var lmsVersion = await _lmStudio.GetVersionAsync(ct);
            if (string.IsNullOrEmpty(lmsVersion))
            {
                throw new InvalidOperationException(
                    _loc["LocalModel_LmStudioNotInstalled"]);
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_LmStudioVersion"], lmsVersion)}");

            task.CurrentStep = _loc["LocalModel_StartLmStudio"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_CheckLmStudioStatus"]}");
            var running = await _autoStarter.TryEnsureRunningAsync("lmstudio", "http://localhost:1234/v1");
            if (!running)
            {
                throw new InvalidOperationException(_loc["LocalModel_LmStudioStartFailed"]);
            }
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_LmStudioReady"]}");

            var requiredBytes = (long)(model.SizeGiB * 1.2 * 1024 * 1024 * 1024);
            var availableBytes = _lmStudio.GetModelsDirFreeSpace();
            if (availableBytes > 0 && availableBytes < requiredBytes)
            {
                throw new InvalidOperationException(
                    string.Format(_loc["LocalModel_DiskSpaceInsufficient"], (model.SizeGiB * 1.2), (availableBytes / (1024.0 * 1024 * 1024))));
            }

            var searchName = model.LmStudioSearchName ?? model.Id;
            var preferredSource = _localModelSettings.PreferredDownloadSource;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(_loc["LocalModel_SearchNameSource"], searchName, preferredSource)}");

            task.CurrentStep = _loc["LocalModel_DownloadModel"];
            await _lmStudioDownload.PullModelAsync(task, model, preferredSource, ct);

            task.CurrentStep = _loc["LocalModel_VerifyDeploy"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_VerifyingModel"]}");
            var verified = await _lmStudioDownload.VerifyModelAsync(searchName, ct);
            if (!verified)
            {
                task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_LmStudioVerifyWarning"]}");
            }
            else
            {
                task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_ModelVerified"]}");
            }

            task.CurrentStep = _loc["LocalModel_ConfigureAiProvider"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_AddingProvider"]}");
            ConfigureLmStudioProvider(model);
            task.AutoConfiguredProvider = true;
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_ProviderConfigured"]}");

            task.Status = "completed";
            task.ProgressPercent = 100;
            task.CurrentStep = _loc["LocalModel_DeployComplete"];
            task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {_loc["LocalModel_DeploySuccess"]}");
        }

        #endregion

}
