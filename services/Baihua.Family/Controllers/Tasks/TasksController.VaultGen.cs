using Baihua.Core;
using Baihua.Family.Services;
using System.Text.Json;
using Baihua.Family.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Baihua.Family.Models;
using Baihua.Contracts.Scene;
using Baihua.Contracts.Tasks;
using Baihua.Contracts.Vaults;

namespace Baihua.Family.Controllers
{
    public partial class TasksController : ControllerBase
    {
        [HttpPost("vault-generation")]
        public async Task<ActionResult<VaultGenerationResponse>> CreateVaultGenerationTask([FromBody] VaultGenerationRequest request)
            => await HandleCreateVaultGenerationTaskAsync(request);
    }
}
