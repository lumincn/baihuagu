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
    /// 璇诲彇绗旇鍐呭锛圵ebUI 浣跨敤锛?
    /// </summary>
    [HttpGet("read/{*path}")]
    public ActionResult<VaultNote> ReadNote(string path, [FromQuery] string vaultId)
    {
        if (string.IsNullOrWhiteSpace(path))
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
            path = path.TrimEnd('/', '\\');
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^3];
            }

            var notesPath = System.IO.Path.Combine(baseVaultPath, "notes");
            var filePath = System.IO.Path.Combine(notesPath, path + ".md");
            
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { error = $"绗旇涓嶅瓨鍦細{path}" });
            }

            var content = System.IO.File.ReadAllText(filePath);
            var title = System.IO.Path.GetFileNameWithoutExtension(path);
            var modified = System.IO.File.GetLastWriteTime(filePath);
            var (tags, aiGenerated, aiProvider, aiModel, generatedAt) = ExtractFrontmatter(content);

            return Ok(new VaultNote
            {
                Path = path,
                Title = title,
                Content = content,
                Modified = modified,
                Tags = tags,
                AiGenerated = aiGenerated,
                AiProvider = aiProvider,
                AiModel = aiModel,
                GeneratedAt = generatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "璇诲彇绗旇澶辫触锛歿Path}", path);
            return StatusCode(500, new { error = "璇诲彇澶辫触", message = ex.Message });
        }
    }

    /// <summary>
    /// 鍐欏叆绗旇鍐呭锛圵ebUI 缂栬緫鐢級銆?
    /// 缁熶竴鍐欏叆 notes/ 瀛愮洰褰曪紱鍏煎浼犲叆甯?notes/ 鍓嶇紑鐨勮矾寰勩€?
    /// </summary>
    [HttpPost("write/{*path}")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB 闄愬埗锛岄槻姝?DoS
    public async Task<IActionResult> WriteNote(string path, [FromQuery] string vaultId, [FromBody] WriteNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "璺緞涓嶈兘涓虹┖" });
        if (request == null || request.Content == null)
            return BadRequest(new { error = "鍐呭涓嶈兘涓虹┖" });

        var baseVaultPath = ResolveVaultPath(vaultId);
        if (string.IsNullOrEmpty(baseVaultPath))
            return BadRequest(new { error = "蹇呴』鎸囧畾鏈夋晥鐨勭煡璇嗗簱" });

        try
        {
            path = path.TrimEnd('/', '\\');
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                path = path[..^3];

            // 璺緞瀹夊叏妫€鏌ワ細闃绘鐩綍閬嶅巻
            path = path.Replace("\\", "/");
            if (path.Contains(".."))
            {
                _logger.LogWarning("鍐欏叆鎿嶄綔妫€娴嬪埌鐩綍閬嶅巻灏濊瘯: {Path}", path);
                return BadRequest(new { error = "闈炴硶璺緞" });
            }

            var notesRoot = System.IO.Path.Combine(baseVaultPath, "notes");
            var filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(notesRoot, path + ".md"));
            var baseFullPath = System.IO.Path.GetFullPath(baseVaultPath);

            // 纭繚鏂囦欢璺緞鍦ㄧ煡璇嗗簱鐩綍鍐咃紙闃叉璺緞閬嶅巻锛?
            if (!filePath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("鍐欏叆璺緞閬嶅巻琚樆姝? {FilePath} 涓嶅湪 {BasePath} 鍐?, filePath, baseFullPath);
                return BadRequest(new { error = "闈炴硶璺緞" });
            }

            var dir = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            await System.IO.File.WriteAllTextAsync(filePath, request.Content);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "鍐欏叆绗旇澶辫触锛歿Path}", path);
            return StatusCode(500, new { error = "鍐欏叆澶辫触", message = ex.Message });
        }
    }

    private List<string> ExtractTags(string content)
    {
        var (tags, _, _, _, _) = ExtractFrontmatter(content);
        return tags;
    }

    private (List<string> tags, bool aiGenerated, string? aiProvider, string? aiModel, DateTime? generatedAt) ExtractFrontmatter(string content)
    {
        var tags = new List<string>();
        bool aiGenerated = false;
        string? aiProvider = null;
        string? aiModel = null;
        DateTime? generatedAt = null;

        if (!content.StartsWith("---")) return (tags, aiGenerated, aiProvider, aiModel, generatedAt);

        var endIndex = content.IndexOf("---", 3);
        if (endIndex <= 0) return (tags, aiGenerated, aiProvider, aiModel, generatedAt);

        var frontmatter = content.Substring(0, endIndex);
        var lines = frontmatter.Split('\n');

        foreach (var line in lines)
        {
            if (line.StartsWith("tags:"))
            {
                var tagPart = line.Substring(5).Trim();
                if (tagPart.StartsWith("["))
                {
                    var tagStr = tagPart.Trim('[', ']', ' ');
                    if (!string.IsNullOrWhiteSpace(tagStr))
                    {
                        tags.AddRange(tagStr.Split(',').Select(t => t.Trim().Trim('"', '\'')));
                    }
                }
            }
            else if (line.StartsWith("ai_generated:"))
            {
                var val = line.Substring("ai_generated:".Length).Trim();
                aiGenerated = val.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("ai_provider:"))
            {
                aiProvider = line.Substring("ai_provider:".Length).Trim().Trim('"', '\'');
            }
            else if (line.StartsWith("ai_model:"))
            {
                aiModel = line.Substring("ai_model:".Length).Trim().Trim('"', '\'');
            }
            else if (line.StartsWith("generated_at:"))
            {
                var val = line.Substring("generated_at:".Length).Trim().Trim('"', '\'');
                if (DateTimeOffset.TryParse(val, out var dto))
                    generatedAt = dto.DateTime;
            }
        }

        return (tags.Take(10).ToList(), aiGenerated, aiProvider, aiModel, generatedAt);
    }
}
