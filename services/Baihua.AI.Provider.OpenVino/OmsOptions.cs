using Microsoft.Extensions.Options;

namespace Baihua.AI.Provider.OpenVino;

/// <summary>
/// OpenVINO Model Server (OVMS) 端点配置。
///
/// 百花本地 OpenVINO 推理已统一由 Intel OVMS 常驻服务提供，不再启动自研
/// Python 服务。所有 LLM 文本对话 / 视觉识别 / 嵌入请求均路由到 OVMS 的
/// OpenAI 兼容 REST 端点（/v3/chat/completions、/v3/embeddings、/v1/models）。
/// </summary>
public class OmsOptions
{
    /// <summary>功能开关（默认开启）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OVMS REST 基地址（默认本机 8000）</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";
}

/// <summary>
/// OVMS 模型 id 映射助手：把百花内部使用的 OpenVINO 模型标识
/// （视觉 "3b"/"7b"、显示名、或 LLM 模型目录名）映射为 OVMS config.json 里注册的 model id。
/// </summary>
public static class OmsModelMap
{
    /// <summary>百花视觉内部 Id → OVMS 模型 id</summary>
    public static string VisionModelId(string internalId)
    {
        var key = (internalId ?? "").Trim();
        if (key.Equals("7b", StringComparison.OrdinalIgnoreCase))
            return "qwen2.5-vl-7b";
        return "qwen2.5-vl-7b";
    }

    /// <summary>LLM 对话模型 id（百花文本推理后端）——统一指向 OVMS 的对话模型</summary>
    public static string ChatModelId => "qwen2.5";

    /// <summary>嵌入模型 id（RAG）</summary>
    public static string EmbeddingModelId => "bge-small-zh";

    /// <summary>本地模型目录名 → OVMS 注册模型 id（用于把 OVMS 注册状态合并到目录页）</summary>
    public static string? OmsIdForDirName(string dirName) => dirName switch
    {
        "Qwen2.5-VL-7B-Instruct-int4-ov" => "qwen2.5-vl-7b",
        "Qwen2.5-7B-Instruct-int4-ov" => "qwen2.5",
        "BianCang-Qwen2.5-7B-Instruct" => "biancang",
        "bge-small-zh-v1.5" => "bge-small-zh",
        _ => null
    };

    /// <summary>OVMS 注册模型 id → 本地模型目录名（用于按目录估算模型大小/安装状态）</summary>
    public static string? DirNameForOmsId(string omsId) => omsId switch
    {
        "qwen2.5-vl-7b" => "Qwen2.5-VL-7B-Instruct-int4-ov",
        "qwen2.5" => "Qwen2.5-7B-Instruct-int4-ov",
        "biancang" => "BianCang-Qwen2.5-7B-Instruct",
        "bge-small-zh" => "bge-small-zh-v1.5",
        _ => null
    };

    /// <summary>
    /// 状态探测：查询 OVMS 是否已注册/可用某模型（懒加载，首次推理时才真正编译加载）。
    /// 返回该模型是否在 /v1/models 或 /v1/models/{id} 可见。
    /// </summary>
    public static bool IsAvailableJsonAvailable(System.Text.Json.JsonElement root)
    {
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return true;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
            return false;
        return data.GetArrayLength() > 0;
    }
}
