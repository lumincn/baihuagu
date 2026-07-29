namespace TaskRunner.Services
{
    /// <summary>
    /// 应用启动时预热本地模型相关缓存（硬件信息 + 模型推荐）
    /// </summary>
    public class LocalModelsCacheWarmupService : BackgroundService
    {
        private readonly ILogger<LocalModelsCacheWarmupService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public LocalModelsCacheWarmupService(
            ILogger<LocalModelsCacheWarmupService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 延迟几秒，等 Kestrel 启动完成后再预热，避免日志混在启动输出中
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var hardwareService = scope.ServiceProvider.GetRequiredService<HardwareInfoService>();
                var recommendationEngine = scope.ServiceProvider.GetRequiredService<ModelRecommendationEngine>();

                _logger.LogInformation("正在预热本地模型缓存...");

                // 预热硬件信息
                hardwareService.WarmupCache();

                // 预热模型推荐（全部场景）
                var hardware = hardwareService.GetHardwareInfo();
                recommendationEngine.GetRecommendations(hardware, scenario: null, maxResults: 20);

                _logger.LogInformation("本地模型缓存预热完成");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "本地模型缓存预热失败（非关键，首次访问时会自动填充）");
            }
        }
    }
}
