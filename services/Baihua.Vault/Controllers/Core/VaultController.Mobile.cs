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
    [HttpGet("cards")]
    public ActionResult<object> GetCards([FromQuery] string vaultId)
    {
        // 绉诲姩绔?API 缁熶竴浣跨敤 HMAC 绛惧悕楠岃瘉锛堝湪 Program.cs 涓棿浠朵腑瀹屾垚锛?
        // 涓嶅啀棰濆瑕佹眰 Bearer Token锛屼笌 GetManifest/GetFile 淇濇寔涓€鑷?
        var targetVault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
        if (targetVault == null || string.IsNullOrEmpty(targetVault.Path))
        {
            return NotFound(new { error = "鐭ヨ瘑搴撲笉瀛樺湪" });
        }

        var cardsPath = System.IO.Path.Combine(targetVault.Path, "cards");
        if (!System.IO.Directory.Exists(cardsPath))
        {
            return Ok(new { vaultId, count = 0, cards = new List<object>() });
        }

        var cards = new List<object>();
        var files = System.IO.Directory.GetFiles(cardsPath, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = System.IO.File.ReadAllText(file);
                var cardItems = ParseCardFile(json);
                foreach (var card in cardItems)
                {
                    var frontText = NormalizeCardField(card.Front, "front", file);
                    var backText = NormalizeCardField(card.Back, "back", file);
                    if (string.IsNullOrWhiteSpace(frontText) || string.IsNullOrWhiteSpace(backText))
                    {
                        continue;
                    }

                    cards.Add(new
                    {
                        front = frontText,
                        back = backText,
                        deck = card.Deck,
                        tags = string.Join(",", card.Tags ?? new List<string>()),
                        source = card.Source,
                        notePath = System.IO.Path.GetFileName(file)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "瑙ｆ瀽鍗＄墖鏂囦欢澶辫触锛歿File}", file);
            }
        }

        return Ok(new { vaultId, count = cards.Count, cards });
    }

    /// <summary>
    /// 瑙ｆ瀽鍗＄墖 JSON 鏂囦欢锛屾敮鎸佷袱绉嶆牸寮忥細
    /// 1. 鏃ф牸寮忥細鏂囦欢鏈韩鏄?MobileCardItem[] 鏁扮粍
    /// 2. 鏍囧噯鏍煎紡锛歿 "Name": "...", "Cards": [ ... ] }
    /// </summary>
    private static List<MobileCardItem> ParseCardFile(string json)
    {
        var result = new List<MobileCardItem>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                var card = JsonSerializer.Deserialize<MobileCardItem>(element.GetRawText());
                if (card != null) result.Add(card);
            }
            return result;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("Cards", out var cardsElement) &&
            cardsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in cardsElement.EnumerateArray())
            {
                var card = JsonSerializer.Deserialize<MobileCardItem>(element.GetRawText());
                if (card != null) result.Add(card);
            }
        }

        return result;
    }

    /// <summary>
    /// 鎶婂崱鐗囧瓧娈电粺涓€杞垚瀛楃涓层€傚吋瀹瑰瓧娈典负 JSON 鏁扮粍锛堢┖鏁扮粍鍒欒烦杩囷級鎴栧瓧绗︿覆鐨勬儏鍐点€?
    /// </summary>
    private static string NormalizeCardField(JsonElement field, string fieldName, string filePath)
    {
        switch (field.ValueKind)
        {
            case JsonValueKind.String:
                return field.GetString() ?? "";
            case JsonValueKind.Array:
                // 绌烘暟缁勮涓烘棤鏁堬紱闈炵┖鏁扮粍鎸夎鎷兼帴
                var items = field.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                return items.Count > 0 ? string.Join("\n", items!) : "";
            default:
                return "";
        }
    }

    /// <summary>
    /// 鏍囧噯鍗＄墖鏂囦欢鍖呰鏍煎紡
    /// </summary>
    private class CardFileWrapper
    {
        public string Name { get; set; } = "";
        public List<MobileCardItem> Cards { get; set; } = new();
    }

    /// <summary>
    /// 鑾峰彇绉诲姩绔璇侀厤缃紙Family 鐗堣繑鍥炲疄闄?sharedSecret锛屼緵鑷姩鍙戠幇娴佺▼浣跨敤锛?
    /// </summary>
    [HttpPost("auth/config")]
    public ActionResult<object> GetMobileAuthConfig()
    {
        return Ok(new { sharedSecret = _signatureService.GetSharedSecret() });
    }

    /// <summary>
    /// 鑾峰彇鐭ヨ瘑搴撶瑪璁版暟閲?
    /// </summary>
    [HttpGet("note-count")]
    public ActionResult<int> GetNoteCount([FromQuery] string vaultId)
    {
        var vault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
        if (vault == null)
        {
            return NotFound(new { error = "鐭ヨ瘑搴撲笉瀛樺湪", vaultId });
        }
        if (string.IsNullOrEmpty(vault.Path))
        {
            return StatusCode(500, new { error = "鐭ヨ瘑搴撹矾寰勪负绌?, vaultId });
        }

        var notesPath = System.IO.Path.Combine(vault.Path, "notes");
        if (!System.IO.Directory.Exists(notesPath))
        {
            _logger.LogWarning("鐭ヨ瘑搴?notes 鐩綍涓嶅瓨鍦細{Path}", notesPath);
            return Ok(0);
        }

        var files = System.IO.Directory.GetFiles(notesPath, "*.md", System.IO.SearchOption.AllDirectories);
        return Ok(files.Length);
    }
}
