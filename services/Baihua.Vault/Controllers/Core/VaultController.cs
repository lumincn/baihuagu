using Baihua.Core.Services;
using Baihua.Core;
using Baihua.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baihua.Data;
using Baihua.Family.Services;
using Baihua.Family.Services.Strategies;
using Baihua.Contracts.Vaults;

namespace Baihua.Vault.Controllers;
    /// <summary>
    /// 楠岃瘉 Token 璇锋眰
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

        // 鏀寔鐨勬枃浠舵墿灞曞悕
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md",
            ".json",  // Anki 鍗＄墖鏂囦欢
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"
        };

        // 鎺掗櫎鐨勭洰褰?
        private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".obsidian", ".trash", "node_modules", ".DS_Store"
        };

        /// <summary>
        /// 鏍规嵁vaultId瑙ｆ瀽鐭ヨ瘑搴撹矾寰勶紝涓嶄慨鏀瑰叏灞€娲昏穬鐘舵€併€?
        /// 涓嶅啀鍥為€€鍒板綋鍓嶆椿璺冪煡璇嗗簱锛屽繀椤绘樉寮忔寚瀹?vaultId銆?
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

            _logger.LogWarning("鎸囧畾鐨勭煡璇嗗簱涓嶅瓨鍦ㄦ垨璺緞涓虹┖锛歿VaultId}", vaultId);
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
        /// 鑾峰彇鎵€鏈夌煡璇嗗簱鍒楄〃
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

            _logger.LogDebug("杩斿洖鐭ヨ瘑搴撳垪琛紝鍏?{Count} 涓?, vaults.Count);
            return Ok(result);
        }

        /// <summary>
        /// 楠岃瘉璇锋眰鐨勮澶囨槸鍚﹀凡鎺堟潈锛堟敮鎸佹柊鏃т袱绉?Token 楠岃瘉鏂瑰紡锛?
        /// </summary>
        private bool ValidateDeviceAuthorization()
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return false;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            // 浼樺厛浣跨敤鏂扮殑 PairingService 楠岃瘉锛堟敮鎸?Token 杩囨湡妫€鏌ワ級
            // 浣跨敤DeviceService楠岃瘉
            return _deviceService.ValidateAccessToken(token);
        }

}
