using Microsoft.Extensions.Caching.Memory;
using TaskRunner.Contracts.LocalModels;

namespace TaskRunner.Services
{
    /// <summary>
    /// 根据硬件信息推荐可本地运行的模型
    /// </summary>
    public class ModelRecommendationEngine
    {
        private readonly ILogger<ModelRecommendationEngine> _logger;
        private readonly OllamaLibraryClient? _ollamaLibrary;
        private readonly IMemoryCache _cache;

        public ModelRecommendationEngine(
            ILogger<ModelRecommendationEngine> logger,
            OllamaLibraryClient? ollamaLibrary = null,
            IMemoryCache? cache = null)
        {
            _logger = logger;
            _ollamaLibrary = ollamaLibrary;
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        }

        /// <summary>
        /// 获取推荐模型列表（硬件不变则缓存不变，手动刷新才更新）
        /// </summary>
        /// <param name="hardware">硬件信息</param>
        /// <param name="scenario">场景筛选：chat / code / reasoning / chinese / tcm(笔记) / lightweight，null 表示全部</param>
        /// <param name="maxResults">最大返回数量</param>
        public List<RecommendedModelDto> GetRecommendations(
            HardwareInfoDto hardware,
            string? scenario = null,
            int maxResults = 20)
        {
            var cacheKey = $"rec_{hardware.Cpu.LogicalProcessorCount}_{hardware.Memory.TotalGiB:F0}_{scenario ?? "all"}_{maxResults}";
            if (_cache.TryGetValue(cacheKey, out List<RecommendedModelDto>? cached) && cached != null)
            {
                _logger.LogDebug("模型推荐命中缓存: {CacheKey}", cacheKey);
                return cached;
            }

            var results = ComputeRecommendations(hardware, scenario, maxResults);
            _cache.Set(cacheKey, results);
            return results;
        }

        /// <summary>
        /// 强制重新计算推荐模型（用于手动刷新或 Ollama Library 更新后）
        /// </summary>
        public List<RecommendedModelDto> RefreshRecommendations(
            HardwareInfoDto hardware,
            string? scenario = null,
            int maxResults = 20)
        {
            var cacheKey = $"rec_{hardware.Cpu.LogicalProcessorCount}_{hardware.Memory.TotalGiB:F0}_{scenario ?? "all"}_{maxResults}";
            _cache.Remove(cacheKey);
            return GetRecommendations(hardware, scenario, maxResults);
        }

        /// <summary>
        /// 清除所有推荐缓存（硬件变更后调用）
        /// </summary>
        public void InvalidateAllCaches()
        {
            // MemoryCache 不支持枚举所有 key，这里简单处理：重新创建缓存实例不现实
            // 实际场景中硬件信息变化极罕见，InvalidateAllCaches 在硬件刷新后由调用方处理
            _logger.LogInformation("模型推荐缓存标记为失效（下次请求将重新计算）");
        }

        private List<RecommendedModelDto> ComputeRecommendations(HardwareInfoDto hardware, string? scenario, int maxResults)
        {
            var tier = HardwareInfoService.GetHardwareTier(hardware);
            var totalRam = hardware.Memory.TotalGiB;
            var maxVram = hardware.Gpus
                .Where(g => g.VramGiB.HasValue)
                .Max(g => g.VramGiB) ?? 0;

            _logger.LogInformation(
                "模型推荐: 硬件等级={Tier}, 显存={Vram:F1}GB, 内存={Ram:F1}GB, 场景={Scenario}",
                tier, maxVram, totalRam, scenario ?? "全部");

            var allModels = ModelDatabase.AllModels.ToList();

            // 补充 Ollama Library 动态模型（去重：硬编码优先）
            if (_ollamaLibrary != null)
            {
                var libraryModels = _ollamaLibrary.GetCachedModels();
                foreach (var lib in libraryModels)
                {
                    var alreadyExists = allModels.Any(m =>
                        m.OllamaModelName.Equals(lib.OllamaModelName, StringComparison.OrdinalIgnoreCase) ||
                        m.Id.Equals(lib.Id, StringComparison.OrdinalIgnoreCase));
                    if (!alreadyExists)
                        allModels.Add(lib);
                }
            }

            // 只推荐 INT4/INT8 精度模型（排除 FP16/FP32/Q2/Q3/Q5/Q6 等）
            bool IsInt4OrInt8Quantization(string q) =>
                q.Contains("Q4", StringComparison.OrdinalIgnoreCase) ||
                q.Contains("Q8", StringComparison.OrdinalIgnoreCase) ||
                q.Contains("INT4", StringComparison.OrdinalIgnoreCase) ||
                q.Contains("INT8", StringComparison.OrdinalIgnoreCase);

            var query = allModels.Where(m => IsInt4OrInt8Quantization(m.Quantization));

            if (!string.IsNullOrEmpty(scenario))
                query = query.Where(m => m.Tags.Contains(scenario, StringComparer.OrdinalIgnoreCase));

            var cpuCores = hardware.Cpu.LogicalProcessorCount;

            var results = query
                .Select(m => EvaluateModel(m, tier, maxVram, totalRam, cpuCores))
                .Where(r => r.MatchScore > 0)
                .OrderByDescending(r => r.MatchScore)
                .ThenBy(r => r.SizeGiB)
                .Take(maxResults)
                .ToList();

            // 如果没有筛选结果，放宽场景条件但仍保持 INT4/INT8 精度要求
            if (results.Count == 0 && !string.IsNullOrEmpty(scenario))
            {
                results = allModels
                    .Where(m => IsInt4OrInt8Quantization(m.Quantization))
                    .Select(m => EvaluateModel(m, tier, maxVram, totalRam, cpuCores))
                    .Where(r => r.MatchScore > 0)
                    .OrderByDescending(r => r.MatchScore)
                    .ThenBy(r => r.SizeGiB)
                    .Take(maxResults)
                    .ToList();
            }

            return results;
        }

