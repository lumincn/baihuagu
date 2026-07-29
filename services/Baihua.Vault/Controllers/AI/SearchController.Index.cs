using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace TaskRunner.Vault.Controllers;

public partial class SearchController
{

        #region FTS5 Index Management

        /// <summary>
        /// 重建指定知识库的 FTS5 全文索引
        /// </summary>
        [HttpPost("reindex")]
        public async Task<ActionResult> Reindex([FromBody] ReindexRequest request)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == request.VaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath) || !Directory.Exists(vaultPath))
                {
                    return BadRequest(new { error = "知识库路径无效" });
                }

                _logger.LogInformation("开始重建知识库 {VaultId} 的 FTS5 索引", request.VaultId);
                await _vaultNoteIndexer.IndexVaultAsync(request.VaultId, vaultPath, HttpContext.RequestAborted);

                return Ok(new { success = true, message = $"知识库 {request.VaultId} 的 FTS5 索引已重建" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重建 FTS5 索引失败");
                return StatusCode(500, new { error = "重建索引失败", message = ex.Message });
            }
        }

        /// <summary>
        /// 获取知识库 FTS5 索引状态
        /// </summary>
        [HttpGet("index-status")]
        public async Task<ActionResult> IndexStatus([FromQuery] string vaultId)
        {
            try
            {
                var (count, _) = await _vaultNoteIndexer.GetIndexStatsAsync(vaultId, HttpContext.RequestAborted);
                return Ok(new { vaultId, indexedCount = count, hasIndex = count > 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 FTS5 索引状态失败");
                return StatusCode(500, new { error = "获取状态失败", message = ex.Message });
            }
        }

        #endregion
}
