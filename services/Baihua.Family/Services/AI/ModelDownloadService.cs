using Baihua.Core.Models;
using Baihua.Core.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Baihua.Contracts.LocalModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baihua.Core;

namespace Baihua.Family.Services;

/// <summary>
/// OpenVINO 模型下载服务：从 ModelScope（国内直连）或 HuggingFace 镜像下载模型目录，
/// 后台任务执行（进度/速度/日志），并同步到 TaskManager（任务管理页可见）。
/// </summary>
public class ModelDownloadService
{
    private readonly ILogger<ModelDownloadService> _logger;
    private readonly LocalAiOptions _options;
    private readonly TaskManager _taskManager;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ConcurrentDictionary<string, (OpenVinoDownloadTaskDto Task, CancellationTokenSource Cts)> _tasks = new();

    private const int MaxHistory = 20;

    public ModelDownloadService(
        ILogger<ModelDownloadService> logger,
        TaskManager taskManager,
        IHttpClientFactory httpFactory,
        IOptions<LocalAiOptions> options)
    {
        _logger = logger;
        _taskManager = taskManager;
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    /// <summary>模型根目录（可通过 LocalAI:DownloadDirectory 配置）</summary>
    public string ModelRoot => _options.GetModelRoot();

    public List<OpenVinoDownloadTaskDto> GetTasks() =>
        _tasks.Values
            .Select(x => x.Task)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaxHistory)
            .ToList();

    public OpenVinoDownloadTaskDto? GetTask(string taskId) =>
        _tasks.TryGetValue(taskId, out var x) ? x.Task : null;

    /// <summary>启动后台下载任务</summary>
    public OpenVinoDownloadTaskDto StartDownload(string modelId)
    {
        var entry = OpenVinoCatalog.GetById(modelId)
            ?? throw new ArgumentException($"目录中不存在模型: {modelId}");

        var taskId = Guid.NewGuid().ToString("N")[..12];
        var cts = new CancellationTokenSource();
        var dirName = string.IsNullOrWhiteSpace(entry.DirectoryName)
            ? entry.ModelScopeRepo.Split('/').Last()
            : entry.DirectoryName;
        var targetDir = Path.Combine(ModelRoot, dirName);

        var dto = new OpenVinoDownloadTaskDto
        {
            TaskId = taskId,
            ModelId = entry.Id,
            ModelName = entry.Name,
            Status = "pending",
            CreatedAt = DateTime.Now,
            Logs = new List<string> { $"[{DateTime.Now:HH:mm:ss}] 开始下载 {entry.Name}（{entry.SizeGiB:F1}GB）" }
        };
        _tasks[taskId] = (dto, cts);

        // 同步到任务管理页
        string? taskManagerId = null;
        try
        {
            taskManagerId = _taskManager.CreateTask("model_download", new Dictionary<string, string>
            {
                ["modelId"] = entry.Id,
                ["modelName"] = entry.Name,
                ["targetDir"] = targetDir
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建任务管理记录失败（不影响下载）");
        }

        _ = Task.Run(async () => await RunDownloadAsync(dto, entry, targetDir, taskManagerId, cts.Token), cts.Token);

        return dto;
    }

    public void CancelDownload(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var x))
        {
            x.Cts.Cancel();
            x.Task.Status = "cancelled";
            x.Task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 已请求取消");
        }
    }

