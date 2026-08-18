namespace Baihua.Core.Services;

/// <summary>
/// AI 服务（8791）地址解析（一服务一数据库：Family/Vault 经 HTTP 访问 AI 服务）。
/// 与 Family 转发中间件/各 HTTP 客户端保持一致：环境变量优先，默认本机 8791。
/// </summary>
public static class AiServiceEndpoints
{
    public static string ResolveAiBaseUrl()
    {
        return Environment.GetEnvironmentVariable("BAIHUA_AI_URL")
            ?? Environment.GetEnvironmentVariable("TASK_RUNNER_AI_API_URL")
            ?? "http://127.0.0.1:8791";
    }
}
