using Microsoft.Extensions.Logging;
using TaskRunner.Contracts.Capability;
using TaskRunner.Contracts.LocalModels;

namespace TaskRunner.Services;

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
    private MachineCapability? _cachedCapability;
    private readonly object _lock = new();

    public CapabilityService(
        HardwareInfoService hardwareInfo,
        ILogger<CapabilityService> logger)
    {
        _hardwareInfo = hardwareInfo;
        _logger = logger;
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
            MachineCapability.Insufficient => "当前机器内存不足（< 8GB），无法使用本地模型功能。建议使用云端 AI 服务。",
            MachineCapability.CpuOnly => "当前机器无独立显卡，本地大模型功能已隐藏。您可以继续使用云端 AI 服务。",
            _ => "当前机器配置不足以使用该功能。"
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