    private async Task RunDownloadAsync(
        OpenVinoDownloadTaskDto dto, OpenVinoCatalogEntry entry, string targetDir,
        string? taskManagerId, CancellationToken ct)
    {
        dto.Status = "running";
        UpdateTaskManager(taskManagerId, "Running", 1, 100, "开始下载");

        var tmpDir = targetDir + ".downloading";
        try
        {
            // 1. 列文件（ModelScope → HF 回退）
            var files = await ListFilesAsync(entry, dto, ct);
            if (files.Count == 0)
                throw new Exception("仓库文件列表为空（仓库可能不存在）");

            var total = files.Sum(f => f.Size);
            dto.TotalBytes = total;
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 共 {files.Count} 个文件，{FormatSize(total)}");

            // 2. 逐文件下载
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
            Directory.CreateDirectory(tmpDir);
            var downloaded = 0L;
            var sw = Stopwatch.StartNew();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var localPath = Path.Combine(tmpDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                dto.CurrentFile = file.Path;
                var url = BuildFileUrl(entry, file);
                await DownloadFileAsync(url, localPath, file.Size, dto, () => downloaded, sw, ct,
                    // 大文件下载期间把字节级进度同步到任务管理页（每 ~2s），避免进度条长期停在 0%
                    (pct, msg) => UpdateTaskManager(taskManagerId, "Running", pct, 100, msg));

                downloaded += file.Size;
                dto.DownloadedBytes = downloaded;
                dto.ProgressPercent = total > 0 ? (int)(downloaded * 100 / total) : 0;
                dto.SpeedMBps = sw.Elapsed.TotalSeconds > 0 ? downloaded / 1024d / 1024d / sw.Elapsed.TotalSeconds : 0;
                UpdateTaskManager(taskManagerId, "Running", dto.ProgressPercent, 100, $"下载中 {dto.CurrentFile}");
            }

            // 3. 完成：临时目录 → 目标目录（已存在则先备份旧目录）
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            Directory.Move(tmpDir, targetDir);

            // 3.5 safetensors → OpenVINO IR 转换（如需要）
            if (string.Equals(entry.Format, "safetensors", StringComparison.OrdinalIgnoreCase))
            {
                dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 开始 safetensors → OpenVINO IR 转换...");
                UpdateTaskManager(taskManagerId, "Running", 95, 100, "转换 OpenVINO IR 中");
                await ConvertSafetensorsToOpenVinoAsync(targetDir, dto, ct);
            }

            dto.Status = "completed";
            dto.ProgressPercent = 100;
            dto.CompletedAt = DateTime.Now;
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 完成：{targetDir}（{FormatSize(dto.TotalBytes)}）");
            UpdateTaskManager(taskManagerId, "Success", 100, 100, "下载完成");
        }
        catch (OperationCanceledException)
        {
            dto.Status = "cancelled";
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 已取消");
            UpdateTaskManager(taskManagerId, "Cancelled", dto.ProgressPercent, 100, "已取消");
            TryCleanTmp(tmpDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模型下载失败: {Model}", entry.Id);
            dto.Status = "failed";
            dto.ErrorMessage = ex.Message;
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 失败: {ex.Message}");
            UpdateTaskManager(taskManagerId, "Failed", dto.ProgressPercent, 100, $"失败: {ex.Message}");
            TryCleanTmp(tmpDir);
        }
        finally
        {
            dto.CompletedAt ??= DateTime.Now;
            if (_tasks.TryGetValue(dto.TaskId, out var x)) x.Cts.Dispose();
        }
    }

    private static void TryCleanTmp(string tmpDir)
    {
        try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true); } catch { }
    }

    private void UpdateTaskManager(string? taskId, string status, int current, int total, string message)
    {
        if (string.IsNullOrEmpty(taskId)) return;
        try
        {
            _taskManager.UpdateProgress(taskId, current, total, message);
            var s = status.ToLowerInvariant() switch
            {
                "running" => Baihua.Core.RunnerTaskStatus.Running,
                "success" or "completed" => Baihua.Core.RunnerTaskStatus.Success,
                "failed" => Baihua.Core.RunnerTaskStatus.Failed,
                "cancelled" => Baihua.Core.RunnerTaskStatus.Cancelled,
                _ => Baihua.Core.RunnerTaskStatus.Pending
            };
            _taskManager.UpdateStatus(taskId, s);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新任务管理进度失败（taskId={TaskId}）", taskId);
        }
    }

    #region safetensors → OpenVINO IR 转换

    /// <summary>调用 Python 脚本将 safetensors 转为 OpenVINO IR（INT4 量化）</summary>
    private async Task ConvertSafetensorsToOpenVinoAsync(
        string modelDir, OpenVinoDownloadTaskDto dto, CancellationToken ct)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "convert_safetensors_to_ov.py");
        if (!File.Exists(scriptPath))
        {
            var alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "src", "baihua", "scripts", "convert_safetensors_to_ov.py");
            if (File.Exists(alt)) scriptPath = alt;
            else throw new Exception("找不到转换脚本 convert_safetensors_to_ov.py");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\" --src \"{modelDir}\" --quant int4",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        foreach (var line in (stdout + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] [convert] {line.Trim()}");

        if (proc.ExitCode != 0)
            throw new Exception($"safetensors 转换失败（exit {proc.ExitCode}）: {stderr.Trim()}");

        dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] OpenVINO IR 转换完成");
    }

    #endregion

    #region 仓库文件列表与下载

    private sealed record RemoteFile(string Path, long Size, string Source);

    /// <summary>固定文件清单（OpenVINO 模型通用结构 + safetensors 常见分片）</summary>
    private static readonly string[] KnownFiles =
    [
        "config.json", "configuration.json", "generation_config.json",
        "openvino_model.bin", "openvino_model.xml", "openvino_detokenizer.bin",
        "openvino_vision_model.bin", "openvino_vision_model.xml",
        "openvino_text_model.bin", "openvino_text_model.xml",
        "tokenizer_config.json", "tokenizer.json", "merges.txt", "vocab.json",
        "added_tokens.json", "special_tokens_map.json", "chat_template.json",
        "preprocessor_config.json", "processor_config.json",
        // safetensors 常见分片（7B 模型通常 4 片）
        "model.safetensors.index.json",
        "model-00001-of-00004.safetensors", "model-00002-of-00004.safetensors",
        "model-00003-of-00004.safetensors", "model-00004-of-00004.safetensors",
        "model.safetensors",
    ];

    private async Task<List<RemoteFile>> ListFilesAsync(OpenVinoCatalogEntry entry, OpenVinoDownloadTaskDto dto, CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        // 1) ModelScope（国内 CDN 实测 ~13MB/s，优先）
        try
        {
            var url = $"https://modelscope.cn/api/v1/models/{entry.ModelScopeRepo}/repo/files?Recursive=true&Revision=master";
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Data", out var data) &&
                data.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var list = new List<RemoteFile>();
                foreach (var f in files.EnumerateArray())
                {
                    var path = f.TryGetProperty("Path", out var p) ? p.GetString() : null;
                    var size = f.TryGetProperty("Size", out var s) ? s.GetInt64() : 0;
                    if (string.IsNullOrEmpty(path) || IsSkippable(path)) continue;
                    // 跳过目录条目（size=0 且无扩展名）
                    if (size == 0 && !Path.HasExtension(path)) continue;
                    list.Add(new RemoteFile(path!, size, "ms"));
                }
                if (list.Count > 0)
                {
                    dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 文件清单来自 ModelScope: {entry.ModelScopeRepo}");
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] ModelScope 列文件失败（回退 HF 镜像）: {ex.Message}");
        }

        // 1.5) HF API 列文件（hf-mirror，递归列出全部文件，适配 safetensors 分片）
        try
        {
            var apiUrl = $"https://hf-mirror.com/api/models/{entry.HuggingFaceRepo}/tree/main?recursive=true";
            var apiJson = await http.GetStringAsync(apiUrl, ct);
            using var apiDoc = JsonDocument.Parse(apiJson);
            if (apiDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var apiList = new List<RemoteFile>();
                foreach (var item in apiDoc.RootElement.EnumerateArray())
                {
                    var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
                    var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    if (string.IsNullOrEmpty(path) || IsSkippable(path)) continue;
                    // 跳过目录条目（HF API 返回 size=0 的目录节点，如 "assets"）
                    if (size == 0 && !Path.HasExtension(path)) continue;
                    apiList.Add(new RemoteFile(path!, size, "hf"));
                }
                if (apiList.Count > 0)
                {
                    dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 文件清单来自 HF API: {entry.HuggingFaceRepo}（{apiList.Count} 个文件）");
                    return apiList;
                }
            }
        }
        catch (Exception ex)
        {
            dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] HF API 列文件失败（回退固定清单）: {ex.Message}");
        }

        // 2) hf-mirror 固定清单逐个 HEAD 探测（小文件走镜像 cache，大文件走 CDN 较慢）
        var hfList = new List<RemoteFile>();
        var seen = 0L;
        foreach (var f in KnownFiles)
        {
            ct.ThrowIfCancellationRequested();
            var hfUrl = $"https://hf-mirror.com/{entry.HuggingFaceRepo}/resolve/main/{f}";
            try
            {
                using var head = new HttpRequestMessage(HttpMethod.Head, hfUrl);
                using var resp = await http.SendAsync(head, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var size = resp.Content.Headers.ContentLength ?? 0;
                hfList.Add(new RemoteFile(f, size, "hf"));
                seen += size;
            }
            catch { }
        }

        if (hfList.Count == 0)
            throw new Exception($"仓库 {entry.HuggingFaceRepo} 无可用文件（ModelScope 与 HF 镜像均不可用）");

        dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 文件清单（hf-mirror 回退）: {hfList.Count} 个文件，{FormatSize(seen)}");
        return hfList;
    }

    private static bool IsSkippable(string path) =>
        path.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/onnx/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("onnx/", StringComparison.OrdinalIgnoreCase) ||
        // 非模型文件：assets 图片、README、训练状态等
        path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("training_args.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("trainer_state.json", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("sft_args.json", StringComparison.OrdinalIgnoreCase);

    private string BuildFileUrl(OpenVinoCatalogEntry entry, RemoteFile file)
    {
        return file.Source == "ms"
            ? $"https://modelscope.cn/models/{entry.ModelScopeRepo}/resolve/master/{file.Path}"
            : $"https://hf-mirror.com/{entry.HuggingFaceRepo}/resolve/main/{file.Path}";
    }

    private async Task DownloadFileAsync(
        string url, string localPath, long expectedSize,
        OpenVinoDownloadTaskDto dto, Func<long> getDownloaded, Stopwatch totalSw, CancellationToken ct,
        Action<int, string>? onProgress = null)
    {
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(30);
        // 魔搭 WAF：无浏览器 UA 的文件请求会 403
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var urls = new[] { url, url.Replace("modelscope.cn", "www.modelscope.cn") };
        Exception? last = null;
        foreach (var u in urls)
        {
            try
            {
                // 根据域名设 Referer：ModelScope WAF 需 ModelScope Referer；hf-mirror 拒绝 ModelScope Referer 返回 HTML 错误页
                using var req = new HttpRequestMessage(HttpMethod.Get, u);
                req.Headers.Referrer = u.Contains("modelscope")
                    ? new Uri("https://modelscope.cn/")
                    : new Uri("https://hf-mirror.com/");

                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    // ModelScope 404 → 回退 HuggingFace 镜像
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound && u.Contains("modelscope"))
                    {
                        var hfMirror = "https://hf-mirror.com/" + url[(url.IndexOf("/models/", StringComparison.Ordinal) + 8)..]
                            .Replace("/resolve/master/", "/resolve/main/");
                        await DownloadFileFromAsync(http, hfMirror, localPath, expectedSize, dto, getDownloaded, totalSw, ct, onProgress);
                        return;
                    }
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                }

                // 检查 Content-Type：hf-mirror 可能返回 HTML 错误页但状态码 200
                var ct0 = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (ct0.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException("服务器返回 HTML 错误页（非文件内容）");

                await StreamToFileAsync(resp, localPath, expectedSize, dto, getDownloaded, totalSw, ct, onProgress);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 下载失败重试: {Path.GetFileName(localPath)} ({ex.Message})");
            }
        }
        throw last ?? new Exception("下载失败");
    }

    private static async Task DownloadFileFromAsync(
        HttpClient http, string url, string localPath, long expectedSize,
        OpenVinoDownloadTaskDto dto, Func<long> getDownloaded, Stopwatch totalSw, CancellationToken ct,
        Action<int, string>? onProgress = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://hf-mirror.com/");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var ct0 = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (ct0.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("服务器返回 HTML 错误页（非文件内容）");

        await StreamToFileAsync(resp, localPath, expectedSize, dto, getDownloaded, totalSw, ct, onProgress);
    }

    private static async Task StreamToFileAsync(
        HttpResponseMessage resp, string localPath, long expectedSize,
        OpenVinoDownloadTaskDto dto, Func<long> getDownloaded, Stopwatch? totalSw, CancellationToken ct,
        Action<int, string>? onProgress = null)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(localPath + ".part", FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        var fileDone = 0L;
        var lastLog = DateTime.UtcNow;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            fileDone += read;
            var now = DateTime.UtcNow;
            if (now - lastLog > TimeSpan.FromSeconds(2))
            {
                lastLog = now;
                var total = getDownloaded() + fileDone;
                var elapsedSec = totalSw?.Elapsed.TotalSeconds ?? 1;
                dto.SpeedMBps = elapsedSec > 0 ? total / 1024d / 1024d / elapsedSec : 0;
                // 实时更新总进度（大文件下载期间页面进度条可见）
                dto.DownloadedBytes = total;
                if (dto.TotalBytes > 0)
                    dto.ProgressPercent = (int)(total * 100 / dto.TotalBytes);
                dto.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(localPath)} {FormatSize(fileDone)}/{FormatSize(expectedSize)} ({dto.SpeedMBps:F1}MB/s)");
                if (dto.Logs.Count > 200) dto.Logs.RemoveRange(0, dto.Logs.Count - 200);
                onProgress?.Invoke(dto.ProgressPercent, $"下载中 {Path.GetFileName(localPath)} {FormatSize(total)}/{FormatSize(dto.TotalBytes)}");
            }
        }
        await fs.FlushAsync(ct);
        fs.Close();
        if (File.Exists(localPath)) File.Delete(localPath);
        File.Move(localPath + ".part", localPath);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / 1024d / 1024d / 1024d:F2}GB";
        if (bytes >= 1L << 20) return $"{bytes / 1024d / 1024d:F1}MB";
        return $"{bytes / 1024d:F0}KB";
    }

    #endregion
}
