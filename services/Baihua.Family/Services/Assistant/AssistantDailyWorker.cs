using Microsoft.Extensions.Logging;

namespace Baihua.Family.Services;

/// <summary>
/// 助理每日分析定时器：按设置的分析时间（默认 23:00）执行一次当日兴趣分析。
/// 每小时检查一次；当天已分析则跳过；开关关闭则跳过。
/// </summary>
public class AssistantDailyWorker : BackgroundService
{
    private readonly ILogger<AssistantDailyWorker> _logger;
    private readonly IServiceProvider _services;

    public AssistantDailyWorker(ILogger<AssistantDailyWorker> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后 2 分钟先检查一次（服务重启后当天没分析可以补上），然后每小时检查
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndAnalyzeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "助理定时检查失败");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CheckAndAnalyzeAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var assistant = scope.ServiceProvider.GetRequiredService<AssistantService>();
        var activities = scope.ServiceProvider.GetRequiredService<UserActivityService>();

        var settings = assistant.GetSettings();
        if (!settings.Enabled) return;
        if (assistant.IsTodayAnalyzed()) return;

        // 到达分析时间（或已过时间但今天还没分析）且有活动记录才分析
        var now = DateTime.Now;
        if (now.Hour < settings.AnalyzeHour) return;

        var todayActivities = activities.GetActivities(DateTime.Today);
        if (todayActivities.Count == 0) return;

        _logger.LogInformation("助理开始每日分析（{Count} 条活动）", todayActivities.Count);
        await assistant.AnalyzeAsync(force: false, ct);

        // 顺带清理超期数据
        activities.CleanupOld(settings.RetentionDays);
    }
}
