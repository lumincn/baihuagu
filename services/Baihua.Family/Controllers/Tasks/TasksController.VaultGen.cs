using TaskRunner.Core.Shared;
using TaskRunner.Services;
using System.Text.Json;
using TaskRunner.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Models;
using TaskRunner.Contracts.Scene;
using TaskRunner.Contracts.Tasks;
using TaskRunner.Contracts.Vaults;

namespace TaskRunner.Controllers
{
    public partial class TasksController : ControllerBase
    {
        [HttpPost("vault-generation")]
        public async Task<ActionResult<VaultGenerationResponse>> CreateVaultGenerationTask([FromBody] VaultGenerationRequest request)
            => await HandleCreateVaultGenerationTaskAsync(request);
    }
}
