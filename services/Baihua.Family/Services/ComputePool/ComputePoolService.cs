using System.Collections.Concurrent;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Baihua.Contracts.ComputePool;
using Baihua.Core;
using Baihua.Core.Models;
using Baihua.Core.Services;
using Baihua.Data;
using Baihua.Data.Entities;
using Baihua.Family.Services.ServerMessaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Baihua.Family.Services.ComputePool;

/// <summary>
/// 百花算力池汇聚服务：
/// - 定时拉取每个对端服务器的 /mg/capabilities（X-Server-Token 鉴权）并缓存；
/// - 把对外暴露了 OpenAI 兼容端点的对端自动注册为本机 AI 提供方（peer- 前缀），
///   聊天/拜师/OpenClaw 即可直接选用对端算力；
/// - 向 WebUI 提供算力池总览（/api/compute-pool）。
/// </summary>
public class ComputePoolService : IHostedService, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly ServerMessageService _messageService;
    private readonly AiConfigService _aiConfig;
    private readonly AiSettingsService _aiSettings;
    private readonly ServerAddressService _serverAddress;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComputePoolService> _logger;

    private readonly ConcurrentDictionary<string, ComputeNodeCapabilitiesDto> _peerCapabilities = new();
    private Timer? _timer;

    public ComputePoolService(
        ServerMessageService messageService,
        AiConfigService aiConfig,
        AiSettingsService aiSettings,
        ServerAddressService serverAddress,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ComputePoolService> logger)
    {
        _messageService = messageService;
        _aiConfig = aiConfig;
        _aiSettings = aiSettings;
        _serverAddress = serverAddress;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer ??= new Timer(async _ =>
            {
                try { await RefreshAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "算力池刷新失败"); }
            },
            null, TimeSpan.FromSeconds(5), RefreshInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Start()
    {
        _timer ??= new Timer(async _ =>
            {
                try { await RefreshAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "算力池刷新失败"); }
            },
            null, TimeSpan.FromSeconds(5), RefreshInterval);
    }

    /// <summary>拉取所有对端能力并自动注册可用的 AI 提供方。</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var peers = await _messageService.ListPeersAsync(ct);
        var localToken = _messageService.LocalToken;
        using var client = _httpClientFactory.CreateClient("ComputePool");
        client.Timeout = TimeSpan.FromSeconds(10);

        foreach (var peer in peers)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{peer.BaseUrl.TrimEnd('/')}/mg/capabilities");
                var token = !string.IsNullOrWhiteSpace(peer.Token) ? peer.Token! : localToken;
                if (!string.IsNullOrEmpty(token))
                    req.Headers.TryAddWithoutValidation("X-Server-Token", token);

                using var resp = await client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _peerCapabilities.TryRemove(peer.ServerId, out _);
                    continue;
                }

                var caps = await resp.Content.ReadFromJsonAsync<ComputeNodeCapabilitiesDto>(ct);
                if (caps == null || string.IsNullOrEmpty(caps.ServerId))
                    continue;

                caps.HostUrl = peer.BaseUrl;
                _peerCapabilities[caps.ServerId] = caps;
                _logger.LogDebug("[ComputePool] 已缓存对端能力: {Name} ({ServerId}) {ProviderCount} 个提供方",
                    caps.Name, caps.ServerId, caps.Providers.Count);

                await AutoRegisterProviderAsync(peer, caps, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[ComputePool] 拉取对端能力失败 {BaseUrl}: {Msg}", peer.BaseUrl, ex.Message);
            }
        }

        // 清理已删除的对端
        var knownIds = peers.Select(p => p.ServerId).ToHashSet();
        foreach (var key in _peerCapabilities.Keys)
        {
            if (!knownIds.Contains(key))
                _peerCapabilities.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 把对端注册为本机 AI 提供方（仅当对端声明了 OpenAiBaseUrl）。
    /// 一个对端一个提供方（peer-{ServerId}），合并其全部模型；ApiKey 用本机互联口令
    /// （对端 OpenAI shim 按 BAIHUA_AI_EXTERNAL_TOKEN 校验）。
    /// </summary>
    private async Task AutoRegisterProviderAsync(ServerPeer peer, ComputeNodeCapabilitiesDto caps, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caps.OpenAiBaseUrl))
            return;

        var providerId = $"peer-{peer.ServerId}";
        if (providerId.Length > 50) providerId = providerId[..50];

        var models = caps.Providers
            .SelectMany(p => p.Models)
            .Select(m => new AiModelConfig { Name = m.Name, IsMain = false, IsPaid = false })
            .GroupBy(m => m.Name)
            .Select(g => g.First())
            .ToList();
        if (models.Count == 0)
            return;

        // 已存在且模型一致 → 跳过写入（避免每次刷新都动 DB）
        var existing = _aiConfig.GetProvider(providerId);
        if (existing != null)
        {
            var existingNames = existing.Models.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var newNames = models.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (existingNames.SequenceEqual(newNames)
                && string.Equals(existing.AiBaseUrl?.TrimEnd('/'), caps.OpenAiBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var setting = new AiProviderSetting
        {
            ProviderId = providerId,
            ProviderName = $"{caps.Name} · 局域网算力池",
            BaseUrl = caps.OpenAiBaseUrl.TrimEnd('/'),
            ModelsJson = AiConfigService.SerializeModels(models),
            IsMain = false,
            IsEnabled = true,
            Tier = (int)AiModelTier.Tier2_Local,
            SortOrder = 100
        };

        var localToken = _messageService.LocalToken;
        _aiConfig.SaveProvider(setting, string.IsNullOrEmpty(localToken) ? null : localToken);
        _aiSettings.ClearAiProvidersCache();
        _logger.LogInformation("[ComputePool] 已注册对端提供方 {ProviderId} ({Name}): {ModelCount} 个模型, {Url}",
            providerId, caps.Name, models.Count, caps.OpenAiBaseUrl);
    }

    /// <summary>算力池总览（本机 + 对端）。</summary>
    public async Task<ComputePoolViewDto> GetPoolViewAsync(CancellationToken ct = default)
    {
        var localNode = GetLocalNode();
        // 本机节点补上 AI 服务（shim 路由）的提供方，与 /mg/capabilities 广播保持一致
        var aiProviders = await GetAiServiceProvidersAsync(ct);
        foreach (var remote in aiProviders)
        {
            if (localNode.Providers.Any(g => string.Equals(g.Id, remote.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            localNode.Providers.Add(remote);
        }

        var nodes = new List<ComputePoolNodeDto> { localNode };
        var peers = await _messageService.ListPeersAsync(ct);

        foreach (var peer in peers)
        {
            _peerCapabilities.TryGetValue(peer.ServerId, out var caps);
            var registered = _aiConfig.GetProvider($"peer-{peer.ServerId}") != null;
            nodes.Add(new ComputePoolNodeDto
            {
                ServerId = peer.ServerId,
                Name = caps?.Name ?? peer.Name,
                HostUrl = peer.BaseUrl,
                OpenAiBaseUrl = caps?.OpenAiBaseUrl ?? "",
                IsLocal = false,
                Online = caps != null || (peer.LastSeenUtc.HasValue && peer.LastSeenUtc.Value > DateTime.UtcNow.AddMinutes(-5)),
                LastSeenUtc = caps?.UpdatedAt ?? peer.LastSeenUtc,
                CpuCores = caps?.CpuCores,
                Providers = caps?.Providers ?? new List<ComputeProviderDto>(),
                ProviderRegistered = registered
            });
        }

        return new ComputePoolViewDto { Nodes = nodes, UpdatedAt = DateTime.UtcNow };
    }

    /// <summary>选用某个节点+模型：把对端提供方设为本机主提供方并选主模型。</summary>
    public bool SelectModel(string serverId, string modelName, out string? error)
    {
        error = null;
        var providerId = $"peer-{serverId}";
        var provider = _aiConfig.GetProvider(providerId);
        if (provider == null)
        {
            error = "该节点尚未注册为可选用提供方（对端需配置 BAIHUA_PUBLIC_OPENAI_BASE_URL）";
            return false;
        }
        var model = provider.Models.FirstOrDefault(m => m.Name == modelName);
        if (model == null)
        {
            error = $"对端未提供模型 {modelName}";
            return false;
        }

        _aiConfig.SaveProvider(new AiProviderSetting
        {
            ProviderId = providerId,
            ProviderName = provider.Name,
            BaseUrl = provider.AiBaseUrl ?? "",
            ModelsJson = AiConfigService.SerializeModels(provider.Models.Select(m => new AiModelConfig
            {
                Name = m.Name,
                IsPaid = m.IsPaid,
                IsMain = m.Name == modelName
            }).ToList()),
            IsMain = true,
            IsEnabled = true,
            Tier = (int)provider.Tier,
            SortOrder = 0
        }, null);
        _aiSettings.ClearAiProvidersCache();
        return true;
    }

    private ComputePoolNodeDto GetLocalNode()
    {
        var settings = _serverAddress.GetSettings();
        var hostIp = _configuration["BAIHUA_HOST_IP"];
        var hostUrl = !string.IsNullOrWhiteSpace(hostIp)
            ? $"http://{hostIp}"
            : _serverAddress.GetLocalPublicBaseUrl();
        var openAiBaseUrl = _configuration["BAIHUA_PUBLIC_OPENAI_BASE_URL"];
        if (string.IsNullOrWhiteSpace(openAiBaseUrl))
            openAiBaseUrl = $"{hostUrl}/mg/ai/v1";

        return new ComputePoolNodeDto
        {
            ServerId = _serverAddress.GetServerInstanceId(),
            Name = string.IsNullOrWhiteSpace(settings.DisplayName) ? Environment.MachineName : settings.DisplayName,
            HostUrl = hostUrl,
            OpenAiBaseUrl = openAiBaseUrl,
            IsLocal = true,
            Online = true,
            LastSeenUtc = DateTime.UtcNow,
            CpuCores = Environment.ProcessorCount,
            Providers = _aiSettings.GetAiProviders()
                .Where(p => p.Models is { Count: > 0 })
                .Select(p => new ComputeProviderDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Tier = ((int)p.Tier).ToString(),
                    Models = p.Models.Select(m => new ComputeModelDto { Name = m.Name, IsMain = m.IsMain }).ToList()
                })
                .ToList(),
            ProviderRegistered = true
        };
    }

    private async Task<List<ComputeProviderDto>> GetAiServiceProvidersAsync(CancellationToken ct)
    {
        try
        {
            var aiBase = Environment.GetEnvironmentVariable("BAIHUA_AI_URL")
                ?? Environment.GetEnvironmentVariable("TASK_RUNNER_AI_API_URL")
                ?? "http://127.0.0.1:8791";
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromSeconds(8);
            var resp = await client.GetAsync($"{aiBase.TrimEnd('/')}/api/ai/config/providers", ct);
            if (!resp.IsSuccessStatusCode)
                return new List<ComputeProviderDto>();
            var providers = await resp.Content.ReadFromJsonAsync<List<AiProviderConfig>>(ct);
            return providers?
                .Where(p => p.Models is { Count: > 0 })
                .Select(p => new ComputeProviderDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Tier = ((int)p.Tier).ToString(),
                    Models = p.Models.Select(m => new ComputeModelDto { Name = m.Name, IsMain = m.IsMain }).ToList()
                })
                .ToList() ?? new List<ComputeProviderDto>();
        }
        catch
        {
            return new List<ComputeProviderDto>();
        }
    }

    public void Dispose() => _timer?.Dispose();
}
