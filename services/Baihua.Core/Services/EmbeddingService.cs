using TaskRunner.Contracts.Search;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

using TaskRunner.Data;
using TaskRunner.Data.Entities;

namespace TaskRunner.Services
{
    /// <summary>
    /// 语义向量服务：基于 SQLite BLOB 缓存 + IEmbeddingGenerator 抽象，对关键词搜索结果按相似度重排
    /// </summary>
    public class EmbeddingService
    {
        private readonly AiClientService _aiClientService;
        private readonly AiSettingsService _aiSettings;
        private readonly VaultSettingsService _vaultSettings;
        private readonly IDbContextFactory<VaultDbContext> _vaultDbFactory;
        private readonly IDbContextFactory<AIDbContext> _aiDbFactory;
        private readonly TaskRunner.Core.Shared.Security.ApiKeyProtectionService _protectionService;
        private readonly ILogger<EmbeddingService> _logger;

        private const int MaxNotesToRerank = 50;

        public EmbeddingService(
            AiClientService aiClientService,
            AiSettingsService aiSettings,
            VaultSettingsService vaultSettings,
            IDbContextFactory<VaultDbContext> vaultDbFactory,
            IDbContextFactory<AIDbContext> aiDbFactory,
            TaskRunner.Core.Shared.Security.ApiKeyProtectionService protectionService,
            ILogger<EmbeddingService> logger)
        {
            _aiClientService = aiClientService;
            _aiSettings = aiSettings;
            _vaultSettings = vaultSettings;
            _vaultDbFactory = vaultDbFactory;
            _aiDbFactory = aiDbFactory;
            _protectionService = protectionService;
            _logger = logger;
        }

        /// <summary>
        /// 获取当前活跃知识库 ID
        /// </summary>
        private string? GetActiveVaultId()
        {
            try
            {
                return _vaultSettings.GetActiveVault()?.Id;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取语义搜索配置（优先 EmbeddingConfig 表）
        /// </summary>
        public bool IsSemanticSearchEnabled()
        {
            var config = GetEmbeddingConfig();
            if (config != null)
                return !string.IsNullOrEmpty(config.BaseUrl) && !string.IsNullOrEmpty(config.Model);
            return !string.IsNullOrEmpty(_aiSettings.SemanticEmbeddingUrl) && 
                   !string.IsNullOrEmpty(_aiSettings.SemanticEmbeddingModel);
        }

        private EmbeddingConfig? GetEmbeddingConfig()
        {
            try
            {
                using var db = _aiDbFactory.CreateDbContext();
                return db.EmbeddingConfigs.OrderBy(e => e.Id).FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取 EmbeddingConfig 失败");
                return null;
            }
        }

        /// <summary>
        /// 调用 Embedding API 获取向量
        /// </summary>
        public async Task<List<double>?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (!IsSemanticSearchEnabled())
                return null;

            var sw = Stopwatch.StartNew();
            try
            {
                var config = GetEmbeddingConfig();
                IEmbeddingGenerator<string, Embedding<float>> generator;
                string providerId = "embedding";
                string modelName;

                if (config != null && config.IsEnabled && !string.IsNullOrEmpty(config.BaseUrl) && !string.IsNullOrEmpty(config.Model))
                {
                    string? apiKey = null;
                    if (!string.IsNullOrEmpty(config.EncryptedApiKey))
                    {
                        try { apiKey = _protectionService.Decrypt(config.EncryptedApiKey); }
                        catch (Exception ex) { _logger.LogDebug(ex, "操作失败"); }
                    }
                    generator = _aiClientService.CreateEmbeddingGenerator(config.BaseUrl, config.Model, apiKey);
                    providerId = config.ProviderId;
                    modelName = config.Model;
                }
                else
                {
                    generator = _aiClientService.CreateEmbeddingGenerator();
                    modelName = _aiSettings.SemanticEmbeddingModel;
                }

                var result = await generator.GenerateAsync([text.Trim()]);
                sw.Stop();

                if (result.Count > 0)
                {
                    await RecordEmbeddingMetricAsync(providerId, modelName, sw.ElapsedMilliseconds, true, null);
                    return result[0].Vector.ToArray().Select(v => (double)v).ToList();
                }

                sw.Stop();
                await RecordEmbeddingMetricAsync(providerId, modelName, sw.ElapsedMilliseconds, false, "返回空结果");
                return null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                await RecordEmbeddingMetricAsync("embedding", _aiSettings.SemanticEmbeddingModel, sw.ElapsedMilliseconds, false, ex.Message);
                _logger.LogDebug(ex, "获取 Embedding 失败");
                return null;
            }
        }

        private async Task RecordEmbeddingMetricAsync(string providerId, string modelName, long latencyMs, bool isSuccess, string? errorMessage)
        {
            try
            {
                var providers = _aiSettings.GetAiProviders();
                var matchedProvider = providers.FirstOrDefault(p =>
                    p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));

                using var aiDb = await _aiDbFactory.CreateDbContextAsync();
                aiDb.AiUsageMetrics.Add(new AiUsageMetric
                {
                    CalledAt = DateTime.UtcNow,
                    ProviderId = matchedProvider?.Id ?? providerId,
                    ProviderName = matchedProvider?.Name ?? providerId,
                    ModelId = modelName,
                    Operation = "embedding",
                    LatencyMs = latencyMs,
                    IsSuccess = isSuccess,
                    ErrorMessage = errorMessage,
                });
                await aiDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "记录 Embedding 指标失败（不影响主流程）");
            }
        }