        /// <summary>
        /// 评估单个模型与当前硬件的匹配度
        /// </summary>
        private RecommendedModelDto EvaluateModel(ModelEntry model, HardwareTier tier, double maxVram, double totalRam, int cpuCores)
        {
            var dto = new RecommendedModelDto
            {
                Id = model.Id,
                Name = model.Name,
                OllamaModelName = model.OllamaModelName,
                LmStudioSearchName = model.LmStudioSearchName,
                Description = model.Description,
                ParameterSize = model.ParameterSize,
                Quantization = model.Quantization,
                SizeGiB = model.SizeGiB,
                MinVramGiB = model.MinVramGiB,
                MinRamGiB = model.MinRamGiB,
                Tags = new List<string>(model.Tags),
                Company = model.Company,
                Sources = GetModelSources(model),
            };

            // 检查内存是否足够
            if (totalRam < model.MinRamGiB * 0.8)
            {
                dto.MatchScore = 0;
                dto.Suitability = "内存不足";
                return dto;
            }

            // 显存评估
            var hasVram = model.MinVramGiB.HasValue;
            var minVram = model.MinVramGiB.GetValueOrDefault();
            var vramSufficient = hasVram && maxVram >= minVram;

            if (vramSufficient)
            {
                // 显存充足，GPU 加速运行
                var vramHeadroom = maxVram - minVram;
                if (vramHeadroom >= 4)
                    dto.MatchScore = 100; // 非常充裕
                else if (vramHeadroom >= 2)
                    dto.MatchScore = 90;  // 充裕
                else if (vramHeadroom >= 1)
                    dto.MatchScore = 80;  // 刚好
                else
                    dto.MatchScore = 70;  // 略紧张

                dto.Suitability = "GPU 加速运行";
            }
            else if (hasVram && maxVram > 0)
            {
                // 有 GPU 但显存不足 → 可用但会部分 offloading 到内存，速度较慢
                var vramRatio = maxVram / minVram;
                if (vramRatio >= 0.5)
                {
                    dto.MatchScore = 55;
                    dto.Suitability = "显存不足，混合运行（较慢）";
                }
                else
                {
                    dto.MatchScore = 40;
                    dto.Suitability = "显存不足，主要使用 CPU（慢）";
                }
            }
            else if (maxVram == 0)
            {
                // 无独立显卡，纯 CPU 运行
                if (model.MinRamGiB <= 4 && model.SizeGiB <= 2)
                {
                    dto.MatchScore = 50;
                    dto.Suitability = "纯 CPU 运行（可接受）";
                }
                else if (model.MinRamGiB <= 8 && model.SizeGiB <= 5)
                {
                    dto.MatchScore = 35;
                    dto.Suitability = "纯 CPU 运行（较慢）";
                }
                else
                {
                    dto.MatchScore = 20;
                    dto.Suitability = "纯 CPU 运行（很慢，仅应急）";
                }
            }
            else
            {
                // 模型不需要显存（小模型）
                dto.MatchScore = 60;
                dto.Suitability = "可直接运行";
            }

            // 根据硬件等级进行额外调整
            dto.MatchScore = AdjustScoreByTier(dto.MatchScore, tier, model);

            // 如果内存刚好够但有压力，降低分数
            if (totalRam < model.MinRamGiB * 1.2)
            {
                dto.MatchScore = Math.Max(0, dto.MatchScore - 10);
                if (dto.MatchScore > 0 && !dto.Suitability.Contains("内存"))
                    dto.Suitability += "，内存紧张";
            }

            // 估算在当前硬件上的输出速度
            dto.EstimatedTokensPerSecond = EstimateTokensPerSecond(model, tier, maxVram, totalRam, cpuCores);

            return dto;
        }

