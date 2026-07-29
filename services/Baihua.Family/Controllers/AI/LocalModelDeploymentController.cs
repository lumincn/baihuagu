using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Services;

namespace TaskRunner.Controllers;
    /// <summary>
    /// 本地模型部署 API：硬件检测、模型推荐、部署管理
    /// </summary>
    [ApiController]
    [Route("api/local-models")]
    public partial class LocalModelDeploymentController : ControllerBase
    {
        private readonly HardwareInfoService _hardwareInfoService;
        private readonly ModelRecommendationEngine _recommendationEngine;
        private readonly LocalModelDeploymentService _deploymentService;
        private readonly LocalModelSettingsService _localModelSettings;
        private readonly OllamaLibraryClient? _ollamaLibrary;
        private readonly ILogger<LocalModelDeploymentController> _logger;

        public LocalModelDeploymentController(
            HardwareInfoService hardwareInfoService,
            ModelRecommendationEngine recommendationEngine,
            LocalModelDeploymentService deploymentService,
            LocalModelSettingsService localModelSettings,
            OllamaLibraryClient? ollamaLibrary,
            ILogger<LocalModelDeploymentController> logger)
        {
            _hardwareInfoService = hardwareInfoService;
            _recommendationEngine = recommendationEngine;
            _deploymentService = deploymentService;
            _localModelSettings = localModelSettings;
            _ollamaLibrary = ollamaLibrary;
            _logger = logger;
        }

        /// <summary>
        /// 手动刷新 Ollama Library 模型列表
        /// </summary>
        [HttpPost("refresh-library")]
        public async Task<ActionResult> RefreshLibrary()
        {
            if (_ollamaLibrary == null)
                return BadRequest(new { error = "Ollama Library 客户端未启用" });

            try
            {
                await _ollamaLibrary.RefreshAsync(HttpContext.RequestAborted);
                return Ok(new { success = true, message = "模型库已刷新" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新 Ollama Library 失败");
                return StatusCode(500, new { error = "刷新失败", message = ex.Message });
            }
        }

        /// <summary>
        /// 获取当前硬件信息
        /// </summary>
        [HttpGet("hardware")]
        public ActionResult<HardwareInfoDto> GetHardware([FromQuery] bool forceRefresh = false)
        {
            try
            {
                var info = forceRefresh
                    ? _hardwareInfoService.RefreshHardwareInfo()
                    : _hardwareInfoService.GetHardwareInfo();
                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取硬件信息失败");
                return StatusCode(500, new { error = "获取硬件信息失败", message = ex.Message });
            }
        }

        /// <summary>
        /// 获取基于当前硬件的推荐模型（包含已下载状态）
        /// </summary>
        [HttpGet("recommend")]
        public async Task<ActionResult<List<RecommendedModelDto>>> GetRecommendations([FromQuery] string? scenario = null, [FromQuery] bool forceRefresh = false)
        {
            try
            {
                var hardware = _hardwareInfoService.GetHardwareInfo();
                var recommendations = forceRefresh
                    ? _recommendationEngine.RefreshRecommendations(hardware, scenario)
                    : _recommendationEngine.GetRecommendations(hardware, scenario);

                // 查询已下载模型列表，填充下载状态
                var ollamaModels = await _deploymentService.GetAvailableModelsAsync("ollama");
                var lmStudioModels = await _deploymentService.GetAvailableModelsAsync("lmstudio");
                var llamaCppModels = await _deploymentService.GetAvailableModelsAsync("llamacpp");

                foreach (var r in recommendations)
                {
                    r.IsDownloadedOllama = ollamaModels.Any(m =>
                        m.Equals(r.OllamaModelName, StringComparison.OrdinalIgnoreCase) ||
                        m.Split(':')[0].Equals(r.OllamaModelName.Split(':')[0], StringComparison.OrdinalIgnoreCase));

                    r.IsDownloadedLmStudio = !string.IsNullOrEmpty(r.LmStudioSearchName) && lmStudioModels.Any(m =>
                        m.Contains(r.LmStudioSearchName, StringComparison.OrdinalIgnoreCase) ||
                        r.LmStudioSearchName.Contains(m, StringComparison.OrdinalIgnoreCase));

                    r.IsDownloadedLlamaCpp = llamaCppModels.Any(m =>
                        !string.IsNullOrEmpty(r.LmStudioSearchName) &&
                        (m.Contains(r.LmStudioSearchName, StringComparison.OrdinalIgnoreCase) ||
                         r.LmStudioSearchName.Contains(m, StringComparison.OrdinalIgnoreCase)));
                }

                return Ok(recommendations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模型推荐失败");
                return StatusCode(500, new { error = "获取模型推荐失败", message = ex.Message });
            }
        }
}
