using System.Collections.Concurrent;
using System.Formats.Tar;
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
    private readonly BenchmarkRepository _benchmarkRepository;

    private readonly ConcurrentDictionary<string, ComputeNodeCapabilitiesDto> _peerCapabilities = new();
    /// <summary>对端 /health 可达时间（capabilities 未就绪时仍能判断在线）</summary>
    private readonly ConcurrentDictionary<string, DateTime> _peerReachable = new();
    private Timer? _timer;

    public ComputePoolService(
        ServerMessageService messageService,
        AiConfigService aiConfig,
        AiSettingsService aiSettings,
        ServerAddressService serverAddress,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ComputePoolService> logger,
        BenchmarkRepository benchmarkRepository)
    {
        _messageService = messageService;
        _aiConfig = aiConfig;
        _aiSettings = aiSettings;
        _serverAddress = serverAddress;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _benchmarkRepository = benchmarkRepository;
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
                    // capabilities 未就绪（对端是旧代码/未配置）→ 用 /health 判定在线，算力状态留空
                    _peerCapabilities.TryRemove(peer.ServerId, out _);
                    await ProbePeerHealthAsync(peer, client, ct);
                    continue;
                }

                var caps = await resp.Content.ReadFromJsonAsync<ComputeNodeCapabilitiesDto>(ct);
                if (caps == null || string.IsNullOrEmpty(caps.ServerId))
                    continue;

                caps.HostUrl = peer.BaseUrl;
                caps.ModelStore = await FetchPeerModelStoreAsync(peer, token, client, ct);
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


    /// <summary>对端 /health 探测：可达则记录在线（供 UI 显示"在线但未接入算力池"）。</summary>
    private async Task ProbePeerHealthAsync(ServerPeer peer, HttpClient client, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{peer.BaseUrl.TrimEnd('/')}/health");
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                _peerReachable[peer.ServerId] = DateTime.UtcNow;
            else
                _peerReachable.TryRemove(peer.ServerId, out _);
        }
        catch
        {
            _peerReachable.TryRemove(peer.ServerId, out _);
        }
    }

    /// <summary>拉取对端模型商店清单（无则空列表）。</summary>
    private async Task<List<ModelStoreEntryDto>> FetchPeerModelStoreAsync(ServerPeer peer, string token, HttpClient client, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{peer.BaseUrl.TrimEnd('/')}/mg/model-store/list");
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("X-Server-Token", token);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new List<ModelStoreEntryDto>();
            return await resp.Content.ReadFromJsonAsync<List<ModelStoreEntryDto>>(ct) ?? new List<ModelStoreEntryDto>();
        }
        catch
        {
            return new List<ModelStoreEntryDto>();
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
                Online = caps != null
                    || (_peerReachable.TryGetValue(peer.ServerId, out var rt) && rt > DateTime.UtcNow.AddMinutes(-5))
                    || (peer.LastSeenUtc.HasValue && peer.LastSeenUtc.Value > DateTime.UtcNow.AddMinutes(-5)),
                LastSeenUtc = caps?.UpdatedAt ?? peer.LastSeenUtc,
                CpuCores = caps?.CpuCores,
                Providers = caps?.Providers ?? new List<ComputeProviderDto>(),
                ProviderRegistered = registered,
                ModelStore = caps?.ModelStore ?? new List<ModelStoreEntryDto>()
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
            openAiBaseUrl = $"{hostUrl}/mg/pool/v1"; // 统一推理网关（全网路由）

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
                    Models = p.Models.Select(m => new ComputeModelDto
                    {
                        Name = m.Name,
                        IsMain = m.IsMain,
                        TokensPerSecond = GetBenchmarkTps(m.Name)
                    }).ToList()
                })
                .ToList(),
            ProviderRegistered = true
        };
    }

    /// <summary>排行榜里该模型最近一次实测 token/s（无记录返回 null）。</summary>
    private double? GetBenchmarkTps(string modelName)
    {
        try
        {
            return _benchmarkRepository.GetLeaderboard()
                .Where(h => string.Equals(h.ModelName, modelName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => h.LastTestedAt)
                .Select(h => h.AvgTokensPerSecond)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>跨机测速：对端运行单模型快速 benchmark，结果回写能力缓存并返回。</summary>
    public async Task<BenchmarkRunResultDto?> RunPeerBenchmarkAsync(string serverId, string modelName, CancellationToken ct = default)
    {
        // 本机模型 → 本地测速（经本机 /mg/benchmark/run 同一逻辑，直接调用）
        if (string.Equals(serverId, GetLocalServerId(), StringComparison.OrdinalIgnoreCase))
        {
            return await RunLocalBenchmarkAsync(modelName, ct);
        }

        var peer = (await _messageService.ListPeersAsync(ct)).FirstOrDefault(p => p.ServerId == serverId);
        if (peer == null) return null;

        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromMinutes(10);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{peer.BaseUrl.TrimEnd('/')}/mg/benchmark/run");
            var token = !string.IsNullOrWhiteSpace(peer.Token) ? peer.Token! : _messageService.LocalToken;
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("X-Server-Token", token);
            req.Content = JsonContent.Create(new PeerBenchmarkRequest { ModelName = modelName });

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var result = await resp.Content.ReadFromJsonAsync<BenchmarkRunResultDto>(ct);
            if (result is { Success: true })
            {
                // 回写能力缓存：对端该模型的 TPS 更新
                if (_peerCapabilities.TryGetValue(peer.ServerId, out var caps))
                {
                    foreach (var p in caps.Providers)
                    foreach (var m in p.Models)
                    {
                        if (string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase))
                            m.TokensPerSecond = result.TokensPerSecond;
                    }
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "跨机测速失败 {Server} {Model}", serverId, modelName);
            return null;
        }
    }

    /// <summary>从对端拉取模型（模型商店）：下载 tar 流并解压到本机模型根目录。</summary>
    public async Task<(bool ok, string? error)> PullPeerModelAsync(string serverId, string modelName, CancellationToken ct = default)
    {
        var peer = (await _messageService.ListPeersAsync(ct)).FirstOrDefault(p => p.ServerId == serverId);
        if (peer == null) return (false, "对端未登记");
        if (string.IsNullOrWhiteSpace(modelName) || modelName.Contains('/') || modelName.Contains('\\') || modelName == "..")
            return (false, "非法的模型名");

        var root = GetLocalModelRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, modelName);
        if (Directory.Exists(target))
            return (false, $"本机已存在 {modelName}");

        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromMinutes(60);
            var url = $"{peer.BaseUrl.TrimEnd('/')}/mg/model-store/download/{Uri.EscapeDataString(modelName)}";
            var token = !string.IsNullOrWhiteSpace(peer.Token) ? peer.Token! : _messageService.LocalToken;
            var (ok, error) = await ModelTarTransfer.DownloadAndExtractAsync(client, url, token, target, modelName, ct);
            if (!ok)
                return (false, error);
            _logger.LogInformation("[ComputePool] 已从 {Server} 拉取模型 {Model}", peer.Name, modelName);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取模型失败 {Server} {Model}", serverId, modelName);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 跨机布署：本机已有模型 → 请求对端从本机 model-store 拉取并启动运行时（对端常驻服务）。
    /// 对端拉取完成后调用 ILocalRuntimeManager.StartAsync 启动推理（GPU 失败自动回退 CPU）。
    /// </summary>
    public async Task<DeployModelResultDto> DeployPeerModelAsync(string serverId, string modelName, string? device, CancellationToken ct = default)
    {
        // 本机布署到本机没有意义
        if (string.Equals(serverId, GetLocalServerId(), StringComparison.OrdinalIgnoreCase))
            return new DeployModelResultDto { Success = false, Error = "目标不能是本机", ModelName = modelName };

        var peer = (await _messageService.ListPeersAsync(ct)).FirstOrDefault(p => p.ServerId == serverId);
        if (peer == null)
            return new DeployModelResultDto { Success = false, Error = "对端未登记", ModelName = modelName };
        if (string.IsNullOrWhiteSpace(modelName) || modelName.Contains('/') || modelName.Contains('\\') || modelName == "..")
            return new DeployModelResultDto { Success = false, Error = "非法的模型名", ModelName = modelName };

        // 本机必须已有该模型（布署 = 把本机模型推给对端跑）
        var localRoot = GetLocalModelRoot();
        if (!Directory.Exists(Path.Combine(localRoot, modelName)))
            return new DeployModelResultDto { Success = false, Error = $"本机模型根中不存在 {modelName}（先在本机下载）", ModelName = modelName };

        // 本机对外下载地址：对端将从这里拉取 tar
        var hostIp = _configuration["BAIHUA_HOST_IP"];
        var localUrl = !string.IsNullOrWhiteSpace(hostIp)
            ? $"http://{hostIp}"
            : _serverAddress.GetLocalPublicBaseUrl();
        if (string.IsNullOrEmpty(localUrl))
            return new DeployModelResultDto { Success = false, Error = "无法确定本机对外地址（BAIHUA_HOST_IP 未配置）", ModelName = modelName };

        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromMinutes(60);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{peer.BaseUrl.TrimEnd('/')}/mg/model-store/deploy");
            var token = !string.IsNullOrWhiteSpace(peer.Token) ? peer.Token! : _messageService.LocalToken;
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("X-Server-Token", token);
            req.Content = JsonContent.Create(new PeerDeployRequest
            {
                SourceServerId = GetLocalServerId(),
                SourceUrl = $"{localUrl}/mg/model-store/download/{Uri.EscapeDataString(modelName)}",
                ModelName = modelName,
                Device = device
            });

            using var resp = await client.SendAsync(req, ct);
            var result = await resp.Content.ReadFromJsonAsync<DeployModelResultDto>(ct);
            if (result == null)
                return new DeployModelResultDto { Success = false, Error = $"对端返回 {(int)resp.StatusCode}", ModelName = modelName };
            if (result.Success)
                _logger.LogInformation("[ComputePool] 已布署 {Model} 到 {Server}（{Device} @ :{Port}）",
                    modelName, peer.Name, result.Device, result.Port);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "跨机布署失败 {Server} {Model}", serverId, modelName);
            return new DeployModelResultDto { Success = false, Error = ex.Message, ModelName = modelName };
        }
    }

    private async Task<BenchmarkRunResultDto?> RunLocalBenchmarkAsync(string modelName, CancellationToken ct)
    {
        // 复用对端端点逻辑：直接构造请求交给同一 controller 不可行，改为通过本机 HTTP 调用
        var hostIp = _configuration["BAIHUA_HOST_IP"];
        var localUrl = !string.IsNullOrWhiteSpace(hostIp)
            ? $"http://{hostIp}"
            : _serverAddress.GetLocalPublicBaseUrl();
        if (string.IsNullOrEmpty(localUrl)) return null;
        try
        {
            using var client = _httpClientFactory.CreateClient("ComputePool");
            client.Timeout = TimeSpan.FromMinutes(10);
            using var resp = await client.PostAsJsonAsync($"{localUrl}/mg/benchmark/run",
                new PeerBenchmarkRequest { ModelName = modelName }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<BenchmarkRunResultDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "本机测速失败 {Model}", modelName);
            return null;
        }
    }

    private string GetLocalServerId()
    {
        try { return _serverAddress.GetServerInstanceId(); }
        catch { return ""; }
    }

    private string GetLocalModelRoot()
    {
        // 与 ModelDownloadService 一致：DownloadDirectory 或 $BAIHUA_HOME/models
        var dl = _configuration["LocalAI:DownloadDirectory"];
        if (!string.IsNullOrWhiteSpace(dl)) return dl;
        var home = _configuration["BAIHUA_HOME"];
        return string.IsNullOrWhiteSpace(home) ? Path.Combine(Environment.CurrentDirectory, "models") : Path.Combine(home, "models");
    }


    /// <summary>网关路由：查找拥有该模型的节点（本机 AI 服务优先，其余按实测 TPS 从快到慢）。</summary>
    public async Task<(string baseUrl, string providerId, string name, bool isLocal, double? tps)?> FindBestNodeAsync(string modelName, CancellationToken ct = default)
    {
        var nodes = await FindCandidateNodesAsync(modelName, ct);
        return nodes.Count > 0 ? nodes[0] : null;
    }

    /// <summary>网关 failover：拥有该模型的所有候选节点（本机优先，对端按实测 TPS 降序）。</summary>
    public async Task<List<(string baseUrl, string providerId, string name, bool isLocal, double? tps)>> FindCandidateNodesAsync(string modelName, CancellationToken ct = default)
    {
        var result = new List<(string baseUrl, string providerId, string name, bool isLocal, double? tps)>();

        // 本机 AI 服务（shim）是否提供该模型
        var aiProviders = await GetAiServiceProvidersAsync(ct);
        var localAiProvider = aiProviders.FirstOrDefault(p => p.Models.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)));
        if (localAiProvider != null)
        {
            var hostIp = _configuration["BAIHUA_HOST_IP"];
            var localHost = !string.IsNullOrWhiteSpace(hostIp) ? $"http://{hostIp}" : _serverAddress.GetLocalPublicBaseUrl();
            var localModel = localAiProvider.Models.First(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase));
            result.Add(($"{localHost}/mg/ai/v1", localAiProvider.Id, "本机", true, localModel.TokensPerSecond));
        }

        // 对端：拥有该模型的节点按 TPS 降序
        var peers = new List<(string baseUrl, string providerId, string name, double? tps)>();
        foreach (var kv in _peerCapabilities)
        {
            var caps = kv.Value;
            if (string.IsNullOrWhiteSpace(caps.OpenAiBaseUrl)) continue;
            var p = caps.Providers.FirstOrDefault(p => p.Models.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)));
            if (p == null) continue;
            var model = p.Models.First(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase));
            peers.Add((caps.OpenAiBaseUrl, p.Id, caps.Name, model.TokensPerSecond));
        }
        result.AddRange(peers
            .OrderByDescending(c => c.tps.HasValue)
            .ThenByDescending(c => c.tps ?? 0)
            .Select(c => (c.baseUrl, c.providerId, c.name, false, c.tps)));

        return result;
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
                    Models = p.Models.Select(m => new ComputeModelDto
                    {
                        Name = m.Name,
                        IsMain = m.IsMain,
                        TokensPerSecond = GetBenchmarkTps(m.Name)
                    }).ToList()
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
