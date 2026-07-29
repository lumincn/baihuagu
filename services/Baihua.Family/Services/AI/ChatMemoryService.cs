using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Controllers;
using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services;

/// <summary>
/// 对话记忆服务：三层记忆系统
/// 1. Token 预算截断：根据模型上下文窗口动态截断历史
/// 2. 摘要压缩：将早期对话压缩为摘要，保留语义
/// 3. 语义检索：通过向量检索与当前问题相关的历史记忆
/// </summary>
public partial class ChatMemoryService
{
    private readonly AiClientService _aiClientService;
    private readonly EmbeddingService _embeddingService;
    private readonly DefaultPromptProvider _scenePromptService;
    private readonly IDbContextFactory<AIDbContext> _dbFactory;
    private readonly ILogger<ChatMemoryService> _logger;

    // 触发摘要压缩的阈值：超过此轮数时，将早期对话压缩为摘要
    private const int SummaryThreshold = 10;

    // 摘要后保留的最近完整对话轮数
    private const int RecentRoundsToKeep = 5;

    // 语义检索返回的最大记忆条数
    private const int MaxRetrievedMemories = 3;

    // 每个会话最多保留的记忆条数
    private const int MaxMemoryEntriesPerSession = 200;

    public ChatMemoryService(
        AiClientService aiClientService,
        EmbeddingService embeddingService,
        DefaultPromptProvider scenePromptService,
        IDbContextFactory<AIDbContext> dbFactory,
        ILogger<ChatMemoryService> logger)
    {
        _aiClientService = aiClientService;
        _embeddingService = embeddingService;
        _scenePromptService = scenePromptService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

}
