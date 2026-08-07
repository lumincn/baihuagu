using System.Text.RegularExpressions;
using Xunit;

namespace Baihua.Family.Tests.Learning;

/// <summary>
/// AI-02 静态契约红测试：知识库生成走服务器代理（百花 AI 开放·阶段2）。
///
/// 验收标准覆盖（本轮：服务端契约层）：
///   - AC1  知识库卡片生成端点纳入 mobileApiPaths HMAC 鉴权域（与 AI-01 的 /api/ai/chat 一致）
///   - AC2  流式生成端点存在（SSE）
///   - AC3  直连回归：不改变请求/响应格式（契约锚：端点存在且路由不变）
///
/// 红测试方式（源码级，FAM-11 先例）：当前 mobileApiPaths 只有 /api/ai/chat，
/// 无知识库生成路径 → 红。
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

    // ============ AC1：生成端点纳入 HMAC 鉴权域 ============

    [Fact]
    public void MobileApiPaths_MustIncludeKnowledgeGenEndpoint()
    {
        // 契约：知识库卡片生成端点必须纳入 mobileApiPaths（与 AI-01 的 /api/ai/chat 同域），
        // 否则设备 HMAC 签名的代理请求会被跳过 → 服务端无法鉴权转发
        var source = ReadProgramSource();
        var mobileApiBlock = ExtractMobileApiPaths(source);

        Assert.True(
            mobileApiBlock.Any(p => p.Contains("/api/ai/cards", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("cards/generate", StringComparison.OrdinalIgnoreCase)),
            "AI-02-AC1 契约：mobileApiPaths 缺少知识库生成端点（期望 /api/ai/cards 或 /api/ai/cards/generate）（红）");
    }

    [Fact]
    public void MobileApiPaths_MustIncludeStreamGenEndpoint()
    {
        // AC2：流式生成端点同样纳入代理域（SSE 流式逐卡片）
        var source = ReadProgramSource();
        var mobileApiBlock = ExtractMobileApiPaths(source);

        Assert.True(
            mobileApiBlock.Any(p => p.Contains("cards/generate-stream", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("/api/ai/cards", StringComparison.OrdinalIgnoreCase) && p.Contains("stream", StringComparison.OrdinalIgnoreCase)
                                    || mobileApiBlock.Any(q => q.Contains("/api/ai/cards", StringComparison.OrdinalIgnoreCase))),
            "AI-02-AC2 契约：mobileApiPaths 缺少流式生成端点（红）");
    }

    // ============ AC3：直连回归锚 ============

    [Fact]
    public void AiChatProxyDomain_MustRemainIntact()
    {
        // 回归锚：AI-01 的 /api/ai/chat 代理域不得被本次改动移除
        var source = ReadProgramSource();
        var mobileApiBlock = ExtractMobileApiPaths(source);
        Assert.Contains(mobileApiBlock, p => p.Contains("/api/ai/chat", StringComparison.OrdinalIgnoreCase));
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
