using System.Text.Json;
using Baihua.Family.Services.ComputePool;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers.ComputePool;

/// <summary>
/// 算力池统一推理网关（/mg/pool/v1，X-Server-Token 或 Bearer 鉴权）。
/// 任何一台百花机器都能按模型名调用全网算力：
/// - 本机 AI 服务（OpenAI shim /mg/ai/v1）有该模型 → 转发本机；
/// - 本机没有 → 按实测 TPS 路由到拥有该模型且最快的对端（对端网关递归处理）；
/// - 全网都没有 → 404。
/// 请求/响应按字节流透传（支持流式 SSE），网关只解析 model 字段做路由。
/// </summary>
[ApiController]
public class ComputeGatewayController : ControllerBase
{
    private readonly ComputePoolService _poolService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComputeGatewayController> _logger;

    public ComputeGatewayController(
        ComputePoolService poolService,
        IConfiguration configuration,
        ILogger<ComputeGatewayController> logger)
    {
        _poolService = poolService;
        _configuration = configuration;
        _logger = logger;
    }

    private bool Authorize()
    {
        var expected = _configuration["BAIHUA_AI_EXTERNAL_TOKEN"] ?? "";
        if (string.IsNullOrEmpty(expected)) return true;
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return string.Equals(auth["Bearer ".Length..].Trim(), expected, StringComparison.Ordinal);
        var token = Request.Headers["X-Server-Token"].FirstOrDefault();
        return string.Equals(token, expected, StringComparison.Ordinal);
    }

    /// <summary>全网可用模型列表（本机 AI 服务 + 对端广播）。</summary>
    [HttpGet("/mg/pool/v1/models")]
    public async Task<ActionResult<object>> ListModels(CancellationToken ct)
    {
        if (!Authorize()) return Unauthorized(new { error = "invalid token" });
        var view = await _poolService.GetPoolViewAsync(ct);
        var models = view.Nodes
            .SelectMany(n => n.Providers)
            .SelectMany(p => p.Models)
            .Select(m => new { id = m.Name, @object = "model", owned_by = "pool" })
            .DistinctBy(m => m.id)
            .ToList();
        return Ok(new { @object = "list", data = models });
    }

    /// <summary>聊天补全（OpenAI 兼容，按模型名路由全网最快节点）。</summary>
    [HttpPost("/mg/pool/v1/chat/completions")]
    public async Task ChatCompletions(CancellationToken ct)
    {
        if (!Authorize())
        {
            Response.StatusCode = 401;
            await Response.WriteAsJsonAsync(new { error = new { message = "invalid token", type = "invalid_request_error" } });
            return;
        }

        string modelName;
        string body;
        try
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(body);
            modelName = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
        }
        catch (Exception ex)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = new { message = ex.Message, type = "invalid_request_error" } });
            return;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = new { message = "model is required", type = "invalid_request_error" } });
            return;
        }

        var node = await _poolService.FindBestNodeAsync(modelName, ct);
        if (node == null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = new { message = $"全网无模型 {modelName}", type = "not_found" } });
            return;
        }

        _logger.LogInformation("[ComputePool] 网关路由 {Model} → {Node} ({Tps} t/s)",
            modelName, node.Value.name, node.Value.tps?.ToString("F1") ?? "—");

        // 透传请求到目标节点（OpenAI 兼容端点 + /chat/completions）
        var target = $"{node.Value.baseUrl.TrimEnd('/')}/chat/completions";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var forward = new HttpRequestMessage(HttpMethod.Post, target);
            forward.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            // 携带鉴权：优先 Bearer（配置了 BAIHUA_AI_EXTERNAL_TOKEN 时），否则用互联口令
            var aiToken = _configuration["BAIHUA_AI_EXTERNAL_TOKEN"] ?? "";
            var msgToken = _configuration["BAIHUA_SERVER_MSG_TOKEN"] ?? "";
            var bearer = !string.IsNullOrEmpty(aiToken) ? aiToken : msgToken;
            if (!string.IsNullOrEmpty(bearer))
                forward.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");

            using var resp = await client.SendAsync(forward, HttpCompletionOption.ResponseHeadersRead, ct);
            Response.StatusCode = (int)resp.StatusCode;
            foreach (var h in resp.Headers)
            {
                if (h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                Response.Headers[h.Key] = h.Value.ToArray();
            }
            foreach (var h in resp.Content.Headers)
            {
                if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                Response.Headers[h.Key] = h.Value.ToArray();
            }
            await resp.Content.CopyToAsync(Response.Body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网关转发失败 {Model} → {Target}", modelName, target);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 502;
                await Response.WriteAsJsonAsync(new { error = new { message = $"路由失败: {ex.Message}", type = "upstream_error" } });
            }
        }
    }
}
