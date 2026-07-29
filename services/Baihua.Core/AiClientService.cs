using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;
using OpenAI;
using System.ClientModel;
using Microsoft.Extensions.Logging;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Models;

namespace Baihua.Family.Services;
    /// <summary>
    /// 统一的 AI 客户端服务：基于 Microsoft.Extensions.AI 抽象层，
    /// 为任意 OpenAI 兼容提供商创建 IChatClient 和 IEmbeddingGenerator。
    /// </summary>
    public partial class AiClientService
    {
        private readonly AiSettingsService _aiSettings;
        private readonly LocalAiAutoStarter _autoStarter;
        private readonly IDbContextFactory<AIDbContext> _dbFactory;
        private readonly AiMetricsService _metrics;
        private readonly IDistributedCache _cache;
        private readonly AnthropicAiClient _anthropicClient;
        private readonly ILogger<AiClientService> _logger;
        private readonly IStringLocalizer<SharedResources> _loc;
        private readonly ConcurrentDictionary<string, IChatClient> _chatClientCache = new();

        public AiClientService(
            AiSettingsService aiSettings,
            LocalAiAutoStarter autoStarter,
            IDbContextFactory<AIDbContext> dbFactory,
            AiMetricsService metrics,
            IDistributedCache cache,
            AnthropicAiClient anthropicClient,
            ILogger<AiClientService> logger,
            IStringLocalizer<SharedResources> loc)
        {
            _aiSettings = aiSettings;
            _autoStarter = autoStarter;
            _dbFactory = dbFactory;
            _metrics = metrics;
            _cache = cache;
            _anthropicClient = anthropicClient;
            _logger = logger;
            _loc = loc;
        }
}
