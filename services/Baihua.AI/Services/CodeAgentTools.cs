using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Baihua.AI.Services;

/// <summary>
/// 编程 Agent 外部工具：Tavily 全网搜索 + 网页正文抓取。
/// 通过 MAF function calling（AIFunctionFactory.Create）暴露给模型，
/// 由 ChatClientAgent 自动执行"模型发工具调用 → 执行 → 回填结果 → 再生成"循环。
/// </summary>
public sealed class CodeAgentTools
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CodeAgentTools> _logger;

    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>GitNexus CLI 入口（node 脚本），可用环境变量 GITNEXUS_CLI_ENTRY 覆盖。</summary>
    private const string GitNexusEntry =
        @"C:\Users\lumin\AppData\Roaming\npm\node_modules\gitnexus\dist\cli\index.js";

    /// <summary>默认目标代码库（GitNexus 全局注册名）。</summary>
    private const string DefaultRepo = "baihuagu";

    /// <summary>GitNexus 工作目录：目标仓库根目录。</summary>
    private const string RepoRoot = @"C:\Users\lumin\src\baihuagu";

    public CodeAgentTools(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _logger = loggerFactory.CreateLogger<CodeAgentTools>();
    }

    private string? TavilyApiKey =>
        !string.IsNullOrWhiteSpace(_configuration["CodeAgent:TavilyApiKey"])
            ? _configuration["CodeAgent:TavilyApiKey"]
            : Environment.GetEnvironmentVariable("TAVILY_API_KEY");

    /// <summary>全网搜索（Tavily）。适合查最新资料、官方文档、报错排查。</summary>
    public async Task<string> TavilySearch(string query, int maxResults = 5)
    {
        var apiKey = TavilyApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return "错误：未配置 Tavily API Key（CodeAgent:TavilyApiKey 或环境变量 TAVILY_API_KEY）。";

        maxResults = Math.Clamp(maxResults, 1, 10);
        try
        {
            var payload = JsonSerializer.Serialize(new { query, max_results = maxResults });
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await SharedHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            var sb = new StringBuilder();
            var idx = 1;
            foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : "";
                var url = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                var content = item.TryGetProperty("content", out var c) ? c.GetString() : "";
                sb.AppendLine($"{idx}. {title}");
                sb.AppendLine($"   URL: {url}");
                if (!string.IsNullOrWhiteSpace(content))
                    sb.AppendLine($"   {Truncate(content, 500)}");
                sb.AppendLine();
                idx++;
            }
            return sb.Length == 0 ? "（无搜索结果）" : sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tavily 搜索失败");
            return $"错误：Tavily 搜索失败 - {ex.Message}";
        }
    }

    /// <summary>抓取网页正文并转纯文本（适合精读官方文档）。</summary>
    public async Task<string> WebFetch(string url, int maxChars = 20000)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "错误：URL 格式不正确（需要 http/https 绝对地址）。";

        try
        {
            using var response = await SharedHttp.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            return Truncate(StripHtml(html), Math.Clamp(maxChars, 1000, 50000));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网页抓取失败: {Url}", url);
            return $"错误：抓取失败 - {ex.Message}";
        }
    }

    /// <summary>微软 Learn 官方文档 MCP 端点（streamable HTTP）。</summary>
    private const string LearnMcpEndpoint = "https://learn.microsoft.com/api/mcp";

    /// <summary>搜索微软官方文档（Learn）。适合 MAF/.NET/Azure/Windows 等微软技术查证。</summary>
    public async Task<string> LearnDocsSearch(string query)
    {
        try
        {
            var args = JsonSerializer.SerializeToElement(new { query });
            var result = await CallLearnMcpAsync("microsoft_docs_search", args);
            return string.IsNullOrWhiteSpace(result) ? "（无搜索结果）" : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Learn 文档搜索失败");
            return $"错误：Learn 文档搜索失败 - {ex.Message}";
        }
    }

    /// <summary>获取微软 Learn 文档完整文章（markdown）。用于精读搜索命中的高价值页面。</summary>
    public async Task<string> LearnDocsFetch(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("microsoft.com", StringComparison.OrdinalIgnoreCase))
            return "错误：URL 必须是 microsoft.com 域的文档页面。";
        try
        {
            var args = JsonSerializer.SerializeToElement(new { url });
            var result = await CallLearnMcpAsync("microsoft_docs_fetch", args);
            return string.IsNullOrWhiteSpace(result) ? "（无内容）" : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Learn 文章获取失败");
            return $"错误：Learn 文章获取失败 - {ex.Message}";
        }
    }

    /// <summary>搜索微软官方代码示例（Learn）。生成/参考微软相关代码时用。</summary>
    public async Task<string> LearnCodeSampleSearch(string query, string? language = null)
    {
        try
        {
            var args = language is null
                ? JsonSerializer.SerializeToElement(new { query })
                : JsonSerializer.SerializeToElement(new { query, language });
            var result = await CallLearnMcpAsync("microsoft_code_sample_search", args);
            return string.IsNullOrWhiteSpace(result) ? "（无搜索结果）" : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Learn 代码示例搜索失败");
            return $"错误：Learn 代码示例搜索失败 - {ex.Message}";
        }
    }

    /// <summary>调用微软 Learn MCP（JSON-RPC over streamable HTTP，解析 SSE 响应）。</summary>
    private static async Task<string> CallLearnMcpAsync(string toolName, JsonElement arguments)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        // 1) initialize（换取会话，微软端点无状态时也可省略 session id）
        var initPayload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "baihua-codeagent", version = "1.0" }
            }
        });
        var initResp = await client.PostAsync(LearnMcpEndpoint,
            new StringContent(initPayload, Encoding.UTF8, "application/json"));
        initResp.EnsureSuccessStatusCode();
        var sessionId = initResp.Headers.TryGetValues("Mcp-Session-Id", out var sv)
            ? sv.FirstOrDefault()
            : null;

        // 2) tools/call
        var callPayload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = toolName, arguments }
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, LearnMcpEndpoint)
        {
            Content = new StringContent(callPayload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(sessionId))
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        using var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        return ParseMcpSseResult(body);
    }

    /// <summary>解析 MCP streamable HTTP 响应（SSE：event: message / data: {...}，或纯 JSON），取 result.content[].text。</summary>
    private static string ParseMcpSseResult(string body)
    {
        var dataLine = body
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
        var json = dataLine is null ? body : dataLine["data:".Length..].Trim();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.AppendLine(t.GetString());
                else if (item.ValueKind == JsonValueKind.String)
                    sb.AppendLine(item.GetString());
            }
            return sb.ToString().TrimEnd();
        }
        // 工具返回的是结构化结果（如 {results:[...]}），转成易读文本
        if (doc.RootElement.TryGetProperty("result", out var r2) && r2.ValueKind == JsonValueKind.Object)
        {
            return FormatLearnResult(r2);
        }
        return doc.RootElement.ToString();
    }

    /// <summary>把 Learn 工具的结构化结果（results 数组）格式化为易读文本。</summary>
    private static string FormatLearnResult(JsonElement result)
    {
        if (!result.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return result.ToString();
        var sb = new StringBuilder();
        var idx = 1;
        foreach (var item in results.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : "";
            var url = item.TryGetProperty("contentUrl", out var u) ? u.GetString()
                : item.TryGetProperty("link", out var l) ? l.GetString() : "";
            var content = item.TryGetProperty("content", out var c) ? c.GetString()
                : item.TryGetProperty("description", out var d) ? d.GetString() : "";
            var code = item.TryGetProperty("codeSnippet", out var cs) ? cs.GetString() : null;
            var lang = item.TryGetProperty("language", out var lg) ? lg.GetString() : null;
            sb.AppendLine($"{idx}. {title}");
            if (!string.IsNullOrWhiteSpace(url)) sb.AppendLine($"   URL: {url}");
            if (!string.IsNullOrWhiteSpace(content)) sb.AppendLine($"   {Truncate(content, 600)}");
            if (!string.IsNullOrWhiteSpace(code))
            {
                sb.AppendLine($"   代码示例({lang}):");
                sb.AppendLine(Truncate(code, 800));
            }
            sb.AppendLine();
            idx++;
        }
        return sb.Length == 0 ? "（无结果）" : sb.ToString().TrimEnd();
    }

    /// <summary>在 GitNexus 知识图谱中按概念搜索代码（找功能/流程的实现位置与涉及文件）。</summary>
    public async Task<string> GitNexusQuery(string query, string? repo = null)
    {
        return await RunGitNexusAsync(["query", query, "--repo", repo ?? DefaultRepo, "--limit", "5"]);
    }

    /// <summary>查看代码符号的 360° 上下文：谁调用它、它调用谁、参与哪些执行流。</summary>
    public async Task<string> GitNexusContext(string symbol, string? repo = null)
    {
        return await RunGitNexusAsync(["context", symbol, "--repo", repo ?? DefaultRepo, "--limit", "20"]);
    }

    /// <summary>分析修改某符号的影响范围（爆炸半径）：upstream=谁依赖它，downstream=它依赖什么。</summary>
    public async Task<string> GitNexusImpact(string target, string direction = "upstream", string? repo = null)
    {
        return await RunGitNexusAsync(["impact", target, "--direction", direction, "--repo", repo ?? DefaultRepo, "--limit", "30"]);
    }

    private async Task<string> RunGitNexusAsync(string[] args)
    {
        var entry = Environment.GetEnvironmentVariable("GITNEXUS_CLI_ENTRY") ?? GitNexusEntry;
        try
        {
            var argLine = string.Join(' ', args.Select(a => "\"" + a + "\""));
            var psi = new ProcessStartInfo("node", $"\"{entry}\" {argLine}")
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 gitnexus 进程");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await proc.WaitForExitAsync(cts.Token);
            var stdout = (await stdoutTask).Trim();
            _ = await stderrTask;

            if (proc.ExitCode != 0)
                return $"gitnexus 失败(exit {proc.ExitCode})：{Truncate(stdout, 500)}";
            return string.IsNullOrEmpty(stdout) ? "（无输出/未找到）" : Truncate(stdout, 8000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitNexus 调用失败");
            return $"错误：GitNexus 调用失败 - {ex.Message}";
        }
    }

    private static string StripHtml(string html)
    {
        // 去掉 script/style 块
        html = System.Text.RegularExpressions.Regex.Replace(html, @"(?is)<(script|style)[^>]*>.*?</\1>", " ");
        // 标签转空白
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        // HTML 实体解码
        html = WebUtility.HtmlDecode(html);
        // 折叠空白
        return System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Trim();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
