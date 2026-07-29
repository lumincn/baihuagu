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
    /// 娴忚鐭ヨ瘑搴撶洰褰曠粨鏋勶紙WebUI 浣跨敤锛?
    /// </summary>
    [HttpGet("vaults/{vaultId}/browse")]
    public ActionResult<VaultBrowseResponse> BrowseVault(string vaultId, [FromQuery] string? path = "")
    {
        var baseVaultPath = ResolveVaultPath(vaultId);
        if (string.IsNullOrEmpty(baseVaultPath))
        {
            return NotFound(new { error = "鐭ヨ瘑搴撲笉瀛樺湪" });
        }

        // 浣跨敤 notes/ 瀛愮洰褰曚綔涓虹煡璇嗗簱鍐呭鏍圭洰褰?
        var notesPath = System.IO.Path.Combine(baseVaultPath, "notes");
        var effectiveRoot = System.IO.Directory.Exists(notesPath) ? notesPath : baseVaultPath;

        var targetPath = string.IsNullOrEmpty(path)
            ? effectiveRoot
            : System.IO.Path.Combine(effectiveRoot, path.Trim('/').Replace('/', System.IO.Path.DirectorySeparatorChar));

        var fullRootPath = System.IO.Path.GetFullPath(effectiveRoot);
        var fullTargetPath = System.IO.Path.GetFullPath(targetPath);
        if (!fullTargetPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "闈炴硶璺緞" });
        }

        if (!System.IO.Directory.Exists(targetPath))
        {
            return NotFound(new { error = "鐩綍涓嶅瓨鍦? });
        }

        var items = new List<VaultBrowseItem>();

        foreach (var dir in System.IO.Directory.GetDirectories(targetPath))
        {
            var dirName = System.IO.Path.GetFileName(dir);
            if (ExcludedDirs.Contains(dirName)) continue;
            var relativePath = dir.Substring(fullRootPath.Length).TrimStart('/', '\\').Replace('\\', '/');
            items.Add(new VaultBrowseItem
            {
                Name = dirName,
                Path = relativePath,
                IsDirectory = true,
                Modified = System.IO.Directory.GetLastWriteTime(dir)
            });
        }

        foreach (var file in System.IO.Directory.GetFiles(targetPath, "*.md"))
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(file);
            var relativePath = file.Substring(fullRootPath.Length).TrimStart('/', '\\').Replace('\\', '/');
            var fileInfo = new System.IO.FileInfo(file);
            items.Add(new VaultBrowseItem
            {
                Name = fileName,
                Path = relativePath[..^3],
                IsDirectory = false,
                Size = fileInfo.Length,
                Modified = fileInfo.LastWriteTime
            });
        }

        items = items.OrderBy(i => !i.IsDirectory).ThenBy(i => i.Name).ToList();

        return Ok(new VaultBrowseResponse
        {
            VaultId = vaultId,
            VaultName = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Name ?? "",
            CurrentPath = path ?? "",
            Items = items
        });
    }
}
