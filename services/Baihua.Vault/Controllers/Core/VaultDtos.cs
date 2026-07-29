
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


namespace Baihua.Vault.Controllers
{
    public class VaultManifestResponse
    {
        [JsonPropertyName("cursor")]
        public long Cursor { get; set; }

        [JsonPropertyName("vaultId")]
        public string? VaultId { get; set; }
        
        [JsonPropertyName("vaultName")]
        public string? VaultName { get; set; }
        
        [JsonPropertyName("files")]
        public List<ManifestFile>? Files { get; set; }
    }

    public class ManifestFile
    {
        [JsonPropertyName("relPath")]
        public string? RelPath { get; set; }
        
        [JsonPropertyName("op")]
        public string? Op { get; set; }
        
        [JsonPropertyName("mtime")]
        public long? Mtime { get; set; }
        
        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    public class VaultNote
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Modified { get; set; }
        public List<string>? Tags { get; set; }
        public bool AiGenerated { get; set; }
        public string? AiProvider { get; set; }
        public string? AiModel { get; set; }
        public DateTime? GeneratedAt { get; set; }
    }

    public class WriteNoteRequest
    {
        public string? Content { get; set; }
    }
}
