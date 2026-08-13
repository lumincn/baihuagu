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
