namespace Baihua.Contracts.LocalModels;

/// <summary>
/// OpenVINO 可下载模型目录（静态数据，仓库已经 hf-mirror 实测验证可达）。
/// 下载源：HuggingFace 镜像（hf-mirror.com，国内直连快），
/// OpenVINO 官方组织仓库（INT4 量化，适配 Intel Arc 核显 GPU）。
/// </summary>
public static class OpenVinoCatalog
{
    public static IReadOnlyList<OpenVinoCatalogEntry> All => _entries;

    private static readonly List<OpenVinoCatalogEntry> _entries =
    [
        new()
        {
            Id = "deepseek-r1-7b",
            Name = "DeepSeek R1 Distill Qwen 7B",
            ParameterSize = "7B",
            SizeGiB = 4.7,
            Description = "深度推理/数学/逻辑，思维链强",
            ModelScopeRepo = "OpenVINO/DeepSeek-R1-Distill-Qwen-7B-INT4-OV",
            HuggingFaceRepo = "openvino/DeepSeek-R1-Distill-Qwen-7B-INT4-OV",
        },
        new()
        {
            Id = "kokoro-82m",
            Name = "Kokoro 82M（TTS 语音合成）",
            ParameterSize = "82M",
            SizeGiB = 0.17,
            Description = "中英文语音合成，独立 Python TTS 服务推理（非 OVMS）",
            IsTts = true,
            ModelScopeRepo = "OpenVINO/Kokoro-82M-int8-ov",
            HuggingFaceRepo = "openvino/Kokoro-82M-int8-ov",
        },
        new()
        {
            Id = "biancang-instruct",
            Name = "扁仓 BianCang Instruct（医疗）",
            ParameterSize = "7B",
            SizeGiB = 14.2,
            Description = "中文医疗领域指令模型。下载 safetensors 源文件（~14GB），本地转 OpenVINO INT4 IR（~4.5GB）",
            IsMedical = true,
            ModelScopeRepo = "QLU-NLP/BianCang-Qwen2.5-7B-Instruct",
            HuggingFaceRepo = "QLU-NLP/BianCang-Qwen2.5-7B-Instruct",
            Format = "safetensors",
        },
        new()
        {
            Id = "qwen2.5-7b",
            Name = "Qwen 2.5 7B Instruct",
            ParameterSize = "7B",
            SizeGiB = 4.7,
            Description = "通用对话/文本生成，OVMS 默认对话模型（qwen2.5）",
            ModelScopeRepo = "OpenVINO/Qwen2.5-7B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-7B-Instruct-INT4-OV",
            DirectoryName = "Qwen2.5-7B-Instruct-int4-ov",
        },
        new()
        {
            Id = "qwen2.5-14b",
            Name = "Qwen 2.5 14B Instruct",
            ParameterSize = "14B",
            SizeGiB = 9.0,
            Description = "高质量长文本，建议内存 ≥ 16G",
            ModelScopeRepo = "OpenVINO/Qwen2.5-14B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-14B-Instruct-INT4-OV",
        },
        new()
        {
            Id = "qwen2.5-coder-7b",
            Name = "Qwen 2.5 Coder 7B",
            ParameterSize = "7B",
            SizeGiB = 4.7,
            Description = "代码生成/补全/解释，编程 Agent 可用",
            ModelScopeRepo = "OpenVINO/Qwen2.5-Coder-7B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-Coder-7B-Instruct-INT4-OV",
        },
        new()
        {
            Id = "qwen2.5-coder-14b",
            Name = "Qwen 2.5 Coder 14B",
            ParameterSize = "14B",
            SizeGiB = 9.0,
            Description = "更强代码能力，建议内存 ≥ 16G",
            ModelScopeRepo = "OpenVINO/Qwen2.5-Coder-14B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-Coder-14B-Instruct-INT4-OV",
        },
        new()
        {
            Id = "qwen2.5-vl-7b",
            Name = "Qwen 2.5 VL 7B（视觉）",
            ParameterSize = "7B",
            SizeGiB = 5.5,
            Description = "图像理解/OCR，接入视觉工具（vision_server）",
            IsVision = true,
            ModelScopeRepo = "OpenVINO/Qwen2.5-VL-7B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-VL-7B-Instruct-INT4-OV",
            DirectoryName = "Qwen2.5-VL-7B-Instruct-int4-ov",
        },
        new()
        {
            Id = "qwen2.5-vl-3b",
            Name = "Qwen 2.5 VL 3B（视觉）",
            ParameterSize = "3B",
            SizeGiB = 2.6,
            Description = "轻量图像理解/OCR，显存占用小",
            IsVision = true,
            ModelScopeRepo = "OpenVINO/Qwen2.5-VL-3B-Instruct-INT4-OV",
            HuggingFaceRepo = "openvino/Qwen2.5-VL-3B-Instruct-INT4-OV",
            DirectoryName = "Qwen2.5-VL-3B-Instruct-int4-ov",
        },
        new()
        {
            Id = "qwen3.5-9b",
            Name = "Qwen 3.5 9B（int8）",
            ParameterSize = "9B",
            SizeGiB = 8.8,
            Description = "新一代多模态大模型（int8 量化，含视觉/文本嵌入）",
            IsVision = true,
            ModelScopeRepo = "OpenVINO/Qwen3.5-9B-int8-ov",
            HuggingFaceRepo = "openvino/Qwen3.5-9B-int8-ov",
            DirectoryName = "Qwen3.5-9B-int8-ov",
        },

    ];

    public static OpenVinoCatalogEntry? GetById(string id) =>
        _entries.FirstOrDefault(e => e.Id == id);
}