        /// <summary>
        /// 对搜索结果按语义相似度重排
        /// </summary>
        public async Task<List<SearchResult>> RerankBySimilarityAsync(
            string query, 
            List<SearchResult> results)
        {
            if (results.Count == 0 || !IsSemanticSearchEnabled())
                return results;

            try
            {
                var queryEmbedding = await GetEmbeddingAsync(query);
                if (queryEmbedding == null)
                    return results;

                var toRerank = results.Count > MaxNotesToRerank
                    ? results.Take(MaxNotesToRerank).ToList()
                    : results;
                
                var rest = results.Count > MaxNotesToRerank
                    ? results.Skip(MaxNotesToRerank).ToList()
                    : new List<SearchResult>();

                var scoredResults = new List<(SearchResult result, double score)>();
                
                foreach (var result in toRerank)
                {
                    var noteEmbedding = await GetNoteEmbeddingAsync(result.Path, result.Title, result.Preview);
                    
                    if (noteEmbedding != null)
                    {
                        var similarity = CosineSimilarity(queryEmbedding, noteEmbedding);
                        scoredResults.Add((result, similarity));
                    }
                    else
                    {
                        scoredResults.Add((result, result.Score));
                    }
                }

                var reranked = scoredResults
                    .OrderByDescending(x => x.score)
                    .Select(x => 
                    {
                        x.result.Score = (int)(x.score * 10);
                        return x.result;
                    })
                    .ToList();

                reranked.AddRange(rest);

                _logger.LogDebug("语义重排完成：{Count} 条结果", reranked.Count);
                return reranked;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "语义重排失败，返回原顺序");
                return results;
            }
        }

        /// <summary>
        /// 获取笔记的向量（从 SQLite 缓存或计算）
        /// </summary>
        private async Task<List<double>?> GetNoteEmbeddingAsync(string path, string title, string preview)
        {
            var vaultId = GetActiveVaultId();
            if (string.IsNullOrEmpty(vaultId))
                return null;

            try
            {
                // 从 SQLite 读取
                using var db = await _vaultDbFactory.CreateDbContextAsync();
                var cached = await db.NoteEmbeddings
                    .FirstOrDefaultAsync(e => e.VaultId == vaultId && e.NotePath == path);

                if (cached != null)
                {
                    var vector = JsonSerializer.Deserialize<List<double>>(cached.VectorJson);
                    if (vector != null && vector.Count > 0)
                        return vector;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "从 SQLite 读取向量缓存失败");
            }

            // 计算新向量
            var textToEmbed = $"{title}\n{preview}".Trim();
            if (string.IsNullOrEmpty(textToEmbed))
                return null;

            var embedding = await GetEmbeddingAsync(textToEmbed);
            if (embedding != null)
            {
                await SaveNoteEmbeddingAsync(vaultId, path, embedding);
            }

            return embedding;
        }

        /// <summary>
        /// 保存笔记向量到 SQLite
        /// </summary>
        private async Task SaveNoteEmbeddingAsync(string vaultId, string path, List<double> vector)
        {
            try
            {
                using var db = await _vaultDbFactory.CreateDbContextAsync();
                var existing = await db.NoteEmbeddings
                    .FirstOrDefaultAsync(e => e.VaultId == vaultId && e.NotePath == path);

                var json = JsonSerializer.Serialize(vector);

                if (existing != null)
                {
                    existing.VectorJson = json;
                    existing.Dimensions = vector.Count;
                    existing.UpdatedAt = DateTime.UtcNow;
                    db.NoteEmbeddings.Update(existing);
                }
                else
                {
                    db.NoteEmbeddings.Add(new NoteEmbedding
                    {
                        VaultId = vaultId,
                        NotePath = path,
                        VectorJson = json,
                        Dimensions = vector.Count,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "保存向量缓存到 SQLite 失败（不影响主流程）");
            }
        }

        /// <summary>
        /// 计算余弦相似度
        /// </summary>
        private static double CosineSimilarity(List<double> a, List<double> b)
        {
            if (a.Count != b.Count || a.Count == 0)
                return 0;

            double dotProduct = 0;
            double magnitudeA = 0;
            double magnitudeB = 0;

            for (int i = 0; i < a.Count; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            magnitudeA = Math.Sqrt(magnitudeA);
            magnitudeB = Math.Sqrt(magnitudeB);

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0;

            return dotProduct / (magnitudeA * magnitudeB);
        }
    }
}
