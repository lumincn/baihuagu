using Baihua.Contracts.Search;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

using Baihua.Data;
using Baihua.Data.Entities;

namespace Baihua.Family.Services
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
        private readonly Baihua.Core.Security.ApiKeyProtectionService _protectionService;
        private readonly ILogger<EmbeddingService> _logger;

        private const int MaxNotesToRerank = 50;

        public EmbeddingService(
            AiClientService aiClientService,
            AiSettingsService aiSettings,
            VaultSettingsService vaultSettings,
            IDbContextFactory<VaultDbContext> vaultDbFactory,
            IDbContextFactory<AIDbContext> aiDbFactory,
            Baihua.Core.Security.ApiKeyProtectionService protectionService,
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
                await RecordEmbeddingMetricAsync(providerId, modelName, sw.ElapsedMilliseconds, false, "Empty result");
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
        /// 纯向量检索：对知识库全部已索引笔记做余弦相似度排序（无需关键词命中）
        /// </summary>
        /// <param name="query">查询文本</param>
        /// <param name="vaultId">知识库 ID</param>
        /// <param name="vaultPath">知识库磁盘路径（用于读取笔记标题/预览）</param>
        /// <param name="topK">返回条数</param>
        public async Task<List<SearchResult>> VectorSearchAsync(string query, string vaultId, string vaultPath, int topK = 20)
        {
            var result = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(query) || !IsSemanticSearchEnabled())
                return result;

            try
            {
                var queryEmbedding = await GetEmbeddingAsync(query);
                if (queryEmbedding == null)
                    return result;

                // 1) 读出该知识库全部笔记向量
                List<(string NotePath, List<double> Vector)> vectors;
                using (var db = await _vaultDbFactory.CreateDbContextAsync())
                {
                    vectors = await db.NoteEmbeddings
                        .Where(e => e.VaultId == vaultId && e.Dimensions > 0)
                        .Select(e => new { e.NotePath, e.VectorJson })
                        .ToListAsync()
                        .ContinueWith(t => t.Result
                            .Select(x => (x.NotePath, DeserializeVector(x.VectorJson)))
                            .Where(x => x.Item2 != null)
                            .Select(x => (x.NotePath, x.Item2!))
                            .ToList());
                }

                if (vectors.Count == 0)
                {
                    _logger.LogInformation("纯向量检索：知识库 {VaultId} 无向量缓存，需先索引", vaultId);
                    return result;
                }

                // 2) 余弦相似度排序
                var scored = new List<(string NotePath, double Score)>();
                foreach (var (notePath, vector) in vectors)
                {
                    var sim = CosineSimilarity(queryEmbedding, vector);
                    if (sim > 0)
                        scored.Add((notePath, sim));
                }

                scored = scored.OrderByDescending(x => x.Score).Take(topK).ToList();

                // 3) 从磁盘读笔记构建结果（标题/预览/相对路径）
                foreach (var (notePath, score) in scored)
                {
                    var fullPath = System.IO.Path.Combine(vaultPath, notePath);
                    if (!System.IO.File.Exists(fullPath))
                        continue;

                    string content;
                    try { content = await System.IO.File.ReadAllTextAsync(fullPath); }
                    catch { continue; }

                    var title = System.IO.Path.GetFileNameWithoutExtension(fullPath);
                    var relativePath = notePath.Replace('\\', '/');
                    result.Add(new SearchResult
                    {
                        Id = title,
                        Title = title,
                        Path = relativePath,
                        Preview = ExtractFirstText(content),
                        Score = (int)Math.Round(score * 10),
                    });
                }

                _logger.LogDebug("纯向量检索完成：{Count} 条（topK={TopK}）", result.Count, topK);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "纯向量检索失败");
            }
            return result;
        }

        private static List<double>? DeserializeVector(string json)
        {
            try { return JsonSerializer.Deserialize<List<double>>(json); }
            catch { return null; }
        }

        /// <summary>提取笔记正文首段文字作为预览（剔除 frontmatter/标题）</summary>
        private static string ExtractFirstText(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder();
            var inFm = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (i == 0 && line == "---") { inFm = true; continue; }
                if (inFm && line == "---") { inFm = false; continue; }
                if (inFm) continue;
                if (line.StartsWith("#")) continue;          // 跳过标题
                if (string.IsNullOrEmpty(line)) { if (sb.Length > 0) break; continue; }
                sb.Append(line).Append(' ');
                if (sb.Length > 160) break;
            }
            var text = sb.ToString().Trim();
            return text.Length > 0 ? text : content.Replace("\n", " ").Trim();
        }

        /// <summary>
        /// 获取知识库已索引向量条数
        /// </summary>
        public async Task<int> GetIndexedCountAsync(string vaultId)
        {
            try
            {
                using var db = await _vaultDbFactory.CreateDbContextAsync();
                return await db.NoteEmbeddings.CountAsync(e => e.VaultId == vaultId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "查询向量索引数失败");
                return 0;
            }
        }

        /// <summary>
        /// 全库向量索引：对知识库所有 .md 笔记生成向量并缓存（纯向量检索的前提）
        /// </summary>
        public async Task<(int Indexed, int Failed)> IndexVaultAsync(string vaultId, string vaultPath, CancellationToken ct = default)
        {
            int indexed = 0, failed = 0;
            if (string.IsNullOrWhiteSpace(vaultId) || !Directory.Exists(vaultPath) || !IsSemanticSearchEnabled())
                return (0, 0);

            try
            {
                var files = Directory.GetFiles(vaultPath, "*.md", SearchOption.AllDirectories);
                _logger.LogInformation("向量索引开始：{VaultId} 共 {Count} 篇笔记", vaultId, files.Length);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var content = await File.ReadAllTextAsync(file, ct);
                        var title = Path.GetFileNameWithoutExtension(file);
                        var textToEmbed = $"{title}\n{ExtractFirstText(content)}".Trim();
                        if (string.IsNullOrEmpty(textToEmbed))
                            continue;

                        var relativePath = file.Substring(vaultPath.Length).TrimStart('\\', '/').Replace('\\', '/');
                        var embedding = await GetEmbeddingAsync(textToEmbed);
                        if (embedding != null)
                        {
                            await SaveNoteEmbeddingAsync(vaultId, relativePath, embedding);
                            indexed++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "索引笔记失败：{File}", file);
                        failed++;
                    }
                }

                _logger.LogInformation("向量索引完成：{VaultId} 成功 {Indexed} 失败 {Failed}", vaultId, indexed, failed);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "向量索引异常");
            }
            return (indexed, failed);
        }

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
