using Baihua.Contracts.LocalModels;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers;

public partial class LocalModelDeploymentController
{
    /// <summary>OpenVINO 可下载模型目录（含已下载状态）</summary>
    [HttpGet("openvino/catalog")]
    public ActionResult<List<OpenVinoCatalogItemDto>> GetOpenVinoCatalog()
    {
        var installed = _openVinoRuntime.GetInstalledModels()
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        var catalog = OpenVinoCatalog.All.Select(e =>
        {
            var dto = new OpenVinoCatalogItemDto
            {
                Id = e.Id,
                Name = e.Name,
                ParameterSize = e.ParameterSize,
                Quantization = e.Quantization,
                SizeGiB = e.SizeGiB,
                Description = e.Description,
                IsVision = e.IsVision,
                ModelScopeRepo = e.ModelScopeRepo,
            };

            // 合并已下载信息：路径/实际大小/运行状态/端口
            if (installed.TryGetValue(DefaultDirName(e), out var m))
            {
                dto.Installed = true;
                dto.Path = m.Path;
                dto.SizeBytes = m.SizeBytes;
                dto.IsRunning = m.IsRunning;
                dto.Port = m.Port;
                dto.LastModified = m.LastModified;
            }
            return dto;
        }).ToList();

        return Ok(catalog);
    }

    private static string DefaultDirName(OpenVinoCatalogEntry e) =>
        string.IsNullOrWhiteSpace(e.DirectoryName) ? e.ModelScopeRepo.Split('/').Last() : e.DirectoryName;

    /// <summary>已下载的 OpenVINO 模型（含运行状态）</summary>
    [HttpGet("openvino/installed")]
    public ActionResult<List<OpenVinoInstalledModelDto>> GetOpenVinoInstalled()
    {
        return Ok(_openVinoRuntime.GetInstalledModels());
    }

    /// <summary>下载任务列表</summary>
    [HttpGet("openvino/downloads")]
    public ActionResult<List<OpenVinoDownloadTaskDto>> GetOpenVinoDownloads()
    {
        return Ok(_downloadService.GetTasks());
    }

    /// <summary>启动下载（后台任务）</summary>
    [HttpPost("openvino/download")]
    public ActionResult<OpenVinoDownloadTaskDto> StartOpenVinoDownload([FromBody] OpenVinoDownloadRequest request)
    {
        try
        {
            return Ok(_downloadService.StartDownload(request.ModelId));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>下载任务进度</summary>
    [HttpGet("openvino/download/{taskId}")]
    public ActionResult<OpenVinoDownloadTaskDto> GetOpenVinoDownload(string taskId)
    {
        var task = _downloadService.GetTask(taskId);
        return task == null ? NotFound() : Ok(task);
    }

    /// <summary>取消下载</summary>
    [HttpPost("openvino/download/{taskId}/cancel")]
    public IActionResult CancelOpenVinoDownload(string taskId)
    {
        _downloadService.CancelDownload(taskId);
        return Ok();
    }

    /// <summary>运行已下载模型（GPU 推理）</summary>
    [HttpPost("openvino/run")]
    public async Task<ActionResult<OpenVinoRunResult>> RunOpenVinoModel([FromBody] OpenVinoRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            return BadRequest(new { error = "modelPath 不能为空" });
        var result = await _openVinoRuntime.StartAsync(request.ModelPath, request.Device, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>停止运行中的模型</summary>
    [HttpPost("openvino/stop")]
    public async Task<IActionResult> StopOpenVinoModel([FromBody] OpenVinoRunRequest request, CancellationToken ct)
    {
        var port = request.Port ?? 0;
        if (port <= 0)
            return BadRequest(new { error = "port 不能为空" });
        var ok = await _openVinoRuntime.StopAsync(port, ct);
        return ok ? Ok() : NotFound();
    }

    /// <summary>删除已下载模型目录</summary>
    [HttpDelete("openvino/model")]
    public IActionResult DeleteOpenVinoModel([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "path 不能为空" });
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(_openVinoRuntime.ModelRoot);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "路径必须在模型目录内" });
            if (!Directory.Exists(full)) return NotFound();
            Directory.Delete(full, recursive: true);
            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
