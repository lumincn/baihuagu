using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Search;
using System.Diagnostics;

namespace TaskRunner.Vault.Controllers;

public partial class SearchController
{
        public async Task<ActionResult> Search([FromQuery] string q = "", [FromQuery] string vaultId = "")
        {
            Console.WriteLine($"[Search] Query: '{q}', VaultId: '{vaultId}'");

            if (string.IsNullOrWhiteSpace(vaultId))
            {
                return Ok(new
                {
                    results = new List<SearchResult>(),
                    status = new SearchStatusInfo
                    {
                        VaultConfigured = false,
                        SearchMethod = "none",
                        ErrorMessage = "必须指定有效的知识库"
                    }
                });
            }

            var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
            if (string.IsNullOrEmpty(vaultPath) || !System.IO.Directory.Exists(vaultPath))
            {
                _logger.LogWarning("知识库路径无效：VaultId={VaultId}, Path={Path}", vaultId, vaultPath);
                return Ok(new
                {
                    results = new List<SearchResult>(),
                    status = new SearchStatusInfo
                    {
                        VaultConfigured = !string.IsNullOrEmpty(vaultPath),
                        VaultExists = !string.IsNullOrEmpty(vaultPath) && System.IO.Directory.Exists(vaultPath),
                        SearchMethod = "none",
                        ErrorMessage = string.IsNullOrEmpty(vaultPath)
                            ? "未找到指定的知识库"
                            : $"知识库路径不存在：{vaultPath}"
                    }
                });
            }

            if (string.IsNullOrWhiteSpace(q))
            {
                return Ok(new { results = new List<SearchResult>(), status = new SearchStatusInfo { VaultConfigured = true, VaultExists = true } });
            }

            _logger.LogInformation("搜索知识库：{Query}", q);

            try
            {
                var canUseCli = Services.ObsidianExecutableResolver.TryGetPath(out var obsidianExe);
                var obsidianRunning = Process.GetProcessesByName("Obsidian").Length > 0;
                string searchMethod = "file-scan";
                string? errorMessage = null;
                
                if (canUseCli && obsidianRunning)
                {
                    var vaultName = System.IO.Path.GetFileName(vaultPath.TrimEnd('/'));
                    var cliResults = await SearchWithObsidianCli(obsidianExe, vaultName, q);
                    
                    if (cliResults != null && cliResults.Count > 0)
                    {
                        _logger.LogInformation("obsidian-cli 搜索成功：找到 {Count} 条结果", cliResults.Count);
                        return Ok(new
                        {
                            results = cliResults,
                            status = new SearchStatusInfo
                            {
                                VaultConfigured = true,
                                VaultExists = true,
                                ObsidianRunning = true,
                                SearchMethod = "obsidian-cli"
                            }
                        });
                    }
                    
                    searchMethod = "file-scan";
                    if (cliResults == null)
                    {
                        _logger.LogDebug("obsidian-cli 搜索失败或超时，回退到文件扫描");
                    }
                }
                else if (canUseCli && !obsidianRunning)
                {
                    _logger.LogDebug("Obsidian 未运行，使用文件扫描");
                    searchMethod = "file-scan";
                }
                else
                {
                    _logger.LogDebug("obsidian-cli 不可用，使用文件扫描");
                    searchMethod = "file-scan";
                }

                // 尝试 FTS5 全文搜索
                var ftsResults = await _vaultNoteIndexer.SearchAsync(vaultId, q, HttpContext.RequestAborted);
                if (ftsResults.Count > 0)
                {
                    _logger.LogInformation("FTS5 搜索成功：找到 {Count} 条结果", ftsResults.Count);
                    searchMethod = "fts5";

                    // 如果配置了语义搜索，进行重排
                    if (_embeddingService.IsSemanticSearchEnabled())
                    {
                        var rerankedResults = await _embeddingService.RerankBySimilarityAsync(q, ftsResults);
                        searchMethod = "fts5+semantic";
                        return Ok(new
                        {
                            results = rerankedResults,
                            status = new SearchStatusInfo
                            {
                                VaultConfigured = true,
                                VaultExists = true,
                                ObsidianRunning = obsidianRunning,
                                SearchMethod = searchMethod
                            }
                        });
                    }

                    return Ok(new
                    {
                        results = ftsResults,
                        status = new SearchStatusInfo
                        {
                            VaultConfigured = true,
                            VaultExists = true,
                            ObsidianRunning = obsidianRunning,
                            SearchMethod = searchMethod
                        }
                    });
                }

                // 回退到直接扫描文件
                var fileResults = await SearchByScanningFiles(vaultPath, q);
                _logger.LogInformation("文件扫描完成：找到 {Count} 条结果", fileResults.Count);
                
                // 如果配置了语义搜索，进行重排
                if (_embeddingService.IsSemanticSearchEnabled() && fileResults.Count > 0)
                {
                    var rerankedResults = await _embeddingService.RerankBySimilarityAsync(q, fileResults);
                    searchMethod = "semantic";
                    return Ok(new
                    {
                        results = rerankedResults,
                        status = new SearchStatusInfo
                        {
                            VaultConfigured = true,
                            VaultExists = true,
                            ObsidianRunning = obsidianRunning,
                            SearchMethod = searchMethod
                        }
                    });
                }
                
                if (fileResults.Count == 0)
                {
                    errorMessage = "未在知识库中找到匹配内容";
                }
                
                return Ok(new
                {
                    results = fileResults,
                    status = new SearchStatusInfo
                    {
                        VaultConfigured = true,
                        VaultExists = true,
                        ObsidianRunning = obsidianRunning,
                        SearchMethod = searchMethod,
                        ErrorMessage = errorMessage
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索失败");
                return StatusCode(500, new { error = "搜索失败", message = ex.Message });
            }
        }
}
