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
    /// <summary>
    /// 绉诲姩绔帹閫?AI 鐢熸垚鐨勭煡璇嗗簱锛堟帴鏀舵潵鑷墜鏈虹鐨?DeepSeek 鐢熸垚鍐呭锛?
    /// </summary>
    [HttpPost("/mobile-vaults/push")]
    public async Task<ActionResult> PushMobileVault([FromBody] MobileVaultPushRequest request)
    {
        _logger.LogInformation("[PushMobileVault] Received from {RemoteIP}, VaultName={VaultName}, Industry={Industry}, NotesCount={NotesCount}",
            HttpContext.Connection.RemoteIpAddress, request.VaultName, request.Industry, request.Notes?.Count ?? 0);

        if (string.IsNullOrWhiteSpace(request.VaultName) || request.Notes == null || request.Notes.Count == 0)
        {
            return BadRequest(new { error = "鐭ヨ瘑搴撳悕绉板拰绗旇鍒楄〃涓嶈兘涓虹┖" });
        }

        try
        {
            var vaultRoot = _vaultSettings.VaultRootPathPreference;
            var mobileDir = Path.Combine(vaultRoot, "mobile");
            var industry = string.IsNullOrWhiteSpace(request.Industry) ? "绉诲姩绔敓鎴? : request.Industry.Trim();
            var safeVaultName = _vaultNameResolver.ToSafeDirectoryName(request.VaultName.Trim());
            var industryDir = Path.Combine(mobileDir, industry);
            Directory.CreateDirectory(industryDir);

            using var dbContext = _dbContextFactory.CreateDbContext();

            // 鏌ユ壘鏄惁宸叉湁鍚屽悕鍚岃涓氱殑 mobile 鐭ヨ瘑搴?
            var existingVault = dbContext.Vaults
                .FirstOrDefault(v => !v.IsDeleted
                    && v.Source == "mobile"
                    && v.Industry == industry
                    && v.Name == request.VaultName.Trim());

            string vaultId;
            string vaultDir;
            bool isNewVault = false;
            bool migrated = false;

            if (existingVault != null)
            {
                vaultId = existingVault.VaultId;

                // 妫€鏌ョ幇鏈夎矾寰勬槸鍚︾鍚堟柊鐨勪笁绾х粨鏋?mobile/{琛屼笟}/{鍚嶇О}/
                var expectedPath = Path.Combine(industryDir, safeVaultName);
                var isOldGuidStructure = !existingVault.Path.Equals(expectedPath, StringComparison.OrdinalIgnoreCase)
                    && !existingVault.Path.StartsWith(expectedPath + "_", StringComparison.OrdinalIgnoreCase);

                if (isOldGuidStructure && Directory.Exists(existingVault.Path))
                {
                    // 鏃?GUID 缁撴瀯闇€瑕佽縼绉诲埌涓夌骇鐩綍缁撴瀯
                    _logger.LogWarning("绉诲姩绔煡璇嗗簱璺緞缁撴瀯杩囨椂: {OldPath}锛岃縼绉诲埌: {NewPath}",
                        existingVault.Path, expectedPath);

                    if (Directory.Exists(expectedPath))
                    {
                        // 鐩爣鐩綍宸插瓨鍦紙涓嶅お鍙兘锛屼絾闃插尽锛?
                        vaultDir = _vaultNameResolver.GetUniqueDirectoryPath(industryDir, safeVaultName);
                    }
                    else
                    {
                        vaultDir = expectedPath;
                    }

                    Directory.Move(existingVault.Path, vaultDir);
                    existingVault.Path = vaultDir;
                    migrated = true;
                    _logger.LogInformation("鐭ヨ瘑搴撹矾寰勮縼绉诲畬鎴? {VaultId} -> {NewPath}", vaultId, vaultDir);
                }
                else if (Directory.Exists(existingVault.Path))
                {
                    vaultDir = existingVault.Path;
                }
                else
                {
                    // 鏁版嵁搴撹褰曞瓨鍦ㄤ絾鐗╃悊鐩綍宸蹭涪澶憋紝鎶ラ敊鑰屼笉鏄潤榛樺垱寤烘柊鐨?
                    _logger.LogError("鐭ヨ瘑搴撴暟鎹簱璁板綍瀛樺湪浣嗙墿鐞嗙洰褰曚涪澶? {VaultId} {Path}",
                        existingVault.VaultId, existingVault.Path);
                    return StatusCode(500, new { error = "鐭ヨ瘑搴撴暟鎹笉涓€鑷达細鏁版嵁搴撹褰曞瓨鍦ㄤ絾鐗╃悊鐩綍宸蹭涪澶憋紝璇疯仈绯荤鐞嗗憳" });
                }

                _logger.LogInformation("澶嶇敤宸叉湁绉诲姩绔煡璇嗗簱: {VaultId} {VaultName}{MigrationNote}锛岃拷鍔犵瑪璁?,
                    vaultId, request.VaultName, migrated ? "锛堝凡杩佺Щ璺緞锛? : "");
            }
            else
            {
                vaultId = Guid.NewGuid().ToString("N");
                vaultDir = _vaultNameResolver.GetUniqueDirectoryPath(industryDir, safeVaultName);
                isNewVault = true;
            }

            var notesDir = Path.Combine(vaultDir, "notes");
            Directory.CreateDirectory(notesDir);

            // 鍐欏叆绗旇鏂囦欢
            foreach (var note in request.Notes)
            {
                var safeRelPath = string.IsNullOrWhiteSpace(note.RelPath)
                    ? $"{note.Title}.md"
                    : note.RelPath;
                // 闃叉璺緞绌胯秺锛氭嫆缁濆寘鍚?.. 鐨勮矾寰?
                if (safeRelPath.Contains(".."))
                {
                    _logger.LogWarning("妫€娴嬪埌璺緞绌胯秺灏濊瘯锛屽凡鎷掔粷: {RelPath}", safeRelPath);
                    return BadRequest(new { error = $"闈炴硶鏂囦欢璺緞: {safeRelPath}" });
                }
                safeRelPath = safeRelPath.TrimStart('/', '\\');
                var notePath = Path.Combine(notesDir, safeRelPath);
                var noteDir = Path.GetDirectoryName(notePath);
                if (!string.IsNullOrEmpty(noteDir))
                {
                    Directory.CreateDirectory(noteDir);
                }
                await System.IO.File.WriteAllTextAsync(notePath, note.Content ?? "");
            }

            // 娉ㄥ唽鍒版暟鎹簱锛堜粎褰撴槸鏂扮煡璇嗗簱鏃讹級
            var pushedByDeviceId = request.DeviceId ?? "";
            var pushedByDeviceName = request.DeviceName ?? "";
            var pushedAt = DateTime.UtcNow;

            if (isNewVault)
            {
                dbContext.Vaults.Add(new Data.Entities.Vault
                {
                    VaultId = vaultId,
                    Name = request.VaultName.Trim(),
                    Path = vaultDir,
                    IsActive = true,
                    Industry = industry,
                    Source = "mobile",
                    PushedByDeviceId = pushedByDeviceId,
                    PushedByDeviceName = pushedByDeviceName,
                    PushedAt = pushedAt,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
            else
            {
                existingVault!.UpdatedAt = DateTime.UtcNow;
                existingVault.PushedByDeviceId = pushedByDeviceId;
                existingVault.PushedByDeviceName = pushedByDeviceName;
                existingVault.PushedAt = pushedAt;
                if (migrated)
                {
                    existingVault.Path = vaultDir;
                }
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("绉诲姩绔煡璇嗗簱鎺ㄩ€佹垚鍔? {VaultId} {VaultName}锛屽叡 {NoteCount} 鏉＄瑪璁?,
                vaultId, request.VaultName, request.Notes.Count);

            return Ok(new { success = true, vaultId, message = migrated ? "鐭ヨ瘑搴撴帹閫佹垚鍔燂紙宸茶縼绉昏矾寰勭粨鏋勶級" : "鐭ヨ瘑搴撴帹閫佹垚鍔? });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "绉诲姩绔煡璇嗗簱鎺ㄩ€佸け璐? {VaultName}", request.VaultName);
            return StatusCode(500, new { error = $"鎺ㄩ€佸け璐? {ex.Message}" });
        }
    }

    private class MobileCardItem
    {
        public JsonElement Front { get; set; }
        public JsonElement Back { get; set; }
        public string Deck { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string Source { get; set; } = "";
    }
}
