namespace TaskRunner.Services
{
    /// <summary>
    /// 自动化备份定时服务
    /// 定期执行全量备份，并清理过期备份文件
    /// </summary>
    public class BackupSchedulerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackupSchedulerService> _logger;
        private readonly TimeSpan _backupInterval;
        private readonly int _retainCount;
        private readonly string? _backupDir;

        public BackupSchedulerService(
            IServiceProvider serviceProvider,
            ILogger<BackupSchedulerService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            // 默认每天备份一次，保留最近 7 个备份
            var intervalHours = configuration.GetValue<int?>("Backup:IntervalHours") ?? 24;
            _backupInterval = TimeSpan.FromHours(Math.Max(1, intervalHours));
            _retainCount = configuration.GetValue<int?>("Backup:RetainCount") ?? 7;
            _backupDir = configuration.GetValue<string?>("Backup:Directory");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("自动备份服务启动，备份间隔: {Interval} 小时，保留数量: {Retain}",
                _backupInterval.TotalHours, _retainCount);

            // 首次启动等待 5 分钟，避免与其他初始化服务争抢资源
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunBackupAsync(stoppingToken);
                    await CleanupOldBackupsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自动备份失败");
                }

                try
                {
                    await Task.Delay(_backupInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunBackupAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();

            var result = await backupService.CreateFullBackupAsync(_backupDir, cancellationToken: stoppingToken);

            if (result.Success)
            {
            }
            else
            {
                _logger.LogError("定时备份失败: {Error}", result.Error);
            }
        }

        private Task CleanupOldBackupsAsync(CancellationToken stoppingToken)
        {
            try
            {
                var backupDir = string.IsNullOrEmpty(_backupDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HuajiBackups")
                    : _backupDir;

                if (!Directory.Exists(backupDir))
                    return Task.CompletedTask;

                var files = Directory.GetFiles(backupDir, "huaji_backup_*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                if (files.Count <= _retainCount)
                    return Task.CompletedTask;

                var toDelete = files.Skip(_retainCount).ToList();
                foreach (var file in toDelete)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "删除过期备份失败: {Path}", file.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期备份失败");
            }

            return Task.CompletedTask;
        }
    }
}
