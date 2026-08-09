using System.Text.Json.Serialization;

namespace Baihua.Contracts.LocalModels;

/// <summary>OpenVINO LLM 服务托管状态（宿主机 openvino_host.py 转发）</summary>
public class OpenVinoLlmStatusDto
{
    [JsonPropertyName("instances")]
    public List<OpenVinoLlmInstanceDto> Instances { get; set; } = new();
}

public class OpenVinoLlmInstanceDto
{
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("managed")] public bool Managed { get; set; }
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("healthy")] public bool Healthy { get; set; }
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("startedAt")] public string? StartedAt { get; set; }
    [JsonPropertyName("logFile")] public string? LogFile { get; set; }
}
