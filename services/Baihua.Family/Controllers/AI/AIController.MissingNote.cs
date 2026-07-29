using TaskRunner.Core.Shared;
using TaskRunner.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Contracts.Ai;
using TaskRunner.Models;

namespace TaskRunner.Controllers
{
    public partial class AIController
    {
        [HttpPost("generate-missing-note")]
        public async Task<ActionResult<GenerateMissingNoteResponse>> GenerateMissingNote([FromBody] GenerateMissingNoteRequest request)
            => await HandleGenerateMissingNoteAsync(request);
    }
}
