using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.OpenClaw;
using Baihua.Core.Localization;
using Baihua.Family.Helpers;

namespace Baihua.Family.Services;

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
    ILogger<LocalAiConfigService> logger,
    IStringLocalizer<SharedResources> loc) : ILocalAiConfigService
{
    private readonly IStringLocalizer<SharedResources> _loc = loc;

    public Task<OpenClawLocalAiConfigDto> GetLocalAiConfigAsync()
        => openClawConfigService.GetLocalAiConfigAsync();

    public Task<bool> SaveLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
        => openClawConfigService.SaveLocalAiConfigAsync(request);

}
