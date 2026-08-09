using Baihua.Core;
using Baihua.Core.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Text.Json;
using Baihua.AI.Provider;
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
        private readonly IStringLocalizer<SharedResources> _loc;

        public AnkiController(
            Services.AnkiCardGenerator cardGenerator,
            Services.DailyCardService dailyCardService,
            Services.AchievementEngine achievementEngine,
            Services.LearnerService learnerService,
            Services.VaultSettingsService vaultSettings,
            Services.TaskManager taskManager,
            ILogger<AnkiController> logger,
            IStringLocalizer<SharedResources> loc)
        {
            _cardGenerator = cardGenerator;
            _dailyCardService = dailyCardService;
            _achievementEngine = achievementEngine;
            _learnerService = learnerService;
            _vaultSettings = vaultSettings;
            _taskManager = taskManager;
            _logger = logger;
            _loc = loc;
        }


}
