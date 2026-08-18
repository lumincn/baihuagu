using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Baihua.Core.Localization;

namespace Baihua.Family.Controllers;

/// <summary>
/// OpenVINO LLM 服务托管端点：转发到宿主机 openvino_host.py（8866）
/// 说明：k8s/compose 容器内无法直接启动宿主机 Python 进程，因此由宿主机托管
/// 服务统一管理 openvino_llm_server.py 实例（8000 对话 / 8001 代码），本控制器只做转发。
/// </summary>
public partial class LocalModelDeploymentController
{
    private string OpenVinoHostUrl =>
        Environment.GetEnvironmentVariable("OPENVINO_HOST_URL")
        ?? Environment.GetEnvironmentVariable("OPENVINO_LLM_URL") // k8s：bh-openvino 服务（LLM :8000）
        ?? "http://127.0.0.1:8866";

    private HttpClient CreateOpenVinoHostClient() => new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>所有 OpenVINO LLM 实例状态（运行/健康/托管/pid）</summary>
    [HttpGet("openvino-llm/status")]
    public async Task<ActionResult> GetOpenVinoLlmStatus()
    {
        try
        {
            using var client = CreateOpenVinoHostClient();
            var result = await client.GetFromJsonAsync<JsonElement>(OpenVinoHostUrl + "/status");
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 OpenVINO 托管服务失败 ({Url})", OpenVinoHostUrl);
            return StatusCode(502, new { error = "OpenVINO 托管服务不可用（宿主机 8866 未运行）", detail = ex.Message });
        }
    }

    /// <summary>启动指定端口的 OpenVINO LLM 实例</summary>
    [HttpPost("openvino-llm/start")]
    public async Task<ActionResult> StartOpenVinoLlm([FromBody] JsonElement body)
    {
        var port = body.TryGetProperty("port", out var p) ? p.GetInt32() : 0;
        return await ForwardControlAsync("start", port);
    }

    /// <summary>停止指定端口的 OpenVINO LLM 实例</summary>
    [HttpPost("openvino-llm/stop")]
    public async Task<ActionResult> StopOpenVinoLlm([FromBody] JsonElement body)
    {
        var port = body.TryGetProperty("port", out var p) ? p.GetInt32() : 0;
        return await ForwardControlAsync("stop", port);
    }

    private async Task<ActionResult> ForwardControlAsync(string action, int port)
    {
        try
        {
            using var client = CreateOpenVinoHostClient();
            // 显式构造转发体（避免 JsonElement 序列化丢失字段）
            var content = new StringContent(JsonSerializer.Serialize(new { port }), System.Text.Encoding.UTF8, "application/json");
            var resp = await client.PostAsync($"{OpenVinoHostUrl}/{action}", content);
            var result = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Action} OpenVINO 托管实例失败 ({Url})", action, OpenVinoHostUrl);
            return StatusCode(502, new { error = "OpenVINO 托管服务不可用（宿主机 8866 未运行）", detail = ex.Message });
        }
    }
}
