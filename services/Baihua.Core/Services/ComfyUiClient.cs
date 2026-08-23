using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Baihua.Core.Services;

/// <summary>
/// ComfyUI HTTP API 客户端：提交工作流、轮询执行结果、获取生成文件。
/// 供「AI 绘图」页调用（本地 ComfyUI 服务，默认 127.0.0.1:8188）。
/// </summary>
public class ComfyUiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ComfyUiClient> _logger;
    private readonly ComfyUiOptions _options;

    public ComfyUiClient(HttpClient http, ILogger<ComfyUiClient> logger, ComfyUiOptions? options = null)
    {
        _http = http;
        _logger = logger;
        _options = options ?? new ComfyUiOptions();
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    /// <summary>ComfyUI 是否在线（/system_stats 可达）</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("/system_stats", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ComfyUI unavailable: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>提交工作流，返回 prompt_id；失败抛异常。</summary>
    public async Task<string> SubmitAsync(Dictionary<string, object> workflow, CancellationToken ct = default)
    {
        var body = new { prompt = workflow };
        var resp = await _http.PostAsJsonAsync("/prompt", body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ComfyUI submit failed ({resp.StatusCode}): {Truncate(err, 500)}");
        }
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("prompt_id").GetString() ?? throw new InvalidOperationException("ComfyUI returned no prompt_id");
    }

    /// <summary>查询执行结果；未完成返回 null。完成时返回输出文件列表。</summary>
    public async Task<ComfyExecutionResult?> GetResultAsync(string promptId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/history/{promptId}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!json.TryGetProperty(promptId, out var entry)) return null;

        var status = entry.TryGetProperty("status", out var st) ? st : default;
        var completed = status.TryGetProperty("completed", out var c) && c.GetBoolean();
        var statusStr = status.TryGetProperty("status_str", out var ss) ? ss.GetString() : null;

        var files = new List<ComfyOutputFile>();
        if (entry.TryGetProperty("outputs", out var outputs))
        {
            foreach (var nodeOut in outputs.EnumerateObject())
            {
                var val = nodeOut.Value;
                if (val.TryGetProperty("images", out var images))
                    foreach (var img in images.EnumerateArray())
                        files.Add(new ComfyOutputFile(GetStr(img, "filename"), GetStr(img, "subfolder"), GetStr(img, "type")));
                if (val.TryGetProperty("videos", out var videos))
                    foreach (var vid in videos.EnumerateArray())
                        files.Add(new ComfyOutputFile(GetStr(vid, "filename"), GetStr(vid, "subfolder"), GetStr(vid, "type")));
                if (val.TryGetProperty("gifs", out var gifs))
                    foreach (var g in gifs.EnumerateArray())
                        files.Add(new ComfyOutputFile(GetStr(g, "filename"), GetStr(g, "subfolder"), GetStr(g, "type")));
            }
        }

        if (statusStr == "error")
        {
            var msg = status.TryGetProperty("messages", out var msgs)
                ? string.Join(" | ", msgs.EnumerateArray().Where(m => m.GetArrayLength() > 1 && m[0].GetString() == "execution_error").Select(m => Truncate(m[1].ToString(), 300)))
                : "unknown error";
            return new ComfyExecutionResult(promptId, true, true, msg, files);
        }
        return completed ? new ComfyExecutionResult(promptId, true, false, null, files) : null;
    }

    /// <summary>获取生成的文件（图片/视频），返回字节流。</summary>
    public async Task<byte[]> GetFileAsync(string filename, string subfolder = "", string type = "output", CancellationToken ct = default)
    {
        var url = $"/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
        return await _http.GetByteArrayAsync(url, ct);
    }

    /// <summary>获取 CheckpointLoaderSimple 的可用 checkpoint 列表（供绘图状态接口）。</summary>
    public async Task<List<string>> GetCheckpointsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/object_info/CheckpointLoaderSimple", ct);
            if (!resp.IsSuccessStatusCode) return new List<string>();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("CheckpointLoaderSimple", out var node) &&
                node.TryGetProperty("input", out var input) &&
                input.TryGetProperty("required", out var required) &&
                required.TryGetProperty("ckpt_name", out var ckpt) &&
                ckpt.ValueKind == JsonValueKind.Array && ckpt.GetArrayLength() > 0 &&
                ckpt[0].ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var name in ckpt[0].EnumerateArray())
                    if (name.ValueKind == JsonValueKind.String) list.Add(name.GetString() ?? "");
                return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("获取 ComfyUI checkpoint 列表失败: {Message}", ex.Message);
        }
        return new List<string>();
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

public class ComfyUiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";
    public int RequestTimeoutSeconds { get; set; } = 360;
}

public record ComfyOutputFile(string Filename, string Subfolder, string Type);

public record ComfyExecutionResult(
    string PromptId,
    bool Completed,
    bool IsError,
    string? Error,
    List<ComfyOutputFile> Files);
