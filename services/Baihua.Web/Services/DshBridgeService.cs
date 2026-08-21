using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Baihua.Web.Services;

/// <summary>
/// DSH（DeepSeek Harness）桥接服务：连接本机 DSH 的 dsh-baihua-bridge 插件，
/// 驱动 agent 会话并实时接收 session/event 事件流。
/// </summary>
public sealed class DshBridgeService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;
    private readonly int _maxBufferBytes;
    private readonly string _token;

    public DshBridgeService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _baseUrl = (config["DshApi:BaseUrl"] ?? "http://127.0.0.1:3080").TrimEnd('/');
        _maxBufferBytes = (config.GetValue<int?>("DshApi:MaxBufferBytes") ?? 1_000_000);
        _token = config["DshApi:Token"] ?? "";
        _availableDefaultCwd = config["DshApi:DefaultCwd"] ?? "";
    }

    public string BaseUrl => _baseUrl;

    public string DefaultCwd => _availableDefaultCwd;
    private readonly string _availableDefaultCwd;

    /// <summary>读取配置中的默认工作目录（可能为空）。</summary>
    public string TryGetDefaultCwd()
    {
        var cwd = _availableDefaultCwd;
        if (!string.IsNullOrWhiteSpace(cwd)) return cwd.Trim();
        // 兜底：本机用户主目录（仅 Windows 场景友好提示，不做 I/O）
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private HttpClient Client()
    {
        var client = _httpFactory.CreateClient("DshApi");
        // 若配置了共享密钥，为所有桥接 HTTP 请求附加 Bearer 鉴权
        if (!string.IsNullOrEmpty(_token))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        return client;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonSerializerOptions EventJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>健康检查：插件是否在线。</summary>
    public async Task<DshStatusDto?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Client().GetStringAsync($"{_baseUrl}/dsh-bridge/status", ct);
            return JsonSerializer.Deserialize<DshStatusDto>(json, EventJsonOptions());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>列出现有会话（本进程管理过的）。</summary>
    public async Task<IReadOnlyList<DshSessionMetaDto>> GetSessionsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Client().GetStringAsync($"{_baseUrl}/dsh-bridge/sessions", ct);
            var envelope = JsonSerializer.Deserialize<DshSessionListDto>(json, EventJsonOptions());
            return envelope?.Sessions ?? Array.Empty<DshSessionMetaDto>();
        }
        catch
        {
            return Array.Empty<DshSessionMetaDto>();
        }
    }

    /// <summary>拉取某会话的完整事件历史。</summary>
    public async Task<DshHistoryDto?> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var json = await Client().GetStringAsync($"{_baseUrl}/dsh-bridge/sessions/{Uri.EscapeDataString(sessionId)}/history", ct);
            return JsonSerializer.Deserialize<DshHistoryDto>(json, EventJsonOptions());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>提交一条用户消息（新建或续聊）。返回请求是否受理及会话 id。</summary>
    public async Task<DshChatResultDto> SendMessageAsync(string message, string? cwd, string? sessionId, CancellationToken ct = default)
    {
        var client = Client();
        var payload = new { message, cwd, sessionId };
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{_baseUrl}/dsh-bridge/chat", content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        DshChatResultDto? result = null;
        try
        {
            result = JsonSerializer.Deserialize<DshChatResultDto>(text, EventJsonOptions());
        }
        catch
        {
            /* 非 JSON */
        }
        return result ?? new DshChatResultDto
        {
            Ok = response.IsSuccessStatusCode,
            SessionId = sessionId,
            Error = text,
        };
    }

    /// <summary>
    /// 监听某会话的事件流（阻塞直到连接关闭或取消）。每条消息回调 <paramref name="onFrame"/>。
    /// 新建会话时以 <paramref name="sessionId"/> 为空触发，插件会返回一个新的 sessionId。
    /// </summary>
    public async Task StreamEventsAsync(
        string? sessionId,
        string? cwd,
        Func<DshStreamFrame, Task> onFrame,
        CancellationToken ct = default)
    {
        using var ws = new ClientWebSocket();
        var wsUrl = $"{BaseWsUrl}/dsh-bridge/stream?sessionId={Uri.EscapeDataString(sessionId ?? "")}{(string.IsNullOrWhiteSpace(cwd) ? "" : "&cwd=" + Uri.EscapeDataString(cwd))}";
        if (!string.IsNullOrEmpty(_token))
            wsUrl += "&token=" + Uri.EscapeDataString(_token);
        var uri = new Uri(wsUrl);
        // ServerName 无需配置；连本机回环
        await ws.ConnectAsync(uri, ct);
        // 取消时主动断开，让 ReceiveAsync 尽快返回
        using var reg = ct.Register(() => { try { ws.Abort(); } catch { } });

        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await onFrame(new DshStreamFrame { Kind = "closed" });
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }

            var line = Encoding.UTF8.GetString(ms.ToArray());
            if (string.IsNullOrWhiteSpace(line)) continue;
            DshStreamFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<DshStreamFrame>(line, EventJsonOptions());
            }
            catch
            {
                frame = new DshStreamFrame { Kind = "raw", Raw = line };
            }
            if (frame is not null) await onFrame(frame);
        }
    }

    private string BaseWsUrl
    {
        get
        {
            var wsScheme = _baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            return $"{wsScheme}://{_baseUrl.Replace("https://", "").Replace("http://", "")}";
        }
    }
}

// ===== DTO =====

public sealed class DshStatusDto
{
    public bool Ok { get; set; }
    public string? Service { get; set; }
    public int ActiveSessions { get; set; }
    public int LoadedSessions { get; set; }
}

public sealed class DshSessionListDto
{
    [JsonPropertyName("sessions")]
    public IReadOnlyList<DshSessionMetaDto>? Sessions { get; set; }
}

public sealed class DshSessionMetaDto
{
    public string Id { get; set; } = "";
    public string? Title { get; set; }
    public string? Cwd { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public int MessageCount { get; set; }
}

public sealed class DshHistoryDto
{
    public string? SessionId { get; set; }
    public string? Title { get; set; }
    [JsonPropertyName("events")]
    public IReadOnlyList<DshStreamFrame>? Events { get; set; }
}

public sealed class DshChatResultDto
{
    public bool Ok { get; set; }
    public string? SessionId { get; set; }
    public int MessageCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>一条会话事件帧（服务端 eventToJson 的精简形状）。</summary>
public sealed class DshStreamFrame
{
    public string? Kind { get; set; }
    public string? SessionId { get; set; }
    public long Seq { get; set; }
    public long Time { get; set; }
    public string? Type { get; set; }
    public JsonElement? Data { get; set; }
    /// <summary>WS 级消息：session / connected / error / closed（未走 Type 判别时）。</summary>
    public string? Message { get; set; }
    public string? Raw { get; set; }
}
