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
    [HttpGet("file")]
    public IActionResult GetFile([FromQuery] string path, [FromQuery] string vaultId, [FromQuery] string? deviceId = null)
    {
        var authResult = _syncAuthStrategy.ValidateFile(HttpContext, vaultId, deviceId);
        if (authResult != null)
        {
            return authResult;
        }

        _logger.LogInformation("GetFile璇锋眰: path={Path}, vaultId={VaultId}", path, vaultId);
        
        if (string.IsNullOrEmpty(path))
        {
            return BadRequest(new { error = "璺緞涓嶈兘涓虹┖" });
        }

        var baseVaultPath = ResolveVaultPath(vaultId);
        if (string.IsNullOrEmpty(baseVaultPath))
        {
            return BadRequest(new { error = "蹇呴』鎸囧畾鏈夋晥鐨勭煡璇嗗簱" });
        }

        try
        {
            // 璺緞瀹夊叏妫€鏌ワ細闃绘鐩綍閬嶅巻
            path = path.Replace("\\", "/").TrimStart('/');
            if (path.Contains(".."))
            {
                _logger.LogWarning("妫€娴嬪埌鐩綍閬嶅巻灏濊瘯: {Path}", path);
                return BadRequest(new { error = "闈炴硶璺緞" });
            }

            var ext = System.IO.Path.GetExtension(path);
            if (!AllowedExtensions.Contains(ext))
            {
                return BadRequest(new { error = $"涓嶆敮鎸佺殑鏂囦欢绫诲瀷: {ext}" });
            }

            string filePath;
            if (path.StartsWith("cards/"))
            {
                filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseVaultPath, path));
            }
            else if (path.StartsWith("notes/"))
            {
                filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseVaultPath, path));
            }
            else
            {
                var notesPath = System.IO.Path.Combine(baseVaultPath, "notes");
                filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(notesPath, path));
            }

            // 纭繚鏂囦欢璺緞鍦ㄧ煡璇嗗簱鐩綍鍐咃紙闃叉璺緞閬嶅巻锛?
            var baseFullPath = System.IO.Path.GetFullPath(baseVaultPath);
            if (!filePath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("璺緞閬嶅巻琚樆姝? {FilePath} 涓嶅湪 {BasePath} 鍐?, filePath, baseFullPath);
                return BadRequest(new { error = "闈炴硶璺緞" });
            }
            
            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("鏂囦欢涓嶅瓨鍦細{Path}", path);
                return NotFound();
            }

            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var content = System.IO.File.ReadAllText(filePath);
                return Ok(content);
            }
            
            if (!ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = System.IO.File.ReadAllBytes(filePath);
                var mimeType = GetMimeType(ext);
                return File(bytes, mimeType);
            }

            var mdContent = System.IO.File.ReadAllText(filePath);
            return Ok(mdContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "璇诲彇鏂囦欢澶辫触锛歿Path}", path);
            return StatusCode(500, new { error = "璇诲彇澶辫触", message = ex.Message });
        }
    }

    private string GetMimeType(string ext)
    {
        return ext.ToLower() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
