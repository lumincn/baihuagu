using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Data.Entities;
using TaskRunner.Models;

namespace TaskRunner.Services;
    /// <summary>
    /// 本地模型部署服务：协调模型下载、部署和 AI Provider 自动配置
    /// </summary>
    public partial class LocalModelDeploymentService
    {
        private readonly ILogger<LocalModelDeploymentService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LocalAiAutoStarter _autoStarter;
        private readonly AiConfigService _aiConfigService;
        private readonly LocalModelSettingsService _localModelSettings;
        private readonly AiSettingsService _aiSettings;
        private readonly IMemoryCache _cache;

        private readonly OllamaService _ollama;
        private readonly LmStudioService _lmStudio;
        private readonly LmStudioDownloadService _lmStudioDownload;
        private readonly LlamaCppService _llamaCpp;

        // 内存中的部署任务状态
        private static readonly ConcurrentDictionary<string, DeployTaskStatusDto> _tasks = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCancellations = new();

        // 缓存配置
        private const string ToolsCacheKey = "local_tools";
        private const string RunningModelsCacheKey = "running_models";

        public LocalModelDeploymentService(
            ILogger<LocalModelDeploymentService> logger,
            IHttpClientFactory httpClientFactory,
            LocalAiAutoStarter autoStarter,
            AiConfigService aiConfigService,
            LocalModelSettingsService localModelSettings,
            AiSettingsService aiSettings,
            IMemoryCache cache,
            OllamaService ollama,
            LmStudioService lmStudio,
            LmStudioDownloadService lmStudioDownload,
            LlamaCppService llamaCpp)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _autoStarter = autoStarter;
            _aiConfigService = aiConfigService;
            _localModelSettings = localModelSettings;
            _aiSettings = aiSettings;
            _cache = cache;
            _ollama = ollama;
            _lmStudio = lmStudio;
            _lmStudioDownload = lmStudioDownload;
            _llamaCpp = llamaCpp;
        }

        #region Task Management

        public DeployTaskStatusDto? GetRunnerTaskStatus(string taskId)
        {
            return _tasks.TryGetValue(taskId, out var status) ? status : null;
        }

        public bool CancelTask(string taskId)
        {
            if (_taskCancellations.TryRemove(taskId, out var cts))
            {
                cts.Cancel();
                if (_tasks.TryGetValue(taskId, out var task))
                {
                    task.Status = "failed";
                    task.ErrorMessage = "用户取消";
                    task.CompletedAt = DateTime.Now;
                }
                return true;
            }
            return false;
        }

        public void CleanupOldTasks(TimeSpan maxAge)
        {
            var cutoff = DateTime.Now - maxAge;
            var keysToRemove = _tasks
                .Where(kv => kv.Value.Status is "completed" or "failed" && kv.Value.CompletedAt < cutoff)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _tasks.TryRemove(key, out _);
                _taskCancellations.TryRemove(key, out _);
            }
        }

        #endregion

}
