namespace Baihua.Contracts.ComputePool;

/// <summary>
/// 百花局域网算力池 —— 对端服务器能力广播（/mg/capabilities，X-Server-Token 鉴权）。
/// 每台百花机器汇报自己的 AI 提供方/模型/算力，供其他机器发现并选用（打通物理壁垒）。
/// </summary>
public class ComputeNodeCapabilitiesDto
{
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>本机对外入口（如 http://192.168.3.13）</summary>
    public string HostUrl { get; set; } = "";

    /// <summary>
    /// 本机对外暴露的 OpenAI 兼容推理端点（对端注册提供方时填 AiBaseUrl）。
    /// 为空表示本机暂不对外提供推理（未配置 BAIHUA_PUBLIC_OPENAI_BASE_URL）。
    /// </summary>
    public string OpenAiBaseUrl { get; set; } = "";

    public List<ComputeProviderDto> Providers { get; set; } = new();

    public string? GpuName { get; set; }
    public double? GpuVramGb { get; set; }
    public int? CpuCores { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ComputeProviderDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "Tier2";
    public List<ComputeModelDto> Models { get; set; } = new();
}

public class ComputeModelDto
{
    public string Name { get; set; } = "";
    public bool IsMain { get; set; }
    public int? ContextWindow { get; set; }

    /// <summary>实测 token/s（来自本机 ModelBenchmark 最近结果；无则 null）</summary>
    public double? TokensPerSecond { get; set; }
}

/// <summary>算力池总览节点（WebUI /compute 用）</summary>
public class ComputePoolNodeDto
{
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string HostUrl { get; set; } = "";
    public string OpenAiBaseUrl { get; set; } = "";
    public bool IsLocal { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? GpuName { get; set; }
    public double? GpuVramGb { get; set; }
    public int? CpuCores { get; set; }
    public List<ComputeProviderDto> Providers { get; set; } = new();

    /// <summary>本机是否已自动注册该节点的提供方（可选用）</summary>
    public bool ProviderRegistered { get; set; }
}

public class ComputePoolViewDto
{
    public List<ComputePoolNodeDto> Nodes { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SelectComputeModelRequest
{
    public string ServerId { get; set; } = "";
    public string ModelName { get; set; } = "";
}
