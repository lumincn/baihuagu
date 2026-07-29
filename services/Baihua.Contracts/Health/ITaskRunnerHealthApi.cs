namespace Baihua.Contracts.Health;

public interface ITaskRunnerHealthApi
{
    Task<SystemHealthReportDto> GetFullHealthAsync(CancellationToken cancellationToken = default);
}

