using TaskRunner.Core.Shared;
using TaskRunner.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Models;
using TaskRunner.Contracts.Ai;
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace TaskRunner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB 限制，防止 DoS
    public partial class AIController : ControllerBase
    {
        private readonly Services.AiSettingsService _aiSettings;
        private readonly Services.VaultSettingsService _vaultSettings;
        private readonly Services.AiClientService _aiClientService;
        private readonly Services.LocalModelDeploymentService _localDeployment;
        private readonly Services.RagService _ragService;
        private readonly DefaultPromptProvider _scenePromptService;
        private readonly Services.AiFunctionService _aiFunctionService;
        private readonly Services.ChatMemoryService _chatMemoryService;
        private readonly Services.AnkiCardGenerator _cardGenerator;
        private readonly TaskManager _taskManager;
        private readonly ILogger<AIController> _logger;

        public AIController(
            Services.AiSettingsService aiSettings,
            Services.VaultSettingsService vaultSettings,
            Services.AiClientService aiClientService,
            Services.LocalModelDeploymentService localDeployment,
            Services.RagService ragService,
            DefaultPromptProvider scenePromptService,
            Services.AiFunctionService aiFunctionService,
            Services.ChatMemoryService chatMemoryService,
            Services.AnkiCardGenerator cardGenerator,
            TaskManager taskManager,
            ILogger<AIController> logger)
        {
            _aiSettings = aiSettings;
            _vaultSettings = vaultSettings;
            _aiClientService = aiClientService;
            _localDeployment = localDeployment;
            _scenePromptService = scenePromptService;
            _ragService = ragService;
            _aiFunctionService = aiFunctionService;
            _chatMemoryService = chatMemoryService;
            _cardGenerator = cardGenerator;
            _taskManager = taskManager;
            _logger = logger;
        }

        /// <summary>
        /// 列出已配置的 AI 提供方（不含密钥），包含模型列表，供前端多选拆分等使用。
        /// </summary>
        [HttpGet("providers")]
        public ActionResult<List<AiProviderPublicDto>> GetProviders()
        {
            var list = _aiSettings.GetAiProviders()
                .Select(p => new AiProviderPublicDto
                {
                    Id = p.Id,
                    Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                    IsMain = p.IsMain,
                    Models = p.GetModelOptions().Select(m => new AiModelPublicDto
                    {
                        Name = m.Name,
                        IsPaid = m.IsPaid,
                        IsMain = m.IsMain
                    }).ToList()
                })
                .ToList();
            return Ok(list);
        }
    }
}
