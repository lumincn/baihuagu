namespace WebUI.Services;

/// <summary>
/// Obsidian 状态服务 - 检测 Obsidian 桌面客户端运行状态
/// </summary>
public class ObsidianStatusService
{
    private readonly IApiService _apiService;

    public ObsidianStatusService(IApiService apiService)
    {
        _apiService = apiService;
    }

    /// <summary>
    /// 获取 Obsidian 状态摘要
    /// </summary>
    public async Task<ObsidianStatusSummary> GetStatusSummaryAsync()
    {
        try
        {
            // 使用快速超时（3秒），避免阻塞页面加载
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var healthReport = await _apiService.GetFullHealthAsync(cts.Token);
            var obsidianComponent = healthReport?.Components?.FirstOrDefault(c => c.Name == "Obsidian");
            
            var isRunning = obsidianComponent?.Status == "healthy";
            
            return new ObsidianStatusSummary
            {
                IsRunning = isRunning,
                Status = isRunning ? ObsidianStatus.Running : ObsidianStatus.NotRunning,
                Message = obsidianComponent?.Message ?? "Obsidian 状态未知"
            };
        }
        catch (OperationCanceledException)
        {
            // 超时，假设Obsidian未运行
            return new ObsidianStatusSummary
            {
                IsRunning = false,
                Status = ObsidianStatus.NotRunning,
                Message = "检测超时，Obsidian 可能未运行"
            };
        }
        catch (Exception)
        {
            return new ObsidianStatusSummary
            {
                IsRunning = false,
                Status = ObsidianStatus.NotRunning,
                Message = "检测 Obsidian 状态失败"
            };
        }
    }
}

/// <summary>
/// Obsidian 运行状态
/// </summary>
public enum ObsidianStatus
{
    NotRunning,
    Running
}

/// <summary>
/// Obsidian 状态摘要
/// </summary>
public class ObsidianStatusSummary
{
    public bool IsRunning { get; set; }
    public ObsidianStatus Status { get; set; }
    public string Message { get; set; } = "";
}
