using Baihua.Contracts.LocalModels;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers;

public partial class LocalModelDeploymentController
{
    /// <summary>OpenVINO 可下载模型目录（含已下载状态；未下载条目按当前显卡显存过滤）</summary>
    [HttpGet("openvino/catalog")]
    public ActionResult<List<OpenVinoCatalogItemDto>> GetOpenVinoCatalog()
    {
        var installed = _openVinoRuntime.GetInstalledModels()
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        var ttsPort = IsPortListening(8001) ? 8001 : (int?)null;
        var ttsModelDir = Path.Combine(_openVinoRuntime.ModelRoot, "Kokoro-82M-int8-ov", "1");
        var ttsInstalled = System.IO.File.Exists(Path.Combine(ttsModelDir, "openvino_model.bin"));

        // 当前机器 GPU 可用显存（GiB）；探测失败则不过滤（显示全部）
        double? availableVramGiB = null;
        try
        {
            var hw = _hardwareInfoService.GetHardwareInfo();
            if (hw.Gpus is { Count: > 0 })
                availableVramGiB = hw.Gpus
                    .Where(g => g.VramGiB.HasValue && g.VramGiB > 0)
                    .Select(g => g.VramGiB!.Value)
                    .DefaultIfEmpty(0)
                    .Max();
        }
        catch { /* 硬件探测失败 → 不过滤 */ }

        var catalog = OpenVinoCatalog.All
            .Where(e => installed.ContainsKey(DefaultDirName(e))        // 已下载/已注册 → 始终显示
                        || ttsInstalled
                        || e.MinVramGiB == null                          // 不依赖显存 → 始终显示
                        || availableVramGiB == null                      // 显存未知 → 不误伤
                        || e.MinVramGiB <= availableVramGiB)             // 未下载且显存足够 → 显示
            .Select(e =>
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
                IsTts = e.IsTts,
                IsMedical = e.IsMedical,
                ModelScopeRepo = e.ModelScopeRepo,
                Format = e.Format,
                MinVramGiB = e.MinVramGiB,
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
                dto.IsOmsHosted = m.IsOmsHosted;
            }

            // TTS 模型由独立 Python 服务承载，模型在 <root>/Kokoro-82M-int8-ov/1/ 下
            if (e.IsTts && ttsInstalled)
            {
                dto.Installed = true;
                dto.Path = ttsModelDir;
                dto.IsRunning = ttsPort != null;
                dto.Port = ttsPort;
            }
            return dto;
        })
        // 已下载/运行中的优先，其余按所需显存升序
        .OrderByDescending(d => d.Installed)
        .ThenBy(d => d.MinVramGiB ?? 0)
        .ToList();

        return Ok(catalog);
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            tcp.Connect("127.0.0.1", port);
            return true;
        }
        catch { return false; }
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