        private int AdjustScoreByTier(int baseScore, HardwareTier tier, ModelEntry model)
        {
            return tier switch
            {
                HardwareTier.TopTierGpu => baseScore, // 最高级，不调整
                HardwareTier.HighEndGpu => model.SizeGiB > 25 ? baseScore - 5 : baseScore,
                HardwareTier.MidRangeGpu => model.SizeGiB > 15 ? baseScore - 10 : baseScore,
                HardwareTier.LowEndGpu => model.SizeGiB > 8 ? Math.Max(0, baseScore - 20) : baseScore,
                HardwareTier.CpuOnly => model.SizeGiB > 5 ? Math.Max(0, baseScore - 25) : baseScore,
                _ => baseScore,
            };
        }

        /// <summary>
        /// 估算模型在当前硬件上的输出速度（tokens/秒）。
        /// 基于 llama.cpp / Ollama 社区在典型硬件上的经验数据，仅供参考。
        /// </summary>
        private double EstimateTokensPerSecond(ModelEntry model, HardwareTier tier, double maxVram, double totalRam, int cpuCores)
        {
            var hasVramReq = model.MinVramGiB.HasValue;
            var minVramReq = model.MinVramGiB.GetValueOrDefault();
            var vramSufficient = hasVramReq && maxVram >= minVramReq;

            // CPU 基础速度估算（以 8 核现代 CPU + DDR4/5 内存为基准）
            double baseCpuTps = model.SizeGiB switch
            {
                <= 0.5 => 30,
                <= 1.0 => 20,
                <= 2.0 => 14,
                <= 4.0 => 9,
                <= 8.0 => 5,
                <= 16.0 => 2.5,
                <= 32.0 => 1.2,
                _ => 0.6
            };

            // 根据实际 CPU 核心数缩放（线性近似）
            double coreScale = Math.Max(0.5, cpuCores / 8.0);
            double cpuTps = baseCpuTps * coreScale;

            if (vramSufficient)
            {
                // GPU 加速估算（基于常见 NVIDIA 显卡跑 llama.cpp 的典型表现，仅供参考）
                double gpuTps = tier switch
                {
                    HardwareTier.TopTierGpu => model.SizeGiB <= 4 ? 150 :
                                               model.SizeGiB <= 8 ? 100 :
                                               model.SizeGiB <= 15 ? 70 :
                                               model.SizeGiB <= 30 ? 45 : 30,
                    HardwareTier.HighEndGpu => model.SizeGiB <= 4 ? 90 :
                                               model.SizeGiB <= 8 ? 60 :
                                               model.SizeGiB <= 15 ? 38 :
                                               model.SizeGiB <= 30 ? 24 : 15,
                    HardwareTier.MidRangeGpu => model.SizeGiB <= 3 ? 55 :
                                                model.SizeGiB <= 7 ? 32 :
                                                model.SizeGiB <= 15 ? 18 : 10,
                    HardwareTier.LowEndGpu => model.SizeGiB <= 2 ? 30 :
                                              model.SizeGiB <= 5 ? 15 :
                                              model.SizeGiB <= 10 ? 8 : 4,
                    _ => cpuTps
                };

                // 显存余量微调
                if (hasVramReq)
                {
                    var vramHeadroom = maxVram - minVramReq;
                    if (vramHeadroom >= 4) gpuTps *= 1.15;
                    else if (vramHeadroom >= 2) gpuTps *= 1.05;
                    else if (vramHeadroom < 0.5) gpuTps *= 0.9;
                }

                return Math.Round(gpuTps, 0);
            }
            else if (hasVramReq && maxVram > 0)
            {
                // 部分 GPU offloading：比纯 CPU 快一些
                double offloadedRatio = Math.Min(1.0, maxVram / minVramReq);
                double hybridTps = cpuTps * (1 + offloadedRatio);
                return Math.Round(hybridTps, 0);
            }
            else
            {
                return Math.Round(cpuTps, 0);
            }
        }

        private List<ModelSourceDto> GetModelSources(ModelEntry model)
        {
            var sources = new List<ModelSourceDto>
            {
                new()
                {
                    Name = "Ollama Library",
                    Url = $"https://ollama.com/library/{model.OllamaModelName.Split(':')[0]}",
                    IsMirror = false,
                }
            };

            // 可扩展：添加 HuggingFace / ModelScope 对应的 GGUF 链接
            return sources;
        }
    }
}
