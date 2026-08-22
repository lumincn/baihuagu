namespace Baihua.Contracts.Ai;

/// <summary>
/// AI 提供方备份条目（全量备份 ZIP 内 db/ai_providers.json 的格式）。
/// API Key 不存明文：备份时用备份密码（BackupPassword）或 AI 服务机器密钥（MachineKey）重新加密。
/// 一服务一数据库：备份/恢复的加解密只发生在 AI 服务进程内（唯一持有 API Key 的进程）。
/// </summary>
public class AiProviderBackupItem
{
    /// <summary>数据库自增 Id（兼容旧格式；恢复时忽略）</summary>
    public int Id { get; set; }

    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? AnthropicBaseUrl { get; set; }
    public string ProtectedApiKey { get; set; } = "";

    /// <summary>密钥保护方式：BackupPassword（备份密码加密）/ MachineKey（机器指纹加密）/ Plaintext（旧格式 PLAINTEXT: 前缀）</summary>
    public string KeyProtection { get; set; } = "MachineKey";

    public string ModelsJson { get; set; } = "[]";
    public bool IsMain { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 恢复备份中的 AI 提供方（POST /api/ai/config/import 请求体）。
/// </summary>
public class ImportAiProvidersRequest
{
    public List<AiProviderBackupItem> Providers { get; set; } = new();

    /// <summary>备份密码（条目 KeyProtection=BackupPassword 时用于解密）</summary>
    public string? Password { get; set; }

    /// <summary>是否先清空现有提供方再导入（全量恢复 overwrite 模式）</summary>
    public bool ReplaceAll { get; set; }
}

