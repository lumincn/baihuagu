using System.Text.Json;
using BaihuaSdk.Storage;
using BaihuaSdk.Signing;
using BaihuaSdk.Transport;

namespace MobileApp.Maui.Services;

public interface IMasterService
{
    Task<List<MasterListItem>> GetMastersAsync();
    Task<CreateMasterResponse> CreateMasterAsync(string goal, string industry);
    Task<bool> DeleteMasterAsync(string masterId);
    Task<ApprenticeProfileResponse> GetProfileAsync(string masterId);
    Task<StageCompleteResponse> CompleteStageAsync(string masterId, string stageName);
    IAsyncEnumerable<string> StreamChatAsync(string masterId, string message, string stage, List<ChatHistoryItem>? history = null, CancellationToken ct = default);
    List<string> GetIndustries();
    string ResolveMasterName(string industry);
    bool IsMedicalOrLegalIndustry(string industry);
}

public class MasterService : IMasterService
{
    private readonly IServerConfigStore _serverStore;
    private readonly IRequestSigner _signer;
    private readonly HttpClient _httpClient;

    private static readonly Dictionary<string, string> IndustryMasterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["中医"] = "岐伯", ["医学"] = "岐伯",
        ["计算机"] = "图灵", ["IT"] = "图灵", ["编程"] = "图灵",
        ["会计"] = "算圣", ["财务"] = "算圣",
        ["教资"] = "夫子", ["教育"] = "夫子",
        ["法律"] = "廷尉",
        ["建筑"] = "鲁班",
    };

    private static readonly string[] Industries = ["中医", "医学", "计算机", "IT", "编程", "会计", "财务", "教资", "教育", "法律", "建筑", "通用"];

    public MasterService(IServerConfigStore serverStore, IRequestSigner signer, HttpClient httpClient)
    {
        _serverStore = serverStore;
        _signer = signer;
        _httpClient = httpClient;
    }

    public List<string> GetIndustries() => Industries.ToList();

    public string ResolveMasterName(string industry)
    {
        foreach (var (key, name) in IndustryMasterNames)
        {
            if (industry.Contains(key, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return "先生";
    }

    public bool IsMedicalOrLegalIndustry(string industry)
    {
        if (string.IsNullOrEmpty(industry)) return false;
        var lower = industry.ToLowerInvariant();
        return lower.Contains("医") || lower.Contains("药") || lower.Contains("法") || lower.Contains("律");
    }

    private async Task<HttpTransport> CreateTransportAsync()
    {
        var server = await _serverStore.GetCurrentServerAsync();
        if (server == null)
            throw new InvalidOperationException("未选择服务器，请先配对");

        return new HttpTransport(_httpClient, _signer, server.HttpUrl);
    }

    public async Task<List<MasterListItem>> GetMastersAsync()
    {
        var transport = await CreateTransportAsync();
        var response = await transport.GetJsonAsync<List<MasterListItem>>("/api/master");
        if (!response.IsSuccess)
            throw new InvalidOperationException(response.ErrorMessage ?? "获取师父列表失败");
        return response.Data ?? new();
    }

    public async Task<CreateMasterResponse> CreateMasterAsync(string goal, string industry)
    {
        var transport = await CreateTransportAsync();
        var request = new CreateMasterRequest { Goal = goal, Industry = industry };
        var response = await transport.PostJsonAsync<CreateMasterResponse>("/api/master/create", request);
        if (!response.IsSuccess)
            throw new InvalidOperationException(response.ErrorMessage ?? "创建师父失败");
        return response.Data ?? new();
    }

    public async Task<bool> DeleteMasterAsync(string masterId)
    {
        var server = await _serverStore.GetCurrentServerAsync();
        if (server == null)
            throw new InvalidOperationException("未选择服务器");

        var baseUrl = HttpTransport.NormalizeBaseUrl(server.HttpUrl);
        var url = $"{baseUrl}/api/master/{masterId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        var signHeaders = _signer.SignRequest(HttpMethod.Delete.Method, url, null, baseUrl);
        foreach (var (k, v) in signHeaders)
            request.Headers.TryAddWithoutValidation(k, v);

        using var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task<ApprenticeProfileResponse> GetProfileAsync(string masterId)
    {
        var transport = await CreateTransportAsync();
        var response = await transport.GetJsonAsync<ApprenticeProfileResponse>($"/api/master/{masterId}/profile");
        if (!response.IsSuccess)
            throw new InvalidOperationException(response.ErrorMessage ?? "获取画像失败");
        return response.Data ?? new();
    }

    public async Task<StageCompleteResponse> CompleteStageAsync(string masterId, string stageName)
    {
        var transport = await CreateTransportAsync();
        var request = new StageCompleteRequest { StageName = stageName };
        var response = await transport.PostJsonAsync<StageCompleteResponse>($"/api/master/{masterId}/stage-complete", request);
        if (!response.IsSuccess)
            throw new InvalidOperationException(response.ErrorMessage ?? "阶段完成处理失败");
        return response.Data ?? new();
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string masterId, string message, string stage,
        List<ChatHistoryItem>? history = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var server = await _serverStore.GetCurrentServerAsync();
        if (server == null)
            throw new InvalidOperationException("未选择服务器");

        var baseUrl = HttpTransport.NormalizeBaseUrl(server.HttpUrl);
        var url = $"{baseUrl}/api/master/chat/stream";

        var requestBody = new MasterChatRequest
        {
            MasterId = masterId,
            Message = message,
            Stage = stage,
            History = history
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        };

        var signHeaders = _signer.SignRequest(HttpMethod.Post.Method, url, jsonBody, baseUrl);
        foreach (var (k, v) in signHeaders)
            request.Headers.TryAddWithoutValidation(k, v);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6).Trim();
            if (data == "")
                continue;

            var parsed = TryParseContent(data);
            if (parsed != null)
                yield return parsed;
        }
    }

    private static string? TryParseContent(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("content", out var content))
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            return null;
        }
        catch
        {
            return data;
        }
    }
}
