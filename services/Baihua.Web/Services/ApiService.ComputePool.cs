using System.Text.Json;
using Baihua.Contracts.ComputePool;

namespace Baihua.Web.Services;

public partial class ApiService
{
    /// <summary>算力池总览（本机 + 局域网对端节点与模型/TPS）。</summary>
    public async Task<ComputePoolViewDto?> GetComputePoolAsync()
    {
        try
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            var response = await _httpClient.GetAsync("/api/compute-pool", quick.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ComputePoolViewDto>(quick.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取算力池失败");
            return null;
        }
    }

    /// <summary>立即刷新对端能力。</summary>
    public async Task<bool> RefreshComputePoolAsync()
    {
        try
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            var response = await _httpClient.PostAsync("/api/compute-pool/refresh", null, quick.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新算力池失败");
            return false;
        }
    }

    /// <summary>选用某个节点+模型为本机主 AI 提供方。</summary>
    public async Task<(bool ok, string? error)> SelectComputeModelAsync(string serverId, string modelName)
    {
        try
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            var response = await _httpClient.PostAsync("/api/compute-pool/select",
                JsonContent.Create(new SelectComputeModelRequest { ServerId = serverId, ModelName = modelName }),
                quick.Token);
            if (response.IsSuccessStatusCode)
                return (true, null);
            var body = await response.Content.ReadAsStringAsync(quick.Token);
            try
            {
                using var doc = JsonDocument.Parse(body);
                return (false, doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : body);
            }
            catch { return (false, body); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选用模型失败");
            return (false, ex.Message);
        }
    }
}
