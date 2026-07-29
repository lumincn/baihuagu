using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace TaskRunner.Vault.Controllers;
    [ApiController]
    [Route("api/[controller]")]
    public partial class SearchController : ControllerBase
    {
        private readonly Services.VaultSettingsService _vaultSettings;
        private readonly Services.EmbeddingService _embeddingService;
        private readonly Services.VaultNoteIndexer _vaultNoteIndexer;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            Services.VaultSettingsService vaultSettings,
            Services.EmbeddingService embeddingService,
            Services.VaultNoteIndexer vaultNoteIndexer,
            ILogger<SearchController> logger)
        {
            _vaultSettings = vaultSettings;
            _embeddingService = embeddingService;
            _vaultNoteIndexer = vaultNoteIndexer;
            _logger = logger;
        }

}
