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
        #region Model Lifecycle

        public async Task<bool> LoadModelAsync(string toolId, string modelName, int keepAliveMinutes, CancellationToken ct = default)
        {
            bool result;
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var running = await _autoStarter.TryEnsureRunningAsync("ollama", "http://localhost:11434/v1");
                if (!running)
                    throw new InvalidOperationException("Ollama 服务未运行且自动启动失败，请手动启动后重试。");
                result = await _ollama.LoadModelAsync(modelName, keepAliveMinutes, ct);
            }
            else if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
            {
                var running = await _autoStarter.TryEnsureRunningAsync("lmstudio", "http://localhost:1234/v1");
                if (!running)
                    throw new InvalidOperationException("LM Studio 服务未运行且自动启动失败，请手动启动后重试。");
                result = await _lmStudio.LoadModelAsync(modelName, ct);
            }
            else if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
            {
                result = await _llamaCpp.LoadModelAsync(modelName, ct);
            }
            else
            {
                return false;
            }

            if (result) InvalidateCaches();
            return result;
        }

        public async Task<bool> UnloadModelAsync(string toolId, string modelName, CancellationToken ct = default)
        {
            bool result;
            if (toolId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                result = await _ollama.UnloadModelAsync(modelName, ct);
            else if (toolId.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                result = await _lmStudio.UnloadModelAsync(modelName, ct);
            else if (toolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
                result = await _llamaCpp.UnloadModelAsync(ct);
            else
                return false;

            if (result) InvalidateCaches();
            return result;
        }

        #endregion

        #region LlamaCpp Service Control

        public async Task<LocalAiServiceStatusDto> StartLlamaCppAsync(CancellationToken ct = default)
        {
            var result = await _llamaCpp.StartAsync(ct);
            if (result.IsRunning) InvalidateCaches();
            return result;
        }

        public async Task<bool> StopLlamaCppAsync(CancellationToken ct = default)
        {
            var result = await _llamaCpp.StopAsync(ct);
            if (result) InvalidateCaches();
            return result;
        }

        #endregion

        #region Download Sources

        public List<DownloadSourceDto> GetDownloadSources()
        {
            return new List<DownloadSourceDto>
            {
                new()
                {
                    Id = "ollama",
                    Name = "Ollama Library",
                    BaseUrl = "https://ollama.com/library",
                    IsChinaMirror = false,
                    IsAvailable = true
                },
                new()
                {
                    Id = "huggingface",
                    Name = "Hugging Face",
                    BaseUrl = "https://huggingface.co",
                    IsChinaMirror = false,
                    IsAvailable = true
                },
                new()
                {
                    Id = "hf-mirror",
                    Name = "Hugging Face 镜像 (hf-mirror.com)",
                    BaseUrl = "https://hf-mirror.com",
                    IsChinaMirror = true,
                    IsAvailable = true
                },
                new()
                {
                    Id = "modelscope",
                    Name = "魔搭社区 (ModelScope)",
                    BaseUrl = "https://modelscope.cn",
                    IsChinaMirror = true,
                    IsAvailable = true
                }
            };
        }

        #endregion

        #region Cache Management

        private void InvalidateCaches()
        {
            _cache.Remove(RunningModelsCacheKey);
            _cache.Remove(ToolsCacheKey);
            _logger.LogDebug("本地模型缓存已清除");
        }

        #endregion
}
