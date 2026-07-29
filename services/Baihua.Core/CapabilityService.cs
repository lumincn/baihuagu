using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.Capability;
using Baihua.Contracts.LocalModels;
using Baihua.Core.Localization;

namespace Baihua.Family.Services;

/// <summary>
/// 需要本地算力的功能标识
/// </summary>
public enum LocalComputeFeature
{
    LocalModelsPage,
    ModelBenchmark,
    HardwareBenchmark,
    OpenClawLocalConfig,
    SettingsLocalModelDownload,
    MessagesLocalModelSelector,
    AiConfigLocalProviderPresets,
    LocalModelDeployment,
    LocalAiInference,
}

/// <summary>
/// 能力评估服务：根据硬件信息决定哪些功能可以展示
/// </summary>
public class CapabilityService
{
    private readonly HardwareInfoService _hardwareInfo;
    private readonly ILogger<CapabilityService> _logger;
    private readonly IStringLocalizer<SharedResources> _loc;
    private MachineCapability? _cachedCapability;
    private readonly object _lock = new();

    public CapabilityService(
        HardwareInfoService hardwareInfo,
        ILogger<CapabilityService> logger,
        IStringLocalizer<SharedResources> loc)
    {
        _hardwareInfo = hardwareInfo;
        _logger = logger;
        _loc = loc;
    }

    /// <summary>
    /// 获取当前机器的能力等级（缓存）
    /// </summary>
    public MachineCapability GetCapability()
    {
        if (_cachedCapability.HasValue)
            return _cachedCapability.Value;

        lock (_lock)
        {
            if (_cachedCapability.HasValue)
                return _cachedCapability.Value;

            _cachedCapability = ComputeCapability();
            _logger.LogInformation("机器能力评估: {Capability}", _cachedCapability.Value);
            return _cachedCapability.Value;
        }
    }

    /// <summary>
    /// 刷新能力评估（硬件变更后调用）
    /// </summary>
    public MachineCapability RefreshCapability()
    {
        lock (_lock)
        {
            _cachedCapability = null;
            return GetCapability();
        }
    }

    /// <summary>
    /// 判断指定功能是否可用
    /// </summary>
    public bool CanUse(LocalComputeFeature feature)
    {
        var cap = GetCapability();
        return feature switch
        {
            LocalComputeFeature.LocalModelsPage => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.ModelBenchmark => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.LocalModelDeployment => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.LocalAiInference => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.HardwareBenchmark => true,
            LocalComputeFeature.OpenClawLocalConfig => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.SettingsLocalModelDownload => cap >= MachineCapability.CpuOnly,
            LocalComputeFeature.MessagesLocalModelSelector => cap >= MachineCapability.LowEndGpu,
            LocalComputeFeature.AiConfigLocalProviderPresets => cap >= MachineCapability.LowEndGpu,
            _ => true
        };
    }

    /// <summary>
    /// 获取功能限制说明
    /// </summary>
    public string? GetRestrictionReason(LocalComputeFeature feature)
    {
        if (CanUse(feature)) return null;

        var cap = GetCapability();
        return cap switch
        {
            MachineCapability.Insufficient => _loc["Capability_MemoryInsufficient"],
            MachineCapability.CpuOnly => _loc["Capability_NoGpu"],
            _ => _loc["Capability_Insufficient"]
        };
    }

    /// <summary>
    /// 获取完整的能力信息（供前端使用）
    /// </summary>
    public CapabilityInfo GetCapabilityInfo()
    {
        var cap = GetCapability();
        var hardware = _hardwareInfo.GetHardwareInfo();

        return new CapabilityInfo
        {
            Level = cap,
            TotalRamGiB = hardware.Memory.TotalGiB,
            MaxVramGiB = hardware.Gpus
                .Where(g => g.VramGiB.HasValue)
                .Max(g => g.VramGiB) ?? 0,
            GpuName = hardware.Gpus.FirstOrDefault(g => !g.IsIntegrated)?.Name
                ?? hardware.Gpus.FirstOrDefault()?.Name
                ?? "无",
            AvailableFeatures = Enum.GetValues<LocalComputeFeature>()
                .Where(f => CanUse(f))
                .Select(f => f.ToString())
                .ToList(),
            RestrictedFeatures = Enum.GetValues<LocalComputeFeature>()
                .Where(f => !CanUse(f))
                .ToDictionary(
                    f => f.ToString(),
                    f => GetRestrictionReason(f) ?? ""
                )
        };
    }

    private MachineCapability ComputeCapability()
    {
        try
        {
            var hardware = _hardwareInfo.GetHardwareInfo();
            var tier = HardwareInfoService.GetHardwareTier(hardware);
            var ramGiB = hardware.Memory.TotalGiB;

            if (ramGiB < 8)
                return MachineCapability.Insufficient;

            return tier switch
            {
                HardwareTier.CpuOnly => MachineCapability.CpuOnly,
                HardwareTier.LowEndGpu => MachineCapability.LowEndGpu,
                HardwareTier.MidRangeGpu => MachineCapability.MidEndGpu,
                HardwareTier.HighEndGpu => MachineCapability.HighEndGpu,
                HardwareTier.TopTierGpu => MachineCapability.HighEndGpu,
                _ => ramGiB >= 8 ? MachineCapability.CpuOnly : MachineCapability.Insufficient
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "能力评估失败，默认返回 Insufficient");
            return MachineCapability.Insufficient;
        }
    }


}
