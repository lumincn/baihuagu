
using Microsoft.AspNetCore.Mvc;
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

        public SearchController(
            VaultSettingsService vaultSettings,
            EmbeddingService embeddingService,
            VaultNoteIndexer vaultNoteIndexer,
            ILogger<SearchController> logger)
        {
            _vaultSettings = vaultSettings;
            _embeddingService = embeddingService;
            _vaultNoteIndexer = vaultNoteIndexer;
            _logger = logger;
        }

}
