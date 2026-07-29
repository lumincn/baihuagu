using TaskRunner.Core.Shared.Notifications;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;
using TaskRunner.Contracts.Vaults;

namespace TaskRunner.Vault.Controllers
{
    public partial class VaultSettingsController : ControllerBase
    {
        #region 多知识库管理 API

        /// <summary>
        /// 获取所有知识库
        /// </summary>
        [HttpGet("vaults")]
        public ActionResult<VaultsResponse> GetVaults()
        {
            var vaults = _vaultSettings.GetVaults();

            return Ok(new VaultsResponse
            {
                Vaults = vaults.Select(v => new VaultConfig
                {
                    Id = v.Id,
                    Name = v.Name,
                    Path = v.Path,
                    CreatedAt = v.CreatedAt,
                    Tags = v.Tags,
                    Industry = v.Industry,
                    Source = v.Source
                }).ToList()
            });
        }

        /// <summary>
        /// 添加新知识库
        /// </summary>
        [HttpPost("vaults")]
        public IActionResult AddVault([FromBody] AddVaultRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "请求不能为空" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { error = "知识库名称不能为空" });

            if (string.IsNullOrWhiteSpace(request.Path))
                return BadRequest(new { error = "知识库路径不能为空" });

            var industry = string.IsNullOrWhiteSpace(request.Industry) ? "笔记" : request.Industry.Trim();
            var vault = _vaultSettings.AddVault(request.Name.Trim(), request.Path.Trim(), industry);

            if (!Directory.Exists(request.Path.Trim()))
            {
                try
                {
                    Directory.CreateDirectory(request.Path.Trim());
                    _logger.LogInformation("创建知识库目录: {Path}", request.Path.Trim());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "无法创建知识库目录: {Path}", request.Path.Trim());
                }
            }

            _ = _webUINotification.NotifyVaultStatusChangedAsync();

            _logger.LogInformation("添加知识库: {Name} ({Id}) at {Path} 行业={Industry}", vault.Name, vault.Id, vault.Path, industry);

            return Ok(new VaultConfig
            {
                Id = vault.Id,
                Name = vault.Name,
                Path = vault.Path,
                CreatedAt = vault.CreatedAt,
                Industry = industry
            });
        }

        /// <summary>
        /// 更新知识库
        /// </summary>
        [HttpPut("vaults/{vaultId}")]
        public IActionResult UpdateVault(string vaultId, [FromBody] UpdateVaultRequest request)
        {
            if (string.IsNullOrWhiteSpace(vaultId))
                return BadRequest(new { error = "知识库 ID 不能为空" });

            if (request == null)
                return BadRequest(new { error = "请求不能为空" });

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var success = _vaultSettings.UpdateVaultName(vaultId, request.Name.Trim());
                if (!success)
                    return NotFound(new { error = "知识库不存在" });
                _logger.LogInformation("更新知识库名称: {VaultId} -> {Name}", vaultId, request.Name);
            }

            if (request.Tags != null)
            {
                var success = _vaultSettings.UpdateVaultTags(vaultId, request.Tags.Trim());
                if (!success)
                    return NotFound(new { error = "知识库不存在" });
                _logger.LogInformation("更新知识库标签: {VaultId} -> {Tags}", vaultId, request.Tags);
            }

            if (!string.IsNullOrWhiteSpace(request.Industry))
            {
                var success = _vaultSettings.UpdateVaultIndustry(vaultId, request.Industry.Trim());
                if (!success)
                    return NotFound(new { error = "知识库不存在" });
                _logger.LogInformation("更新知识库行业: {VaultId} -> {Industry}", vaultId, request.Industry);
            }

            return Ok(new { success = true });
        }

        /// <summary>
        /// 删除知识库
        /// </summary>
        [HttpDelete("vaults/{vaultId}")]
        public IActionResult RemoveVault(string vaultId)
        {
            if (string.IsNullOrWhiteSpace(vaultId))
                return BadRequest(new { error = "知识库 ID 不能为空" });

            var success = _vaultSettings.RemoveVault(vaultId);
            if (!success)
                return NotFound(new { error = "知识库不存在" });

            _ = _webUINotification.NotifyVaultStatusChangedAsync();

            _logger.LogInformation("删除知识库: {VaultId}", vaultId);
            return Ok(new { success = true });
        }

        /// <summary>
        /// 获取回收站中的知识库列表
        /// </summary>
        [HttpGet("vaults/trash")]
        public ActionResult<VaultsResponse> GetTrashVaults()
        {
            var vaults = _vaultSettings.GetTrashVaults();
            return Ok(new VaultsResponse
            {
                Vaults = vaults.Select(v => new VaultConfig
                {
                    Id = v.Id,
                    Name = v.Name,
                    Path = v.Path,
                    CreatedAt = v.CreatedAt,
                    Tags = v.Tags,
                    Industry = v.Industry
                }).ToList()
            });
        }

        /// <summary>
        /// 恢复回收站中的知识库
        /// </summary>
        [HttpPost("vaults/{vaultId}/restore")]
        public IActionResult RestoreVault(string vaultId)
        {
            if (string.IsNullOrWhiteSpace(vaultId))
                return BadRequest(new { error = "知识库 ID 不能为空" });

            var success = _vaultSettings.RestoreVault(vaultId);
            if (!success)
                return BadRequest(new { error = "恢复失败，知识库不存在或原始路径已被占用" });

            _ = _webUINotification.NotifyVaultStatusChangedAsync();
            _logger.LogInformation("恢复知识库: {VaultId}", vaultId);
            return Ok(new { success = true });
        }

        /// <summary>
        /// 清空回收站（永久删除）
        /// </summary>
        [HttpPost("vaults/trash/empty")]
        public IActionResult EmptyTrash()
        {
            _vaultSettings.EmptyTrash();
            _ = _webUINotification.NotifyVaultStatusChangedAsync();
            _logger.LogInformation("回收站已清空");
            return Ok(new { success = true });
        }

        #endregion
    }
}
