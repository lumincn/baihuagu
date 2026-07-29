namespace WebUI.Services;

/// <summary>AI 模型信息</summary>
public class AiModelInfo
{
    public string Name { get; set; } = "";
    public bool IsPaid { get; set; }
    public bool IsMain { get; set; }
}

/// <summary>后端 GET /api/ai/providers 返回项（与 TaskRunner DTO 字段对齐）。</summary>
public class AiProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsMain { get; set; }
    public List<AiModelInfo> Models { get; set; } = new();
}
