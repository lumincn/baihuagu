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

            _cache.Set(ToolsCacheKey, tools, TimeSpan.FromSeconds(10));
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

            try
            {
                var ollamaModels = await _ollama.GetRunningModelsAsync(ct);
                results.AddRange(ollamaModels);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取 Ollama 运行中模型失败");
            }

            try
            {
                var lmModels = await _lmStudio.GetRunningModelsAsync(ct);
                results.AddRange(lmModels);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取 LM Studio 运行中模型失败");
            }

            try
            {
                var llamaModels = await _llamaCpp.GetRunningModelsAsync(ct);
                results.AddRange(llamaModels);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取 llama.cpp 运行中模型失败");
            }

            _cache.Set(RunningModelsCacheKey, results);
            return results;
        }

        #endregion

        #region Available Models

        public async Task<List<string>> GetAvailableModelsAsync(string toolId, CancellationToken ct = default)
        {
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return await _ollama.GetAvailableModelsAsync(ct);

            if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                return await _lmStudio.GetAvailableModelsAsync(ct);

            if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
                return await _llamaCpp.GetAvailableModelsAsync(ct);

            return new List<string>();
        }

        #endregion

        #region Downloaded Model Management

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(CancellationToken ct = default)
        {
            var runningModels = await GetRunningModelsAsync(forceRefresh: true, ct);
            var results = new List<DownloadedModelDto>();

            results.AddRange(await _ollama.GetDownloadedModelsAsync(runningModels, ct));
            results.AddRange(await _lmStudio.GetDownloadedModelsAsync(runningModels, ct));
            results.AddRange(await _llamaCpp.GetDownloadedModelsAsync(runningModels, ct));

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
