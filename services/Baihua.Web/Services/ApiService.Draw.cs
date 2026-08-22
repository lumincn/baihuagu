using System.Net.Http.Json;
using Baihua.Contracts.Draw;

namespace Baihua.Web.Services
{
    public partial class ApiService
    {
        /// <summary>绘图能力状态（ComfyUI 在线 + 可用 checkpoint）。</summary>
        public async Task<DrawStatusDto?> GetDrawStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/draw/status", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DrawStatusDto>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取绘图状态失败");
                return null;
            }
        }

        /// <summary>文生图（同步等待，图片约 20-60 秒）。</summary>
        public async Task<DrawResultDto?> GenerateDrawImageAsync(DrawImageRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await PostWithMetricsAsync("/api/draw/image", JsonContent.Create(request), cancellationToken, _longHttpClient);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DrawResultDto>(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文生图失败");
                return null;
            }
        }

        /// <summary>文生视频（同步等待，视频约 1-5 分钟，走长超时客户端）。</summary>
        public async Task<DrawResultDto?> GenerateDrawVideoAsync(DrawVideoRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await PostWithMetricsAsync("/api/draw/video", JsonContent.Create(request), cancellationToken, _longHttpClient);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DrawResultDto>(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文生视频失败");
                return null;
            }
        }

        /// <summary>构造绘图文件的下载 URL（Family API 中转）。</summary>
        public string GetDrawFileUrl(string filename, string subfolder = "", string type = "output")
        {
            var baseUrl = GetPrimaryBaseUrl().TrimEnd('/');
            var qs = $"filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
            return $"{baseUrl}/api/draw/file?{qs}";
        }
    }
}
