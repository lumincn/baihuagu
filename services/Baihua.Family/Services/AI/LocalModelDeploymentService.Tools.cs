using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Baihua.Contracts.LocalModels;
using Baihua.Contracts.OpenClaw;
using Baihua.Data.Entities;
using Baihua.AI.Provider;
using Baihua.Family.Models;

namespace Baihua.Family.Services;

public partial class LocalModelDeploymentService
{
        #region Tool Detection

        public async Task<List<LocalToolInfoDto>> GetLocalToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && _cache.TryGetValue(ToolsCacheKey, out List<LocalToolInfoDto>? cached) && cached != null)
            {
                _logger.LogDebug("本地工具状态命中缓存");
                return cached;
            }

            var tools = new List<LocalToolInfoDto>();

            // Ollama
            var ollamaVersion = await _ollama.GetVersionAsync(ct);
            var ollamaRunning = false;
            if (!string.IsNullOrEmpty(ollamaVersion))
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var response = await client.GetAsync("http://localhost:11434/", ct);
                    ollamaRunning = response.IsSuccessStatusCode || (int)response.StatusCode < 500;
                }
                catch (Exception ex) { _logger.LogDebug(ex, "检测服务运行状态失败"); }
            }

            tools.Add(new LocalToolInfoDto
            {
                Id = "ollama",
                Name = "Ollama",
                IsInstalled = !string.IsNullOrEmpty(ollamaVersion),
                Version = ollamaVersion,
                IsRunning = ollamaRunning,
                DefaultModelPath = _ollama.GetDefaultModelsPath(),
                InstallGuideUrl = "https://ollama.com/download"
            });

            // LM Studio
            var lmsVersion = await _lmStudio.GetVersionAsync(ct);
            var lmstudioRunning = false;
            if (!string.IsNullOrEmpty(lmsVersion))
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var response = await client.GetAsync("http://localhost:1234/v1/models", ct);
                    lmstudioRunning = response.IsSuccessStatusCode;
                }
                catch (Exception ex) { _logger.LogDebug(ex, "检测服务运行状态失败"); }
            }

            tools.Add(new LocalToolInfoDto
            {
                Id = "lmstudio",
                Name = "LM Studio",
                IsInstalled = !string.IsNullOrEmpty(lmsVersion),
                Version = lmsVersion,
                IsRunning = lmstudioRunning,
                DefaultModelPath = LmStudioDownloadService.GetDefaultModelsPath(),
                InstallGuideUrl = "https://lmstudio.ai/download"
            });

            // llama.cpp
            var (llamaCppInstalled, llamaCppVersion, llamaCppRunning, llamaCppModelPath) = await _llamaCpp.GetToolInfoAsync(ct);
            tools.Add(new LocalToolInfoDto
            {
                Id = "llamacpp",
                Name = "llama.cpp",
                IsInstalled = llamaCppInstalled,
                Version = llamaCppVersion,
                IsRunning = llamaCppRunning,
                DefaultModelPath = llamaCppModelPath,
                InstallGuideUrl = "https://github.com/ggerganov/llama.cpp"
            });

            // OpenVINO GenAI（本地视觉模型，对接 vision_server.py）
            var (ovInstalled, ovVersion, ovRunning, ovModelPath) = await _openVino.GetToolInfoAsync(ct);
            tools.Add(new LocalToolInfoDto
            {
                Id = "openvino",
                Name = "OpenVINO GenAI",
                IsInstalled = ovInstalled,
                Version = ovVersion,
                IsRunning = ovRunning,
                DefaultModelPath = ovModelPath,
                InstallGuideUrl = "https://docs.openvino.ai"
            });

            _cache.Set(ToolsCacheKey, tools, TimeSpan.FromSeconds(300));
            return tools;
        }

        #endregion

        #region Running Model Management

        public async Task<List<RunningModelDto>> GetRunningModelsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && _cache.TryGetValue(RunningModelsCacheKey, out List<RunningModelDto>? cached) && cached != null)
            {
                _logger.LogDebug("运行中模型命中缓存");
                return cached;
            }

            var results = new List<RunningModelDto>();

            // 4 个工具并行探测（各自内部有超时兑底，串行会把超时叠加到 6s+）
            var ollamaTask = SafeRunningAsync(() => _ollama.GetRunningModelsAsync(ct), "Ollama");
            var lmTask = SafeRunningAsync(() => _lmStudio.GetRunningModelsAsync(ct), "LM Studio");
            var llamaTask = SafeRunningAsync(() => _llamaCpp.GetRunningModelsAsync(ct), "llama.cpp");
            var ovTask = SafeRunningAsync(() => _openVino.GetRunningModelsAsync(ct), "OpenVINO");
            await Task.WhenAll(ollamaTask, lmTask, llamaTask, ovTask);

            results.AddRange(await ollamaTask);
            results.AddRange(await lmTask);
            results.AddRange(await llamaTask);
            results.AddRange(await ovTask);

            _cache.Set(RunningModelsCacheKey, results);
            return results;
        }

        private async Task<List<RunningModelDto>> SafeRunningAsync(Func<Task<List<RunningModelDto>>> probe, string name)
        {
            try
            {
                return await probe();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取 {Tool} 运行中模型失败", name);
                return new List<RunningModelDto>();
            }
        }

        #endregion

        #region Available Models

        public async Task<List<string>> GetAvailableModelsAsync(string toolId, CancellationToken ct = default)
        {
            var cacheKey = "available_" + toolId.ToLowerInvariant();
            if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
            {
                _logger.LogDebug("可用模型列表命中缓存: {Key}", cacheKey);
                return cached;
            }

            List<string> result;
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                result = await _ollama.GetAvailableModelsAsync(ct);
            else if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                result = await _lmStudio.GetAvailableModelsAsync(ct);
            else if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
                result = await _llamaCpp.GetAvailableModelsAsync(ct);
            else if (toolId.Equals("openvino", StringComparison.OrdinalIgnoreCase))
                result = await _openVino.GetAvailableModelsAsync(ct);
            else
                result = new List<string>();

            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(300));
            return result;
        }

        #endregion

        #region Downloaded Model Management

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(DownloadedModelsCacheKey, out List<DownloadedModelDto>? cached) && cached != null)
            {
                _logger.LogDebug("已下载模型命中缓存");
                return cached;
            }

            var runningModels = await GetRunningModelsAsync(forceRefresh: false, ct);

            // 先拿工具状态（缓存），跳过未安装工具的下载扫描（避免未监听端口的 2s+ 连接延迟）
            var tools = await GetLocalToolsAsync(ct: ct);
            var installedIds = tools.Where(t => t.IsInstalled).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 4 个工具并行扫描下载目录（避免串行把 2s 连接探测延迟叠加）
            var ollamaTask = installedIds.Contains("ollama") ? _ollama.GetDownloadedModelsAsync(runningModels, ct) : Task.FromResult(new List<DownloadedModelDto>());
            var lmTask = installedIds.Contains("lmstudio") ? _lmStudio.GetDownloadedModelsAsync(runningModels, ct) : Task.FromResult(new List<DownloadedModelDto>());
            var llamaTask = installedIds.Contains("llamacpp") ? _llamaCpp.GetDownloadedModelsAsync(runningModels, ct) : Task.FromResult(new List<DownloadedModelDto>());
            var ovTask = _openVino.GetDownloadedModelsAsync(ct);
            await Task.WhenAll(ollamaTask, lmTask, llamaTask, ovTask);

            var results = new List<DownloadedModelDto>();
            results.AddRange(await ollamaTask);
            results.AddRange(await lmTask);
            results.AddRange(await llamaTask);
            results.AddRange(await ovTask);

            _cache.Set(DownloadedModelsCacheKey, results, TimeSpan.FromSeconds(300));
            return results;
        }

        public async Task<bool> DeleteModelAsync(string toolId, string modelName, CancellationToken ct = default)
        {
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var result = await _ollama.DeleteModelAsync(modelName, ct);
                if (result)
                {
                    RemoveModelFromProviderConfig("ollama", modelName);
                }
                return result;
            }

            if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("LM Studio 模型删除暂不支持，请手动删除模型文件");
                return false;
            }

            if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("llama.cpp 模型删除暂不支持，请手动删除 .gguf 文件");
                return false;
            }

            if (toolId.Equals("openvino", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("OpenVINO 模型删除暂不支持，请手动删除模型目录");
                return false;
            }

            return false;
        }

        public async Task<ModelDetailsDto?> GetModelDetailsAsync(string toolId, string modelName, CancellationToken ct = default)
        {
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return await _ollama.GetModelDetailsAsync(modelName, ct);

            if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                return await _lmStudio.GetModelDetailsAsync(modelName, ct);

            if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
                return await _llamaCpp.GetModelDetailsAsync(modelName, ct);

            return null;
        }

        #endregion

}
