using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace TaskRunner.Services
{
    /// <summary>
    /// 知识库 FTS5 索引定时重建服务
    /// 定期检查知识库文件变化，自动重建全文索引
    /// </summary>
    public class VaultIndexSchedulerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<VaultIndexSchedulerService> _logger;
        private readonly TimeSpan _checkInterval;
        private readonly Dictionary<string, DateTime> _lastScanTimes = new();
        private readonly Dictionary<string, int> _lastFileCounts = new();

        public VaultIndexSchedulerService(
            IServiceProvider serviceProvider,
            ILogger<VaultIndexSchedulerService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            // 默认每小时检查一次，可通过配置调整
            var intervalMinutes = configuration.GetValue<int?>("VaultIndex:IntervalMinutes") ?? 60;
            _checkInterval = TimeSpan.FromMinutes(Math.Max(5, intervalMinutes));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("知识库索引定时服务启动，检查间隔: {Interval} 分钟", _checkInterval.TotalMinutes);

            // 首次启动时等待 30 秒，让其他服务初始化完成
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunIndexCheckAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "知识库索引检查失败");
                }

                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("知识库索引定时服务已停止");
        }

        private async Task RunIndexCheckAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var vaultSettings = scope.ServiceProvider.GetRequiredService<VaultSettingsService>();
            var indexer = scope.ServiceProvider.GetRequiredService<VaultNoteIndexer>();

            var vaults = vaultSettings.GetVaults();
            if (vaults.Count == 0)
            {
                _logger.LogDebug("没有配置知识库，跳过索引检查");
                return;
            }

            foreach (var vault in vaults)
            {
                if (stoppingToken.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(vault.Path) || !Directory.Exists(vault.Path))
                {
                    _logger.LogDebug("知识库路径无效: {VaultId}", vault.Id);
                    continue;
                }

                try
                {
                    var currentFiles = Directory.GetFiles(vault.Path, "*.md", SearchOption.AllDirectories);
                    var currentCount = currentFiles.Length;
                    var currentLatestWrite = currentFiles.Length > 0
                        ? currentFiles.Select(f => new FileInfo(f).LastWriteTimeUtc).Max()
                        : DateTime.MinValue;

                    var needsReindex = false;

                    if (!_lastFileCounts.TryGetValue(vault.Id, out var lastCount))
                    {
                        _logger.LogInformation("首次为知识库 {VaultName} 建立索引，文件数: {Count}", vault.Name, currentCount);
                        needsReindex = true;
                    }
                    else if (currentCount != lastCount)
                    {
                        _logger.LogInformation("知识库 {VaultName} 文件数量变化: {LastCount} -> {CurrentCount}，重建索引", 
                            vault.Name, lastCount, currentCount);
                        needsReindex = true;
                    }
                    else if (_lastScanTimes.TryGetValue(vault.Id, out var lastScan))
                    {
                        // 检查是否有文件在上次扫描后修改
                        var hasNewerFiles = currentFiles.Any(f => new FileInfo(f).LastWriteTimeUtc > lastScan);
                        if (hasNewerFiles)
                        {
                            _logger.LogInformation("知识库 {VaultName} 有新增或修改的文件，重建索引", vault.Name);
                            needsReindex = true;
                        }
                    }

                    if (needsReindex)
                    {
                        _logger.LogInformation("开始重建知识库 {VaultName} 的 FTS5 索引...", vault.Name);
                        await indexer.IndexVaultAsync(vault.Id, vault.Path, stoppingToken);
                        _logger.LogInformation("知识库 {VaultName} 的 FTS5 索引重建完成", vault.Name);

                        _lastFileCounts[vault.Id] = currentCount;
                    }

                    _lastScanTimes[vault.Id] = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查知识库 {VaultId} 索引状态时失败", vault.Id);
                }
            }
        }
    }
}
