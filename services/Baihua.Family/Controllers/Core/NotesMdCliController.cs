using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Vaults;
using TaskRunner.Services;
using TaskRunner.Contracts.Core;

namespace TaskRunner.Controllers
{
    [ApiController]
    [Route("api/notesmd-cli")]
    public class NotesMdCliController : ControllerBase
    {
        private readonly NotesMdCliService _notesMdCliService;

        public NotesMdCliController(NotesMdCliService notesMdCliService)
        {
            _notesMdCliService = notesMdCliService;
        }

        /// <summary>
        /// 获取 notesmd-cli 状态及已注册的 vault 路径列表。
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var available = _notesMdCliService.IsAvailable();
            if (!available)
            {
                return Ok(new { available = false });
            }

            var registeredPaths = _notesMdCliService.GetRegisteredVaultPaths();
            return Ok(new { available = true, registeredPaths });
        }

        /// <summary>
        /// 添加单个 vault 到 notesmd-cli。
        /// </summary>
        [HttpPost("add-vault")]
        public IActionResult AddVault([FromBody] TaskRunner.Contracts.Vaults.AddVaultRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Path))
            {
                return BadRequest(new { success = false, error = "路径不能为空" });
            }

            var path = request.Path.Trim();
            if (!Directory.Exists(path))
            {
                return BadRequest(new { success = false, error = "目录不存在" });
            }

            var success = _notesMdCliService.AddVault(path);
            if (success)
            {
                return Ok(new { success = true, path });
            }

            return StatusCode(500, new { success = false, error = "添加失败，请检查 notesmd-cli 是否正常工作" });
        }

        /// <summary>
        /// 批量添加 vaults 到 notesmd-cli。
        /// </summary>
        [HttpPost("batch-add")]
        public IActionResult BatchAdd([FromBody] NotesMdBatchAddRequest request)
        {
            if (request?.Paths == null || request.Paths.Count == 0)
            {
                return BadRequest(new { success = false, error = "路径列表不能为空" });
            }

            var validPaths = request.Paths
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p.Trim()))
                .Select(p => p.Trim())
                .ToList();

            if (validPaths.Count == 0)
            {
                return BadRequest(new { success = false, error = "没有有效的目录路径" });
            }

            var (succeeded, failed) = _notesMdCliService.BatchAddVaults(validPaths);
            return Ok(new
            {
                success = failed.Count == 0,
                succeededCount = succeeded.Count,
                failedCount = failed.Count,
                succeeded,
                failed
            });
        }
    }
}
