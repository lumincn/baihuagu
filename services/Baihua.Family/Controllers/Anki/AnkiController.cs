using Baihua.Core;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Baihua.Family.Helpers;
using Baihua.Contracts.Anki;

namespace Baihua.Family.Controllers;
    /// <summary>
    /// Anki 卡片生成控制器
    /// </summary>
    [ApiController]
    [Route("api/anki")]
    public partial class AnkiController : ControllerBase
    {
        private readonly Services.AnkiCardGenerator _cardGenerator;
        private readonly Services.DailyCardService _dailyCardService;
        private readonly Services.AchievementEngine _achievementEngine;
        private readonly Services.LearnerService _learnerService;
        private readonly Services.VaultSettingsService _vaultSettings;
        private readonly Services.TaskManager _taskManager;
        private readonly ILogger<AnkiController> _logger;

        public AnkiController(
            Services.AnkiCardGenerator cardGenerator,
            Services.DailyCardService dailyCardService,
            Services.AchievementEngine achievementEngine,
            Services.LearnerService learnerService,
            Services.VaultSettingsService vaultSettings,
            Services.TaskManager taskManager,
            ILogger<AnkiController> logger)
        {
            _cardGenerator = cardGenerator;
            _dailyCardService = dailyCardService;
            _achievementEngine = achievementEngine;
            _learnerService = learnerService;
            _vaultSettings = vaultSettings;
            _taskManager = taskManager;
            _logger = logger;
        }


}
