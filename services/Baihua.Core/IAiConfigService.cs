using Baihua.Core.Models;
using Baihua.Core.Security;
using Baihua.Data.Entities;

namespace Baihua.Core.Services;

/// <summary>
/// AI 提供方配置数据源（一服务一数据库的读/写抽象）：
/// - AI 服务进程：<see cref="AiConfigService"/> 直读/写自己的 ai.db（唯一持有 API Key 的进程）
/// - Family 进程：经 AI 服务 HTTP API 的 HTTP 实现（Family 不接触 ai.db、不持有 API Key；
///   推理统一经 /mg/ai/v1 shim 转发）
/// </summary>
public interface IAiConfigService
{
    /// <summary>获取所有启用的 AI 提供商（不含密钥）</summary>
    List<AiProviderConfig> GetProviders();

    /// <summary>获取 API Key 配置摘要（掩码，用于设置页面显示）</summary>
    List<ApiKeySummary> GetApiKeySummaries();

    /// <summary>获取单个 Provider 配置（不含密钥）</summary>
    AiProviderConfig? GetProvider(string providerId);

    /// <summary>获取主 Provider（无主时回退第一个启用的）</summary>
    AiProviderConfig? GetMainProvider();

    /// <summary>
    /// 获取指定 Provider 的有效 API Key。
    /// 注意：Family 进程不应调用此方法（不持有 key）——HTTP 实现返回空串并告警。
    /// </summary>
    string GetApiKey(string providerId);

    /// <summary>保存 Provider 配置（plainApiKey：null=保留旧 key，""=清空，非空=更新并加密）</summary>
    void SaveProvider(AiProviderSetting setting, string? plainApiKey = null);

    /// <summary>删除 Provider 配置</summary>
    bool DeleteProvider(string providerId);
}
