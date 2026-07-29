using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;

namespace TaskRunner.Services;

public interface ILocalAiConfigService
{
    Task<OpenClawLocalAiConfigDto> GetLocalAiConfigAsync();
    Task<bool> SaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request);
    Task<List<OpenClawLocalModelDto>> ScanLocalModelsAsync(string provider);
    Task<LocalAiServiceStatusDto> DetectAndStartLocalAiAsync(string provider);
    Task<bool> SyncLocalModelsToOpenClawAsync(string provider);
}

public partial class LocalAiConfigService(
    IHttpClientFactory httpClientFactory,
    OpenClawConfigService openClawConfigService,
    ILogger<LocalAiConfigService> logger) : ILocalAiConfigService
{
    public Task<OpenClawLocalAiConfigDto> GetLocalAiConfigAsync()
        => openClawConfigService.GetLocalAiConfigAsync();

    public Task<bool> SaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
        => openClawConfigService.SaveLocalAiConfigAsync(request);

}
