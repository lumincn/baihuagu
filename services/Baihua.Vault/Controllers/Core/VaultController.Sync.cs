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

public partial class VaultController
{
        [HttpPost("verify-token")]
        public ActionResult<object> VerifyToken([FromBody] VerifyTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.Token))
            {
                return BadRequest(new { valid = false, error = "Token 涓嶈兘涓虹┖" });
            }

            var isValid = _deviceService.ValidateAccessToken(request.Token);

            if (!isValid)
            {
                return Ok(new { valid = false, error = "Token 鏃犳晥鎴栧凡杩囨湡" });
            }

            return Ok(new { valid = true, deviceId = "" });
        }

        /// <summary>
        /// 鑾峰彇鐭ヨ瘑搴撴竻鍗曪紙澧為噺鍚屾锛?
        /// cloud 妯″紡锛欻MAC绛惧悕 + deviceId + 閰嶉/棰戠巼妫€鏌?
        /// 瀹跺涵鐗?鏈湴妯″紡锛氫粛闇€ Bearer Token 楠岃瘉
        /// </summary>
        [HttpGet("manifest")]
        public ActionResult<VaultManifestResponse> GetManifest([FromQuery] string vaultId, [FromQuery] string? deviceId = null)
        {
            var authResult = _syncAuthStrategy.ValidateManifest(HttpContext, vaultId, deviceId);
            if (authResult != null)
            {
                return authResult;
            }

            var targetVault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
            var baseVaultPath = targetVault?.Path;
            _logger.LogDebug("GetManifest called. VaultPath={VaultPath}, VaultId={VaultId}", baseVaultPath, vaultId);

            if (string.IsNullOrEmpty(baseVaultPath))
            {
                return NotFound(new { error = "鐭ヨ瘑搴撲笉瀛樺湪鎴栧凡琚垹闄? });
            }

            if (!System.IO.Directory.Exists(baseVaultPath))
            {
                _logger.LogError("鐭ヨ瘑搴撹矾寰勬棤鏁堬細{Path}锛屾暟鎹簱璁板綍瀛樺湪浣嗙墿鐞嗙洰褰曞凡涓㈠け", baseVaultPath);
                return StatusCode(410, new { error = "鐭ヨ瘑搴撴暟鎹笉涓€鑷达細鐗╃悊鐩綍宸蹭涪澶?, vaultId });
            }

            try
            {
                var files = new List<ManifestFile>();
                long maxMtime = 0;

                // 鍚屾 notes/ 鐩綍
                var notesPath = System.IO.Path.Combine(baseVaultPath, "notes");
                if (System.IO.Directory.Exists(notesPath))
                {
                    ScanDirectory(notesPath, notesPath, files, ref maxMtime, "");
                }

                // 鍚屾 cards/ 鐩綍
                var cardsPath = System.IO.Path.Combine(baseVaultPath, "cards");
                if (System.IO.Directory.Exists(cardsPath))
                {
                    ScanDirectory(cardsPath, cardsPath, files, ref maxMtime, "cards/");
                }

                // 鍥為€€锛氬鏋?notes/ 鍜?cards/ 閮戒笉瀛樺湪锛屾壂鎻忔牴鐩綍涓嬬殑鐩存帴鏂囦欢
                if (files.Count == 0 && !System.IO.Directory.Exists(notesPath) && !System.IO.Directory.Exists(cardsPath))
                {
                    ScanDirectory(baseVaultPath, baseVaultPath, files, ref maxMtime, "");
                }

                var cursor = maxMtime;

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var syncDeviceId = !string.IsNullOrWhiteSpace(deviceId) ? deviceId : "ip-" + ipAddress.GetHashCode().ToString("x");
                var syncDeviceName = !string.IsNullOrWhiteSpace(deviceId) ? deviceId : "绉诲姩绔?" + ipAddress + ")";
                _deviceService.RecordSyncActivity(syncDeviceId, syncDeviceName, vaultId, files.Count, "manifest", ipAddress);

                _logger.LogInformation("杩斿洖鍏ㄩ噺娓呭崟锛歿Count} 涓枃浠讹紝cursor={Cursor}, vaultId={VaultId}", files.Count, cursor, vaultId);

                return Ok(new VaultManifestResponse
                {
                    VaultId = vaultId,
                    VaultName = targetVault?.Name ?? "鎸囧畾鐭ヨ瘑搴?,
                    Cursor = cursor,
                    Files = files
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "鑾峰彇娓呭崟澶辫触");
                return StatusCode(500, new { error = "鑾峰彇澶辫触", message = ex.Message });
            }
        }

        private void ScanDirectory(string rootPath, string currentPath, List<ManifestFile> files, ref long maxMtime, string pathPrefix = "")
        {
            foreach (var dir in System.IO.Directory.GetDirectories(currentPath))
            {
                var dirName = System.IO.Path.GetFileName(dir);
                if (ExcludedDirs.Contains(dirName))
                {
                    _logger.LogDebug("ScanDirectory 璺宠繃鎺掗櫎鐩綍: {DirName}", dirName);
                    continue;
                }
                ScanDirectory(rootPath, dir, files, ref maxMtime, pathPrefix);
            }

            foreach (var file in System.IO.Directory.GetFiles(currentPath))
            {
                var ext = System.IO.Path.GetExtension(file);
                if (!AllowedExtensions.Contains(ext))
                {
                    _logger.LogDebug("ScanDirectory 璺宠繃涓嶆敮鎸佺殑鏂囦欢绫诲瀷: {File} ({Ext})", file, ext);
                    continue;
                }

                var relativePath = pathPrefix + file.Substring(rootPath.Length).TrimStart('/', '\\');
                relativePath = relativePath.Replace('\\', '/').TrimStart('/');
                var modified = System.IO.File.GetLastWriteTime(file);
                var modifiedUnix = new DateTimeOffset(modified).ToUnixTimeSeconds();
                
                if (modifiedUnix > maxMtime)
                {
                    maxMtime = modifiedUnix;
                }
                
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    _logger.LogWarning("ScanDirectory 璁＄畻鍑虹┖鐨勭浉瀵硅矾寰? {File}", file);
                    continue;
                }

                var fileInfo = new System.IO.FileInfo(file);
                if (fileInfo.Length == 0)
                {
                    _logger.LogWarning("ScanDirectory 璺宠繃绌烘枃浠? {File}", file);
                    continue;
                }

                files.Add(new ManifestFile
                {
                    RelPath = relativePath,
                    Op = "upsert",
                    Mtime = modifiedUnix,
                    Size = fileInfo.Length,
                    Sha256 = modifiedUnix.ToString()
                });
            }
        }

}
