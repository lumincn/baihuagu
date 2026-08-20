using Baihua.Contracts.Ai;

namespace Baihua.AI.Provider;

/// <summary>
/// 本地视觉推理提供方接口（厂商中立）。
///
/// OpenVINO 视觉（Qwen2.5-VL，vision_server.py）当前实现；未来 NVIDIA（CUDA/VL 模型）、
/// AMD（ROCm）可实现同一接口接入。Family/AI 只依赖本接口，不依赖具体厂商类型。
/// </summary>
public interface ILocalVisionInference
{
    /// <summary>功能开关</summary>
    bool Enabled { get; }

    /// <summary>视觉服务状态（含可用模型）</summary>
    Task<VisionStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>识别图片（所有视觉提供方共有的核心能力）</summary>
    Task<VisionResultDto> RecognizeAsync(
        byte[] imageBytes, string prompt, string modelId, CancellationToken cancellationToken = default);

    /// <summary>确保视觉服务运行（首次调用冷启动）</summary>
    Task EnsureServerRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止视觉服务。对于常驻托管后端（如 OVMS）无独立服务可停，默认返回 false；
    /// 自启进程型实现（如自研 Python 服务）可覆盖。
    /// </summary>
    Task<bool> StopServerAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
