using Baihua.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Search;
using System.Diagnostics;

namespace Baihua.Vault.Controllers;

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
                        ErrorMessage = "蹇呴』鎸囧畾鏈夋晥鐨勭煡璇嗗簱"
                    }
                });
            }

            var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
            if (string.IsNullOrEmpty(vaultPath) || !System.IO.Directory.Exists(vaultPath))
            {
                _logger.LogWarning("鐭ヨ瘑搴撹矾寰勬棤鏁堬細VaultId={VaultId}, Path={Path}", vaultId, vaultPath);
                return Ok(new
                {
                    results = new List<SearchResult>(),
                    status = new SearchStatusInfo
                    {
                        VaultConfigured = !string.IsNullOrEmpty(vaultPath),
                        VaultExists = !string.IsNullOrEmpty(vaultPath) && System.IO.Directory.Exists(vaultPath),
                        SearchMethod = "none",
                        ErrorMessage = string.IsNullOrEmpty(vaultPath)
                            ? "鏈壘鍒版寚瀹氱殑鐭ヨ瘑搴?
                            : $"鐭ヨ瘑搴撹矾寰勪笉瀛樺湪锛歿vaultPath}"
                    }
                });
            }

            if (string.IsNullOrWhiteSpace(q))
            {
                return Ok(new { results = new List<SearchResult>(), status = new SearchStatusInfo { VaultConfigured = true, VaultExists = true } });
            }

            _logger.LogInformation("鎼滅储鐭ヨ瘑搴擄細{Query}", q);

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
                        _logger.LogInformation("obsidian-cli 鎼滅储鎴愬姛锛氭壘鍒?{Count} 鏉＄粨鏋?, cliResults.Count);
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
                        _logger.LogDebug("obsidian-cli 鎼滅储澶辫触鎴栬秴鏃讹紝鍥為€€鍒版枃浠舵壂鎻?);
                    }
                }
                else if (canUseCli && !obsidianRunning)
                {
                    _logger.LogDebug("Obsidian 鏈繍琛岋紝浣跨敤鏂囦欢鎵弿");
                    searchMethod = "file-scan";
                }
                else
                {
                    _logger.LogDebug("obsidian-cli 涓嶅彲鐢紝浣跨敤鏂囦欢鎵弿");
                    searchMethod = "file-scan";
                }

                // 灏濊瘯 FTS5 鍏ㄦ枃鎼滅储
                var ftsResults = await _vaultNoteIndexer.SearchAsync(vaultId, q, HttpContext.RequestAborted);
                if (ftsResults.Count > 0)
                {
                    _logger.LogInformation("FTS5 鎼滅储鎴愬姛锛氭壘鍒?{Count} 鏉＄粨鏋?, ftsResults.Count);
                    searchMethod = "fts5";

                    // 濡傛灉閰嶇疆浜嗚涔夋悳绱紝杩涜閲嶆帓
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

                // 鍥為€€鍒扮洿鎺ユ壂鎻忔枃浠?
                var fileResults = await SearchByScanningFiles(vaultPath, q);
                _logger.LogInformation("鏂囦欢鎵弿瀹屾垚锛氭壘鍒?{Count} 鏉＄粨鏋?, fileResults.Count);
                
                // 濡傛灉閰嶇疆浜嗚涔夋悳绱紝杩涜閲嶆帓
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
                    errorMessage = "鏈湪鐭ヨ瘑搴撲腑鎵惧埌鍖归厤鍐呭";
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
                _logger.LogError(ex, "鎼滅储澶辫触");
                return StatusCode(500, new { error = "鎼滅储澶辫触", message = ex.Message });
            }
        }
}
