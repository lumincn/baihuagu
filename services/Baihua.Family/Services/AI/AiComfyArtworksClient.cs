using System.Text.Json;
using Baihua.Contracts.Ai;

namespace Baihua.Family.Services.AI;

/// <summary>
/// Comfy 生成记录客户端（一服务一数据库）：Family 的 ComfyController 经本客户端
/// 读写 AI 服务（8791）的 ComfyArtworks 表（ai.db），不再直连 ai.db。
/// </summary>
public class AiComfyArtworksClient
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiComfyArtworksClient> _logger;

    public AiComfyArtworksClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AiComfyArtworksClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private string AiBaseUrl =>
        _configuration["BAIHUA_AI_URL"]
        ?? _configuration["TASK_RUNNER_AI_API_URL"]
        ?? "http://127.0.0.1:8791";

    /// <summary>历史生成记录（最近 N 条，可按类型过滤）</summary>
    public async Task<List<AiComfyArtworkDto>> ListAsync(int limit = 50, string? kind = null, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromSeconds(10);
            var query = $"limit={limit}";
            if (!string.IsNullOrEmpty(kind)) query += $"&kind={Uri.EscapeDataString(kind)}";
            var items = await client.GetFromJsonAsync<List<AiComfyArtworkDto>>(
                $"{AiBaseUrl.TrimEnd('/')}/api/ai/comfy/artworks?{query}", _jsonOpts, ct);
            return items ?? new List<AiComfyArtworkDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "经 AI 服务读取 Comfy 历史失败");
            return new List<AiComfyArtworkDto>();
        }
    }

    /// <summary>保存一条生成记录</summary>
    public async Task<AiComfyArtworkDto?> CreateAsync(SaveAiComfyArtworkRequest request, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.PostAsJsonAsync($"{AiBaseUrl.TrimEnd('/')}/api/ai/comfy/artworks", request, _jsonOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("保存 Comfy 记录失败：HTTP {(int)resp.StatusCode} {Body}",
                    resp.StatusCode, body.Length > 300 ? body[..300] : body);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<AiComfyArtworkDto>(_jsonOpts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "经 AI 服务保存 Comfy 记录失败");
            return null;
        }
    }

    /// <summary>删除一条历史记录</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.DeleteAsync($"{AiBaseUrl.TrimEnd('/')}/api/ai/comfy/artworks/{id}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "经 AI 服务删除 Comfy 记录 {Id} 失败", id);
            return false;
        }
    }
}
