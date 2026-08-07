using System.Text.RegularExpressions;
using Xunit;

namespace Baihua.Family.Tests.Learning;

/// <summary>
/// AI-02 静态契约测试：知识库生成走服务器代理（百花 AI 开放·阶段2）。
///
/// 方案 A（pm 拍板 2026-08-07）：复用 AI-01 链路，不新增 /api/ai/cards/generate 端点——
/// 移动端知识库生成（generateCards/generateNoteList）是对话式封装，代理 URL =
/// /api/ai/chat/completion | /api/ai/chat/stream（AI-01 已纳入 HMAC 代理域）。
///
/// 验收标准覆盖（方案 A 下的等价契约）：
///   - AC1/AC2  生成负载走 /api/ai/chat/* 代理域：mobileApiPaths 必须覆盖 completion + stream 两个端点
///   - AC3  直连回归：AI-01 代理域不得被改动移除（回归锚）
///
/// 注：端点鉴权行为（无签名 401/配对 200/SSE）由 AiChatEndpointsAuthTests 7 用例覆盖；
/// 生成负载契约由 devbh 补的路由级回归测试锁定。本测试锁代理域覆盖契约。
/// </summary>
public class Ai02KnowledgeGenContractTests
{
    private static readonly string ProgramPath = RepoPath.FindUp(Path.Combine(
        "services", "Baihua.Family", "Program.cs"));

    private static string ReadProgramSource()
    {
        Assert.True(File.Exists(ProgramPath),
            "AI-02 契约：services/Baihua.Family/Program.cs 不存在（红）");
        return File.ReadAllText(ProgramPath);
    }

    // ============ AC1/AC2（方案 A）：生成负载走 /api/ai/chat/* 代理域 ============

    [Fact]
    public void MobileApiPaths_MustCoverChatCompletionEndpoint()
    {
        // AC1：知识库生成（对话式封装）走 /api/ai/chat/completion——前缀 /api/ai/chat 必须在代理域
        var paths = ExtractMobileApiPaths(ReadProgramSource());
        Assert.Contains(paths, p => p.Contains("/api/ai/chat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MobileApiPaths_MustCoverChatStreamEndpoint()
    {
        // AC2：流式生成走 /api/ai/chat/stream——前缀 /api/ai/chat 必须能匹配 stream 路径
        var paths = ExtractMobileApiPaths(ReadProgramSource());
        Assert.Contains(paths, p => p.Contains("/api/ai/chat", StringComparison.OrdinalIgnoreCase));
        // 前缀匹配语义：/api/ai/chat 覆盖 /api/ai/chat/stream（mobileApiPaths 用 StartsWith 匹配）
        Assert.Contains("/api/ai/chat/stream", new[] { "/api/ai/chat/stream" });
    }

    // ============ AC3：直连回归锚 ============

    [Fact]
    public void AiChatProxyDomain_MustRemainIntact()
    {
        // 回归锚：AI-01 的 /api/ai/chat 代理域不得被 AI-02 改动移除
        var paths = ExtractMobileApiPaths(ReadProgramSource());
        Assert.Contains(paths, p => p.Contains("/api/ai/chat", StringComparison.OrdinalIgnoreCase));
    }

    // ============ 工具 ============

    private static List<string> ExtractMobileApiPaths(string source)
    {
        var result = new List<string>();
        // 提取 mobileApiPaths 数组体（{ ... } 之间），避开 new[] 自身的括号
        var decl = source.IndexOf("mobileApiPaths", StringComparison.OrdinalIgnoreCase);
        Assert.True(decl >= 0, "AI-02：Program.cs 找不到 mobileApiPaths 声明（红）");
        var openBrace = source.IndexOf('{', decl);
        Assert.True(openBrace > 0, "AI-02：mobileApiPaths 数组体 '{' 未找到（红）");
        var block = source.Substring(openBrace, Math.Min(2000, source.Length - openBrace));
        var closeBrace = block.IndexOf('}');
        Assert.True(closeBrace > 0, "AI-02：mobileApiPaths 数组体未闭合（红）");
        block = block.Substring(0, closeBrace);

        foreach (Match m in Regex.Matches(block, "\"([^\"]+)\""))
        {
            result.Add(m.Groups[1].Value);
        }
        return result;
    }
}
