using TaskRunner.Contracts.Search;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;


namespace TaskRunner.Services
{
    /// <summary>
    /// 知识库笔记 FTS5 全文索引服务
    /// </summary>
    public class VaultNoteIndexer
    {
        private readonly IDbContextFactory<VaultDbContext> _dbContextFactory;
        private readonly ILogger<VaultNoteIndexer> _logger;

        public VaultNoteIndexer(IDbContextFactory<VaultDbContext> dbContextFactory, ILogger<VaultNoteIndexer> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        /// <summary>
        /// 确保 FTS5 虚拟表已创建
        /// </summary>
        public async Task EnsureFtsTableAsync(CancellationToken ct = default)
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
                await dbContext.Database.ExecuteSqlRawAsync(@"
                    CREATE VIRTUAL TABLE IF NOT EXISTS VaultNoteFts USING fts5(
                        title, content, vault_id UNINDEXED, file_path UNINDEXED,
                        tokenize='unicode61 remove_diacritics 2'
                    );
                ", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 FTS5 虚拟表失败");
                throw;
            }
        }

        /// <summary>
        /// 重建指定知识库的全文索引
        /// </summary>
        public async Task IndexVaultAsync(string vaultId, string vaultPath, CancellationToken ct = default)
        {
            if (!Directory.Exists(vaultPath))
            {
                _logger.LogWarning("知识库路径不存在：{Path}", vaultPath);
                return;
            }

            await EnsureFtsTableAsync(ct);

            using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

            // 1. 删除该知识库的旧索引
            await dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM VaultNoteFts WHERE vault_id = {0}",
                vaultId);

            // 2. 扫描所有 .md 文件
            var files = Directory.GetFiles(vaultPath, "*.md", SearchOption.AllDirectories);
            _logger.LogInformation("开始索引知识库 {VaultId}，共 {Count} 个文件", vaultId, files.Length);

            var indexed = 0;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var title = Path.GetFileNameWithoutExtension(file);
                    var relativePath = file.Substring(vaultPath.Length).TrimStart('/', '\\');
                    var content = await File.ReadAllTextAsync(file, ct);

                    // 3. 插入索引（批量插入性能更好，但 FTS5 不支持普通 INSERT 的批量优化）
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "INSERT INTO VaultNoteFts (title, content, vault_id, file_path) VALUES ({0}, {1}, {2}, {3})",
                        title, content, vaultId, relativePath);

                    indexed++;
                    if (indexed % 100 == 0)
                    {
                        _logger.LogDebug("已索引 {Indexed}/{Total} 个文件", indexed, files.Length);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "索引文件失败：{File}", file);
                }
            }

            _logger.LogInformation("知识库 {VaultId} 索引完成：{Indexed}/{Total} 个文件", vaultId, indexed, files.Length);
        }

        /// <summary>
        /// 使用 FTS5 搜索知识库
        /// </summary>
        public async Task<List<SearchResult>> SearchAsync(string vaultId, string query, CancellationToken ct = default)
        {
            var results = new List<SearchResult>();

            try
            {
                await EnsureFtsTableAsync(ct);

                using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync(ct);

                // 检查该知识库是否有索引
                using (var countCmd = connection.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM VaultNoteFts WHERE vault_id = @vaultId";
                    var p = countCmd.CreateParameter();
                    p.ParameterName = "@vaultId";
                    p.Value = vaultId;
                    countCmd.Parameters.Add(p);
                    var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                    _logger.LogDebug("知识库 {VaultId} FTS5 索引数量: {Count}", vaultId, count);
                    if (count == 0)
                    {
                        _logger.LogWarning("知识库 {VaultId} 尚未建立 FTS5 索引", vaultId);
                        return results;
                    }
                }

                // FTS5 MATCH 查询（使用参数化查询防止 SQL 注入）
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT title, file_path, content, bm25(VaultNoteFts) AS rank
                    FROM VaultNoteFts
                    WHERE vault_id = @vaultId AND VaultNoteFts MATCH @query
                    ORDER BY rank
                    LIMIT 50
                ";

                var vaultIdParam = command.CreateParameter();
                vaultIdParam.ParameterName = "@vaultId";
                vaultIdParam.Value = vaultId;
                command.Parameters.Add(vaultIdParam);

                var queryParam = command.CreateParameter();
                queryParam.ParameterName = "@query";
                queryParam.Value = query;
                command.Parameters.Add(queryParam);

                using var reader = await command.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    var title = reader.GetString(0);
                    var filePath = reader.GetString(1);
                    var content = reader.GetString(2);
                    var rank = reader.GetDouble(3);

                    results.Add(new SearchResult
                    {
                        Id = title,
                        Title = title,
                        Path = filePath,
                        Preview = ExtractPreview(content, query),
                        Score = (int)(Math.Max(0, -rank) * 100) // bm25 越小越好，转为正分
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FTS5 搜索失败：{Query}", query);
            }

            return results;
        }

        /// <summary>
        /// 获取指定知识库的索引统计
        /// </summary>
        public async Task<(int Count, DateTime? LastIndexed)> GetIndexStatsAsync(string vaultId, CancellationToken ct = default)
        {
            try
            {
                await EnsureFtsTableAsync(ct);
                using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync(ct);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM VaultNoteFts WHERE vault_id = @vaultId";
                var p = cmd.CreateParameter();
                p.ParameterName = "@vaultId";
                p.Value = vaultId;
                cmd.Parameters.Add(p);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                return (count, null);
            }
            catch
            {
                return (0, null);
            }
        }

        private static string ExtractPreview(string content, string query)
        {
            var queryLower = query.ToLower();
            var contentLower = content.ToLower();
            var idx = contentLower.IndexOf(queryLower);

            if (idx < 0)
            {
                // 尝试匹配第一个关键词
                var firstWord = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstWord))
                    idx = contentLower.IndexOf(firstWord);
            }

            if (idx < 0)
                return content.Length > 200 ? content[..200] + "..." : content;

            var start = Math.Max(0, idx - 80);
            var length = Math.Min(200, content.Length - start);
            var preview = content.Substring(start, length);

            if (start > 0) preview = "..." + preview;
            if (start + length < content.Length) preview += "...";

            return preview;
        }
    }
}
