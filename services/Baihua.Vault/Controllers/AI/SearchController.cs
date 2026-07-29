
using Baihua.Core.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Diagnostics;

using Baihua.Family.Services;
namespace Baihua.Vault.Controllers;
    [ApiController]
    [Route("api/[controller]")]
    public partial class SearchController : ControllerBase
    {
        private readonly VaultSettingsService _vaultSettings;
        private readonly EmbeddingService _embeddingService;
        private readonly VaultNoteIndexer _vaultNoteIndexer;
        private readonly ILogger<SearchController> _logger;
        private readonly IStringLocalizer<SharedResources> _loc;

        public SearchController(
            VaultSettingsService vaultSettings,
            EmbeddingService embeddingService,
            VaultNoteIndexer vaultNoteIndexer,
            ILogger<SearchController> logger,
            IStringLocalizer<SharedResources> loc)
        {
            _vaultSettings = vaultSettings;
            _embeddingService = embeddingService;
            _vaultNoteIndexer = vaultNoteIndexer;
            _logger = logger;
            _loc = loc;
        }

}
