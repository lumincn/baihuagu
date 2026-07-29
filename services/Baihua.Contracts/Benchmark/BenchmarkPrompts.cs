namespace TaskRunner.Contracts.Benchmark;

/// <summary>
/// 内置基准测试数据：推荐模型 + 测试提示词
/// </summary>
public static class BenchmarkPrompts
{
    #region 显存等级划分

    public static readonly int[] VramTiers = new[] { 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 256, 512, 1024 };

    #endregion

    #region 大模型推荐（按显存等级 + 精度）

    public static readonly List<VramTierDto> TcmVramTiers = new()
    {
        new()
        {
            VramGb = 4,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-3B-Instruct", Params = "3B", SizeGb = "~1.8", Description = "阿里云轻量通用模型，设定专家角色后可处理基础知识问答", OllamaName = "qwen2.5:3b" , Company = "阿里云" },
                new() { Name = "Phi-3-mini-3.8B", Params = "3.8B", SizeGb = "~2.2", Description = "微软轻量模型，推理效率高，适合基础知识问答", OllamaName = "phi3:3.8b" , Company = "Microsoft" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-1.5B", Params = "1.5B", SizeGb = "~0.9", Description = "阿里云超轻量模型，可设定专家角色进行简单问诊", OllamaName = "qwen2.5-coder:1.5b" , Company = "阿里云" },
                new() { Name = "CodeGemma-2B", Params = "2B", SizeGb = "~2.4", Description = "Google轻量模型，通用能力尚可，可辅助基础问答", OllamaName = "codegemma:2b" , Company = "Google" }
            }
        },
        new()
        {
            VramGb = 8,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-7B-Instruct", Params = "7B", SizeGb = "~4.2", Description = "CCL2025任务冠军基座，设定专家角色后辨证论治能力强、幻觉低", OllamaName = "qwen2.5:7b" , Company = "阿里云" },
                new() { Name = "HuatuoGPT-II-7B", Params = "7B", SizeGb = "~4.2", Description = "港中文开源医疗大模型，融合真实医生回复，支持知识问答", OllamaName = "hf.co/FreedomIntelligence/HuatuoGPT2-7B-GGUF:Q4_K_M" , Company = "其他" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-7B-Instruct", Params = "7B", SizeGb = "~7.8", Description = "INT8精度更高，术语理解更准确，辨证推理更稳定", OllamaName = "qwen2.5:7b" , Company = "阿里云" },
                new() { Name = "HuatuoGPT-II-7B", Params = "7B", SizeGb = "~7.8", Description = "INT8医疗模型，减少量化损失，药方推荐更精确", OllamaName = "hf.co/FreedomIntelligence/HuatuoGPT2-7B-GGUF:Q8_0" , Company = "其他" }
            }
        },
        new()
        {
            VramGb = 12,
            Int4Models = new()
            {
                new() { Name = "BianCang-Qwen2.5-7B", Params = "7B", SizeGb = "~4.2", Description = "齐鲁工业大学专用模型，辨证论治数据微调，开方能力最强", OllamaName = "hf.co/QLU-NLP/BianCang-Qwen2.5-7B-Instruct-GGUF:Q4_K_M" , Company = "阿里云" },
                new() { Name = "Qwen2.5-14B-Instruct", Params = "14B", SizeGb = "~8.5", Description = "14B通用强基模型，专家角色设定后理论深度和经典解释能力显著提升", OllamaName = "qwen2.5:14b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-7B-Instruct", Params = "7B", SizeGb = "~7.8", Description = "INT8精度，7B级别最高质量知识问答", OllamaName = "qwen2.5:7b" , Company = "阿里云" },
                new() { Name = "HuatuoGPT-II-7B", Params = "7B", SizeGb = "~7.8", Description = "INT8医疗模型，12GB显存下最稳的知识问答选择", OllamaName = "hf.co/FreedomIntelligence/HuatuoGPT2-7B-GGUF:Q8_0" , Company = "其他" }
            }
        },
        new()
        {
            VramGb = 16,
            Int4Models = new()
            {
                new() { Name = "BianCang-Qwen2.5-14B", Params = "14B", SizeGb = "~8.5", Description = "扁仓大模型14B版本，专用，疑难证候分析质量更高", OllamaName = "hf.co/QLU-NLP/BianCang-Qwen2.5-14B-Instruct-GGUF:Q4_K_M" , Company = "阿里云" },
                new() { Name = "Qwen2.5-14B-Instruct", Params = "14B", SizeGb = "~8.5", Description = "通义千问14B通用模型，专家角色设定后综合能力优秀", OllamaName = "qwen2.5:14b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-14B-Instruct", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，经典条文理解和方剂解析精度最高", OllamaName = "qwen2.5:14b" , Company = "阿里云" },
                new() { Name = "BianCang-Qwen2.5-7B", Params = "7B", SizeGb = "~7.8", Description = "扁仓7B INT8，低幻觉，适合对精度要求高的处方推荐", OllamaName = "hf.co/QLU-NLP/BianCang-Qwen2.5-7B-Instruct-GGUF:Q8_0" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 24,
            Int4Models = new()
            {
                new() { Name = "BianCang-Qwen2.5-14B", Params = "14B", SizeGb = "~8.5", Description = "扁仓14B专用，24GB显存可轻松运行，留出余量处理长上下文医案", OllamaName = "hf.co/QLU-NLP/BianCang-Qwen2.5-14B-Instruct-GGUF:Q4_K_M" , Company = "阿里云" },
                new() { Name = "Qwen2.5-32B-Instruct", Params = "32B", SizeGb = "~19", Description = "32B通用强基+专家角色设定，理论深度和推理能力达到本地模型顶级", OllamaName = "qwen2.5:32b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-14B-Instruct", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，高质量辨证分析，24GB下可开启更大上下文", OllamaName = "qwen2.5:14b" , Company = "阿里云" },
                new() { Name = "DeepSeek-R1-Distill-Qwen-14B", Params = "14B", SizeGb = "~14", Description = "深度思考模型，推理链对复杂多步骤辨证逻辑有帮助", OllamaName = "deepseek-r1:14b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 32,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-32B-Instruct", Params = "32B", SizeGb = "~19", Description = "32B通用模型+专家角色，本地知识问答质量接近云端大模型", OllamaName = "qwen2.5:32b" , Company = "阿里云" },
                new() { Name = "DeepSeek-R1-Distill-Qwen-32B", Params = "32B", SizeGb = "~19", Description = "32B推理模型，复杂辨证的多步推理链非常清晰，适合教学演示", OllamaName = "deepseek-r1:32b" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-14B-Instruct", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，32GB下可并发加载多个模型或处理超长医案", OllamaName = "qwen2.5:14b" , Company = "阿里云" },
                new() { Name = "DeepSeek-R1-Distill-Qwen-14B", Params = "14B", SizeGb = "~14", Description = "14B推理模型INT8，辨证过程可解释性最强", OllamaName = "deepseek-r1:14b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 48,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-72B-Instruct", Params = "72B", SizeGb = "~42", Description = "72B通用巨模型+专家角色，本地可运行的最大通用模型之一，理论深度极佳", OllamaName = "qwen2.5:72b" , Company = "阿里云" },
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~42", Description = "Meta最新70B模型，多语言能力强，经典翻译和理解优秀", OllamaName = "llama3.3:70b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-32B-Instruct", Params = "32B", SizeGb = "~36", Description = "32B INT8，精度与速度的最佳平衡，处方推荐高可靠", OllamaName = "qwen2.5:32b" , Company = "阿里云" },
                new() { Name = "DeepSeek-R1-Distill-Qwen-32B", Params = "32B", SizeGb = "~36", Description = "32B推理模型INT8，复杂疑难证候的逐步分析最详细", OllamaName = "deepseek-r1:32b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 64,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-72B-Instruct", Params = "72B", SizeGb = "~42", Description = "72B Q4，64GB下运行游刃有余，可开启超大上下文处理完整医案", OllamaName = "qwen2.5:72b" , Company = "阿里云" },
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~42", Description = "70B Q4，多语言文献理解和翻译能力顶尖", OllamaName = "llama3.3:70b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "阿里云MoE巨模型，激活参数22B，推理效率极高，理论深度极佳", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~45", Description = "DeepSeek MoE模型，激活参数37B，辨证分析推理能力极强", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 96,
            Int4Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "MoE架构，96GB可轻松运行，知识覆盖广度最大", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~45", Description = "671B MoE模型，复杂病案分析能力接近专业医师水平", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-72B-Instruct", Params = "72B", SizeGb = "~78", Description = "72B INT8，本地模型最高精度，处方推荐最可靠", OllamaName = "qwen2.5:72b" , Company = "阿里云" },
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~78", Description = "70B INT8，经典原文理解和古文翻译最精准", OllamaName = "llama3.3:70b" , Company = "Meta" }
            }
        },
        new()
        {
            VramGb = 128,
            Int4Models = new()
            {
                new() { Name = "QwQ-32B-Preview", Params = "32B", SizeGb = "~19", Description = "阿里推理模型，128GB可同时运行多个大模型进行会诊对比", OllamaName = "qwq:32b" , Company = "阿里云" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "MoE巨模型，128GB下可处理超长医案全文", OllamaName = "qwen3:235b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~80", Description = "MoE INT8，理论深度和推理精度达到本地极限", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~90", Description = "671B MoE INT8，复杂疑难杂症的辨证分析最详尽", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 256,
            Int4Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "Meta 405B巨模型，本地可运行的最大dense模型，理论体系最完整", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "256GB可同时加载405B+MoE，实现多模型交叉验证", OllamaName = "qwen3:235b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~78", Description = "70B INT8，256GB可并发运行多个实例服务多用户知识问答", OllamaName = "llama3.3:70b" , Company = "Meta" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~80", Description = "MoE INT8，可同时加载多个专科模型并行推理", OllamaName = "qwen3:235b" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 512,
            Int4Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "405B Q4，512GB可同时运行多个超大模型进行方剂对比研究", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE Q4，知识库覆盖最广，疑难杂症分析能力顶尖", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~480", Description = "405B INT8，本地dense模型最高精度，古籍原文理解最准确", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE INT8，临床决策支持最强模型", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 1024,
            Int4Models = new()
            {
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE，1024GB可同时加载所有顶级模型，构建AI超级计算中心", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" },
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "405B Q4，可大规模并发处理医案分析和处方推荐", OllamaName = "llama3.1:405b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~800", Description = "671B MoE INT8，本地可运行的最大规模模型，诊疗能力接近顶级人类专家", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" },
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~480", Description = "405B INT8，dense架构最高精度，经典文献理解和考证最权威", OllamaName = "llama3.1:405b" , Company = "Meta" }
            }
        }
    };

    #endregion

    #region 编程大模型推荐（按显存等级 + 精度）

    public static readonly List<VramTierDto> CodingVramTiers = new()
    {
        new()
        {
            VramGb = 4,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-1.5B", Params = "1.5B", SizeGb = "~0.9", Description = "阿里云代码专用轻量模型，适合简单脚本和基础算法", OllamaName = "qwen2.5-coder:1.5b" , Company = "阿里云" },
                new() { Name = "Qwen2.5-Coder-3B", Params = "3B", SizeGb = "~1.8", Description = "3B代码模型，HumanEval约55%，适合快速代码补全", OllamaName = "qwen2.5-coder:3b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-1.5B", Params = "1.5B", SizeGb = "~1.8", Description = "1.5B INT8，语法正确率最高，适合代码格式化和小功能实现", OllamaName = "qwen2.5-coder:1.5b" , Company = "阿里云" },
                new() { Name = "CodeGemma-2B", Params = "2B", SizeGb = "~2.4", Description = "Google代码模型，FIM补全速度快，IDE集成首选", OllamaName = "codegemma:2b" , Company = "Google" }
            }
        },
        new()
        {
            VramGb = 8,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-7B", Params = "7B", SizeGb = "~4.2", Description = "HumanEval 72%，40+语言支持，C#代码生成质量优秀", OllamaName = "qwen2.5-coder:7b" , Company = "阿里云" },
                new() { Name = "DeepSeek-Coder-6.7B", Params = "6.7B", SizeGb = "~4", Description = "DeepSeek代码模型，填空补全(FIM)能力强，适合IDE插件", OllamaName = "deepseek-coder:6.7b" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-7B", Params = "7B", SizeGb = "~7.8", Description = "7B INT8，代码逻辑准确性更高，Bug率更低", OllamaName = "qwen2.5-coder:7b" , Company = "阿里云" },
                new() { Name = "StarCoder2-3B", Params = "3B", SizeGb = "~3.5", Description = "HuggingFace代码模型，3B INT8下FIM补全质量最佳", OllamaName = "starcoder2:3b" , Company = "其他" }
            }
        },
        new()
        {
            VramGb = 12,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-14B", Params = "14B", SizeGb = "~8.5", Description = "Aider得分69.2%，多文件重构和复杂算法设计能力强", OllamaName = "qwen2.5-coder:14b" , Company = "阿里云" },
                new() { Name = "CodeLlama-13B", Params = "13B", SizeGb = "~8", Description = "Meta代码模型，13B级别HumanEval基准表现稳定", OllamaName = "codellama:13b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-7B", Params = "7B", SizeGb = "~7.8", Description = "7B INT8，12GB下最稳的代码生成选择，复杂逻辑少出错", OllamaName = "qwen2.5-coder:7b" , Company = "阿里云" },
                new() { Name = "DeepSeek-Coder-6.7B", Params = "6.7B", SizeGb = "~8", Description = "6.7B INT8，代码补全和生成长文本稳定性好", OllamaName = "deepseek-coder:6.7b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 16,
            Int4Models = new()
            {
                new() { Name = "DeepSeek-Coder-V2-Lite-16B", Params = "16B(MoE)", SizeGb = "~10", Description = "MoE架构，HumanEval 81%，16GB下推理速度快于同参数dense模型", OllamaName = "deepseek-coder-v2:16b" , Company = "DeepSeek" },
                new() { Name = "Qwen2.5-Coder-14B", Params = "14B", SizeGb = "~8.5", Description = "14B Q4，16GB下可开启更大上下文处理大型代码库", OllamaName = "qwen2.5-coder:14b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-14B", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，代码生成精度和正确率显著提升", OllamaName = "qwen2.5-coder:14b" , Company = "阿里云" },
                new() { Name = "CodeLlama-13B", Params = "13B", SizeGb = "~15", Description = "13B INT8，长代码生成连贯性更好", OllamaName = "codellama:13b" , Company = "Meta" }
            }
        },
        new()
        {
            VramGb = 24,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-32B", Params = "32B", SizeGb = "~19", Description = "HumanEval 87%，本地编程模型冠军，C#质量接近GPT-4o Mini", OllamaName = "qwen2.5-coder:32b" , Company = "阿里云" },
                new() { Name = "Codestral-22B", Params = "22B", SizeGb = "~13", Description = "Mistral代码模型，RepoBench长上下文代码任务表现顶尖", OllamaName = "codestral:22b" , Company = "其他" }
            },
            Int8Models = new()
            {
                new() { Name = "DeepSeek-Coder-V2-Lite-16B", Params = "16B(MoE)", SizeGb = "~20", Description = "MoE INT8，24GB下性价比最高的高质量代码模型", OllamaName = "deepseek-coder-v2:16b" , Company = "DeepSeek" },
                new() { Name = "Qwen2.5-Coder-14B", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，24GB下可处理更大代码文件的上下文分析", OllamaName = "qwen2.5-coder:14b" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 32,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-32B", Params = "32B", SizeGb = "~19", Description = "32B Q4，32GB显存最佳代码模型，多语言跨文件重构能力强", OllamaName = "qwen2.5-coder:32b" , Company = "阿里云" },
                new() { Name = "DeepSeek-R1-Distill-Qwen-32B", Params = "32B", SizeGb = "~19", Description = "推理型代码模型，复杂Bug调试和算法设计逐步推导清晰", OllamaName = "deepseek-r1:32b" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "DeepSeek-Coder-V2-Lite-16B", Params = "16B(MoE)", SizeGb = "~20", Description = "16B MoE INT8，32GB下可并发处理多个代码分析任务", OllamaName = "deepseek-coder-v2:16b" , Company = "DeepSeek" },
                new() { Name = "Qwen2.5-Coder-14B", Params = "14B", SizeGb = "~15.5", Description = "14B INT8，32GB下最稳定的生产级代码生成选择", OllamaName = "qwen2.5-coder:14b" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 48,
            Int4Models = new()
            {
                new() { Name = "Qwen2.5-Coder-32B", Params = "32B", SizeGb = "~19", Description = "32B Q4，48GB下可开启128K上下文处理大型代码库", OllamaName = "qwen2.5-coder:32b" , Company = "阿里云" },
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~42", Description = "Meta 70B通用模型，代码理解+架构设计能力强，适合全栈开发", OllamaName = "llama3.3:70b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen2.5-Coder-32B", Params = "32B", SizeGb = "~36", Description = "32B INT8，代码生成精度最高，复杂算法实现最可靠", OllamaName = "qwen2.5-coder:32b" , Company = "阿里云" },
                new() { Name = "CodeLlama-34B", Params = "34B", SizeGb = "~34", Description = "34B INT8，代码补全和长函数生成质量优秀", OllamaName = "codellama:34b" , Company = "Meta" }
            }
        },
        new()
        {
            VramGb = 64,
            Int4Models = new()
            {
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~42", Description = "70B Q4，64GB下运行流畅，代码架构设计和审查能力顶尖", OllamaName = "llama3.3:70b" , Company = "Meta" },
                new() { Name = "Qwen2.5-72B", Params = "72B", SizeGb = "~42", Description = "72B Q4，中文代码注释和文档生成最佳", OllamaName = "qwen2.5:72b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "MoE架构，激活参数22B，推理效率极高，代码生成速度快", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~45", Description = "671B MoE，代码理解和复杂系统架构设计能力极强", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 96,
            Int4Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "MoE Q4，96GB可处理超大型代码库的全局分析和重构", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~45", Description = "671B MoE，代码生成质量接近顶级闭源模型", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Llama-3.3-70B", Params = "70B", SizeGb = "~78", Description = "70B INT8，dense架构最高精度代码生成", OllamaName = "llama3.3:70b" , Company = "Meta" },
                new() { Name = "Qwen2.5-72B", Params = "72B", SizeGb = "~78", Description = "72B INT8，中文代码项目理解和生成最精准", OllamaName = "qwen2.5:72b" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 128,
            Int4Models = new()
            {
                new() { Name = "QwQ-32B-Preview", Params = "32B", SizeGb = "~19", Description = "阿里推理模型，128GB可同时运行多个模型进行代码方案对比", OllamaName = "qwq:32b" , Company = "阿里云" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "MoE巨模型，128GB下可处理完整企业级代码仓库分析", OllamaName = "qwen3:235b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~80", Description = "MoE INT8，代码推理精度和深度达到本地极限", OllamaName = "qwen3:235b" , Company = "阿里云" },
                new() { Name = "DeepSeek-V3 (MoE)", Params = "671B(MoE)", SizeGb = "~90", Description = "671B MoE INT8，复杂系统架构设计和代码审查最详尽", OllamaName = "deepseek-v3" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 256,
            Int4Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "Meta 405B巨模型，本地最大dense模型，代码理论体系最完整", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~40", Description = "256GB可同时加载405B+MoE，实现代码多模型交叉验证", OllamaName = "qwen3:235b" , Company = "阿里云" }
            },
            Int8Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~480", Description = "405B INT8，本地dense代码模型最高精度", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "Qwen3-235B-A22B (MoE)", Params = "235B(MoE)", SizeGb = "~80", Description = "MoE INT8，可并发服务多用户代码生成请求", OllamaName = "qwen3:235b" , Company = "阿里云" }
            }
        },
        new()
        {
            VramGb = 512,
            Int4Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "405B Q4，512GB可同时运行多个超大模型进行代码对比研究", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE Q4，代码知识库覆盖最广，复杂架构设计顶尖", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" }
            },
            Int8Models = new()
            {
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~480", Description = "405B INT8，dense架构代码模型精度巅峰", OllamaName = "llama3.1:405b" , Company = "Meta" },
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE INT8，企业级代码审查和架构决策支持最强", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" }
            }
        },
        new()
        {
            VramGb = 1024,
            Int4Models = new()
            {
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~400", Description = "671B MoE，1024GB可构建代码AI超级计算中心，大规模并发处理", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" },
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~240", Description = "405B Q4，可大规模并发代码生成和审查服务", OllamaName = "llama3.1:405b" , Company = "Meta" }
            },
            Int8Models = new()
            {
                new() { Name = "DeepSeek-V3-671B (MoE)", Params = "671B(MoE)", SizeGb = "~800", Description = "671B MoE INT8，本地可运行最大规模模型，代码能力接近顶级人类专家", OllamaName = "deepseek-v3:671b" , Company = "DeepSeek" },
                new() { Name = "Llama-3.1-405B", Params = "405B", SizeGb = "~480", Description = "405B INT8，dense架构最高精度，代码理论和算法理解最权威", OllamaName = "llama3.1:405b" , Company = "Meta" }
            }
        }
    };

    #endregion

    #region 测试提示词

    private static string JoinLines(params string[] lines) => string.Join("\n", lines);

    public static readonly List<BenchmarkPrompt> TcmPrompts = new()
    {
        new()
        {
            Id = "tcm-01",
            Category = "tcm",
            Title = "基础理论：肝主疏泄",
            Prompt = JoinLines(
                "请详细解释医学中\"肝主疏泄\"的生理意义和常见病理表现。要求：",
                "1. 说明疏泄功能的具体内涵（调畅气机、促进脾胃运化、调畅情志、促进生殖功能等）；",
                "2. 列举肝失疏泄的常见证型（肝气郁结、肝火上炎、肝阳上亢等）及其临床表现；",
                "3. 简述调理原则。"),
            ExpectedKeywords = new[] { "气机", "脾胃", "情志", "郁结", "肝火", "疏肝" },
            MaxTokens = 800
        },
        new()
        {
            Id = "tcm-02",
            Category = "tcm",
            Title = "辨证论治：失眠案",
            Prompt = JoinLines(
                "患者，女，45岁。主诉：失眠多梦3月余。",
                "现病史：入睡困难，易醒，醒后难以再睡，伴心悸健忘，面色少华，神疲乏力，食欲不振，舌淡苔薄白，脉细弱。",
                "请完成：",
                "1. 辨病与辨证（证型）；",
                "2. 辨证依据；",
                "3. 治法；",
                "4. 推荐方剂（写明方名及主要药物组成）。"),
            ExpectedKeywords = new[] { "心脾两虚", "归脾汤", "人参", "黄芪", "当归", "养血安神" },
            MaxTokens = 800
        },
        new()
        {
            Id = "tcm-03",
            Category = "tcm",
            Title = "方剂分析：四君子汤",
            Prompt = JoinLines(
                "请分析方剂\"四君子汤\"：",
                "1. 组成药物及剂量；",
                "2. 功效与主治；",
                "3. 方解（君臣佐使分析）；",
                "4. 现代常用加减变化（至少举2例）。"),
            ExpectedKeywords = new[] { "人参", "白术", "茯苓", "甘草", "健脾益气", "君子" },
            MaxTokens = 800
        },
        new()
        {
            Id = "tcm-04",
            Category = "tcm",
            Title = "中药知识：黄芪",
            Prompt = JoinLines(
                "请详细介绍中药\"黄芪\"：",
                "1. 性味归经；",
                "2. 功效主治；",
                "3. 常用配伍（至少3组）；",
                "4. 用法用量及使用注意（禁忌）。"),
            ExpectedKeywords = new[] { "甘", "微温", "脾", "肺", "补气升阳", "固表止汗", "利水消肿" },
            MaxTokens = 800
        },
        new()
        {
            Id = "tcm-05",
            Category = "tcm",
            Title = "经典条文：伤寒论太阳病",
            Prompt = JoinLines(
                "《伤寒论》原文：\"太阳之为病，脉浮，头项强痛而恶寒。\"",
                "请解释：",
                "1. 本条文的临床意义；",
                "2. \"太阳\"在六经辨证中的定位；",
                "3. 出现上述症状的病机分析；",
                "4. 对应的治法和代表方剂。"),
            ExpectedKeywords = new[] { "麻黄汤", "桂枝汤", "表证", "风寒", "营卫" },
            MaxTokens = 800
        }
    };

    public static readonly List<BenchmarkPrompt> CodingPrompts = new()
    {
        new()
        {
            Id = "code-01",
            Category = "coding",
            Title = "C#算法：泛型快速排序",
            Prompt = JoinLines(
                "请用C#实现一个泛型快速排序算法，要求：",
                "1. 支持任意实现了IComparable<T>的类型；",
                "2. 使用递归实现分区逻辑；",
                "3. 包含完整的XML文档注释；",
                "4. 提供一个简单的单元测试示例（使用xUnit风格）。",
                "请只输出代码，不要额外解释。"),
            ExpectedKeywords = new[] { "QuickSort", "IComparable", "T", "Partition", "array", "recursive" },
            MaxTokens = 800
        },
        new()
        {
            Id = "code-02",
            Category = "coding",
            Title = "C#特性：async并行下载",
            Prompt = JoinLines(
                "请用C#编写一个方法，使用async/await并行下载多个文件，要求：",
                "1. 接收URL列表和本地保存目录；",
                "2. 限制最大并发数为5；",
                "3. 每个下载任务有独立的超时控制（10秒）；",
                "4. 正确处理异常，返回成功和失败的下载结果；",
                "5. 使用HttpClient和SemaphoreSlim。",
                "请只输出代码。"),
            ExpectedKeywords = new[] { "async", "Task", "HttpClient", "SemaphoreSlim", "Parallel", "Download" },
            MaxTokens = 900
        },
        new()
        {
            Id = "code-03",
            Category = "coding",
            Title = "C#设计：线程安全LRU缓存",
            Prompt = JoinLines(
                "请用C#实现一个线程安全的LRU（最近最少使用）缓存类，要求：",
                "1. 支持泛型键值对；",
                "2. 固定容量，超出时淘汰最久未使用的项；",
                "3. 线程安全（使用lock或ConcurrentDictionary）；",
                "4. 提供Get、Set、Remove方法；",
                "5. O(1)时间复杂度。",
                "请只输出代码。"),
            ExpectedKeywords = new[] { "class", "LRU", "ConcurrentDictionary", "LinkedList", "lock", "Capacity" },
            MaxTokens = 900
        },
        new()
        {
            Id = "code-04",
            Category = "coding",
            Title = "C#审查：找出并发Bug",
            Prompt = JoinLines(
                "以下C#代码存在并发安全问题，请指出所有bug并给出修复后的正确代码：",
                "```csharp",
                "public class Counter",
                "{",
                "    private int _count = 0;",
                "    public int Count => _count;",
                "    public void Increment() { _count++; }",
                "    public void Decrement() { _count--; }",
                "}",
                "```",
                "要求说明问题原因和修复方案。"),
            ExpectedKeywords = new[] { "Interlocked", "volatile", "lock", "thread-safe", "race condition", "atomic" },
            MaxTokens = 700
        },
        new()
        {
            Id = "code-05",
            Category = "coding",
            Title = "C#优化：LINQ重构嵌套循环",
            Prompt = JoinLines(
                "请将以下嵌套循环代码用LINQ重构为更简洁高效的实现：",
                "```csharp",
                "var result = new List<string>();",
                "foreach (var order in orders)",
                "{",
                "    if (order.Amount > 100)",
                "    {",
                "        foreach (var item in order.Items)",
                "        {",
                "            if (item.IsAvailable)",
                "            {",
                "                result.Add(item.Name + \" - \" + order.CustomerName);",
                "            }",
                "        }",
                "    }",
                "}",
                "```",
                "请输出重构后的代码，并解释性能优势。"),
            ExpectedKeywords = new[] { "SelectMany", "Where", "LINQ", "lambda", "ToList", "Select" },
            MaxTokens = 700
        }
    };

    #endregion

    #region 辅助方法

    public static List<BenchmarkPrompt> GetPromptsByCategory(string category)
    {
        return category switch
        {
            "tcm" => TcmPrompts,
            "coding" => CodingPrompts,
            _ => TcmPrompts.Concat(CodingPrompts).ToList()
        };
    }

    public static List<VramTierDto> GetTiersByCategory(string category)
    {
        return category switch
        {
            "tcm" => TcmVramTiers,
            "coding" => CodingVramTiers,
            _ => TcmVramTiers.Concat(CodingVramTiers).ToList()
        };
    }

    public static List<RecommendedBenchmarkModel> GetModelsByCategory(string category)
    {
        // 兼容旧接口：从显存等级中展平获取
        var tiers = GetTiersByCategory(category);
        var result = new List<RecommendedBenchmarkModel>();
        foreach (var tier in tiers)
        {
            foreach (var m in tier.Int4Models)
                result.Add(new RecommendedBenchmarkModel
                {
                    Id = m.Name.ToLowerInvariant().Replace(" ", "-").Replace(".", "-"),
                    Name = m.Name,
                    Category = category,
                    Description = m.Description,
                    SizeInfo = m.Params,
                    VramInfo = $"{tier.VramGb}GB级/{m.SizeGb}",
                    OllamaName = m.OllamaName,
                    Tags = new[] { category, "INT4" }
                });
            foreach (var m in tier.Int8Models)
                result.Add(new RecommendedBenchmarkModel
                {
                    Id = m.Name.ToLowerInvariant().Replace(" ", "-").Replace(".", "-") + "-q8",
                    Name = m.Name + " (INT8)",
                    Category = category,
                    Description = m.Description,
                    SizeInfo = m.Params,
                    VramInfo = $"{tier.VramGb}GB级/{m.SizeGb}",
                    OllamaName = m.OllamaName,
                    Tags = new[] { category, "INT8" }
                });
        }
        return result;
    }

    #endregion
}
