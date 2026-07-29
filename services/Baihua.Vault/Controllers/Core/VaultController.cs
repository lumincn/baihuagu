using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskRunner.Data;
using TaskRunner.Services;
using TaskRunner.Services.Strategies;
using TaskRunner.Contracts.Vaults;

namespace TaskRunner.Vault.Controllers;
    /// <summary>
    /// 验证 Token 请求
    /// </summary>
    public class VerifyTokenRequest
    {
        public string? Token { get; set; }
    }

    [ApiController]
    [Route("vault")]
    [Route("api")]
    [Route("mg")]
    public partial class VaultController : ControllerBase
    {
        private readonly Services.VaultSettingsService _vaultSettings;
        private readonly DeviceService _deviceService;
        private readonly ILogger<VaultController> _logger;
        private readonly ISyncAuthorizationStrategy _syncAuthStrategy;
        private readonly IDbContextFactory<VaultDbContext> _dbContextFactory;
        private readonly RequestSignatureService _signatureService;
        private readonly IVaultNameResolver _vaultNameResolver;

        // 支持的文件扩展名
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md",
            ".json",  // Anki 卡片文件
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"
        };

        // 排除的目录
        private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".obsidian", ".trash", "node_modules", ".DS_Store"
        };

        /// <summary>
        /// 根据vaultId解析知识库路径，不修改全局活跃状态。
        /// 不再回退到当前活跃知识库，必须显式指定 vaultId。
        /// </summary>
        private string? ResolveVaultPath(string? vaultId)
        {
            if (string.IsNullOrEmpty(vaultId))
            {
                return null;
            }

            var targetVault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
            if (targetVault != null && !string.IsNullOrEmpty(targetVault.Path))
            {
                return targetVault.Path;
            }

            _logger.LogWarning("指定的知识库不存在或路径为空：{VaultId}", vaultId);
            return null;
        }

        public VaultController(
            Services.VaultSettingsService vaultSettings,
            DeviceService deviceService,
            ILogger<VaultController> logger,
            ISyncAuthorizationStrategy syncAuthStrategy,
            IDbContextFactory<VaultDbContext> dbContextFactory,
            RequestSignatureService signatureService,
            IVaultNameResolver vaultNameResolver)
        {
            _vaultSettings = vaultSettings;
            _deviceService = deviceService;
            _logger = logger;
            _syncAuthStrategy = syncAuthStrategy;
            _dbContextFactory = dbContextFactory;
            _signatureService = signatureService;
            _vaultNameResolver = vaultNameResolver;
        }

        /// <summary>
        /// 获取所有知识库列表
        /// </summary>
        [HttpGet("vaults")]
        public ActionResult<IEnumerable<object>> GetVaults()
        {
            var vaults = _vaultSettings.GetVaults();

            var result = vaults.Select(v => new
            {
                id = v.Id,
                name = v.Name,
                path = v.Path,
                industry = v.Industry,
                source = v.Source,
                pushedByDeviceId = v.PushedByDeviceId,
                pushedByDeviceName = v.PushedByDeviceName,
                pushedAt = v.PushedAt
            });

            _logger.LogDebug("返回知识库列表，共 {Count} 个", vaults.Count);
            return Ok(result);
        }

        /// <summary>
        /// 验证请求的设备是否已授权（支持新旧两种 Token 验证方式）
        /// </summary>
        private bool ValidateDeviceAuthorization()
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return false;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            // 优先使用新的 PairingService 验证（支持 Token 过期检查）
            // 使用DeviceService验证
            return _deviceService.ValidateAccessToken(token);
        }

}
