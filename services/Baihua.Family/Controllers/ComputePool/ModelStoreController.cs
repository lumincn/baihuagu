using System.Formats.Tar;
using System.IO.Compression;
using Baihua.Contracts.Benchmark;
using Baihua.Contracts.ComputePool;
using Baihua.Core.Services;
using Baihua.Family.Services;
using Microsoft.AspNetCore.Mvc;

namespace Baihua.Family.Controllers.ComputePool;

/// <summary>
/// 算力池对端服务端点（X-Server-Token 自校验，公开路径）：
/// - POST /mg/benchmark/run：对端发起本机单模型快速测速（实测 token/s）
/// - GET  /mg/model-store/list：本机已下载模型清单（可被对端拉取）
/// - GET  /mg/model-store/download/{name}：以 tar 流式下发模型目录
/// </summary>
[ApiController]
public class ModelStoreController : ControllerBase
{
    private readonly AiSettingsService _aiSettings;
    private readonly AiClientService _aiClient;
    private readonly Microsoft.Extensions.Options.IOptions<LocalAiOptions> _localAiOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModelStoreController> _logger;
    private readonly BenchmarkRepository _benchmarkRepository;

    public ModelStoreController(
        AiSettingsService aiSettings,
        AiClientService aiClient,
        Microsoft.Extensions.Options.IOptions<LocalAiOptions> localAiOptions,
        IConfiguration configuration,
        ILogger<ModelStoreController> logger,
        BenchmarkRepository benchmarkRepository)
    {
        _aiSettings = aiSettings;
        _aiClient = aiClient;
        _localAiOptions = localAiOptions;
        _configuration = configuration;
        _logger = logger;
        _benchmarkRepository = benchmarkRepository;
    }

    private string ModelRoot => _localAiOptions.Value.GetModelRoot();

    private bool AuthorizePeer()
    {
        var localToken = _configuration["BAIHUA_SERVER_MSG_TOKEN"] ?? "";
        if (string.IsNullOrEmpty(localToken)) return true;
        var token = Request.Headers["X-Server-Token"].FirstOrDefault();
        return string.Equals(token, localToken, StringComparison.Ordinal);
    }

    /// <summary>对端发起本机单模型快速测速（短提示 + max_tokens=64，几秒出结果）。</summary>
    [HttpPost("/mg/benchmark/run")]
    public async Task<ActionResult<BenchmarkRunResultDto>> RunBenchmark([FromBody] PeerBenchmarkRequest request, CancellationToken ct)
    {
        if (!AuthorizePeer()) return Unauthorized(new { error = "口令校验失败" });
        if (string.IsNullOrWhiteSpace(request.ModelName))
            return BadRequest(new BenchmarkRunResultDto { Success = false, Error = "缺少 modelName" });

        try
        {
            var modelName = request.ModelName.Trim();
            var provider = _aiSettings.GetAiProviders()
                .FirstOrDefault(p => p.Models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)));
            if (provider == null)
                return BadRequest(new BenchmarkRunResultDto { Success = false, Error = $"本机无模型 {modelName}" });

            // 快速测速：短提示 + 小 max_tokens，避免长作文把超时窗口耗尽
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.System, "请尽量简短回答。"),
                new(Microsoft.Extensions.AI.ChatRole.User, "用三句话介绍你自己。")
            };
            var options = AiClientService.BuildChatOptions(maxOutputTokens: 64, temperature: 0.3f);

            // 用无缓存的 client 测速（分布式缓存会命中同 prompt 的旧响应，测不出真实速度）
            var rawClient = _aiClient.CreateChatClient(provider, modelName);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await rawClient.GetResponseAsync(messages, options, ct);
            sw.Stop();

            var text = response.Text ?? "";
            var actualTokens = response.Usage?.OutputTokenCount is long ot && ot > 0 ? (int)ot : (int?)(text.Length * 0.7);
            var elapsedSec = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var tps = Math.Round(actualTokens.Value / elapsedSec, 1);

            // 持久化到 Benchmark 排行榜（category="quick"，不影响模型评测页的 tcm/coding 分类），
            // 使本机 /mg/capabilities 的 GetBenchmarkTps 能持续广播该模型的实测 TPS，
            // 否则算力池总览只有内存里的一次性回写，60 秒刷新后又被清空。
            await _benchmarkRepository.SaveSessionAsync(new BenchmarkSession
            {
                ModelName = modelName,
                Category = "quick",
                ProviderId = provider.Id,
                ModelId = modelName,
                TestedAt = DateTime.UtcNow,
                Results = new List<BenchmarkPromptResult>
                {
                    new()
                    {
                        PromptId = "quick",
                        PromptTitle = "算力池快速测速",
                        LatencyMs = (long)Math.Round(sw.Elapsed.TotalMilliseconds),
                        OutputChars = text.Length,
                        TokensPerSecond = tps,
                        ResponseText = text.Length > 200 ? text[..200] : text,
                        QualityScore = 0
                    }
                }
            });

            return Ok(new BenchmarkRunResultDto
            {
                Success = true,
                ModelName = modelName,
                TokensPerSecond = tps,
                LatencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                TestedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "对端测速失败 {Model}", request.ModelName);
            return Ok(new BenchmarkRunResultDto { Success = false, Error = ex.Message, ModelName = request.ModelName.Trim() });
        }
    }

    /// <summary>本机已下载模型清单（供对端拉取，模型商店）。</summary>
    [HttpGet("/mg/model-store/list")]
    public ActionResult<List<ModelStoreEntryDto>> ListStore()
    {
        if (!AuthorizePeer()) return Unauthorized(new { error = "口令校验失败" });
        try
        {
            var root = ModelRoot;
            if (!Directory.Exists(root))
                return Ok(new List<ModelStoreEntryDto>());

            var entries = Directory.GetDirectories(root)
                .Where(d => !d.EndsWith(".downloading", StringComparison.OrdinalIgnoreCase))
                .Select(d =>
                {
                    var files = Directory.GetFiles(d, "*", SearchOption.AllDirectories);
                    return new ModelStoreEntryDto
                    {
                        Name = Path.GetFileName(d),
                        SizeBytes = files.Sum(f => new FileInfo(f).Length),
                        FileCount = files.Length
                    };
                })
                .Where(e => e.SizeBytes > 0)
                .OrderBy(e => e.Name)
                .ToList();
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "列出模型商店失败");
            return Ok(new List<ModelStoreEntryDto>());
        }
    }

    /// <summary>以 tar 流式下发模型目录（模型文件多为已压缩权重，不二次 gzip）。</summary>
    [HttpGet("/mg/model-store/download/{modelName}")]
    public async Task DownloadModel(string modelName, CancellationToken ct)
    {
        if (!AuthorizePeer())
        {
            Response.StatusCode = 401;
            return;
        }

        var root = ModelRoot;
        var dir = Path.Combine(root, modelName);
        if (string.IsNullOrWhiteSpace(modelName) || !Directory.Exists(dir))
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "模型不存在" }, ct);
            return;
        }

        Response.ContentType = "application/x-tar";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{modelName}.tar\"";
        await using var tar = new TarWriter(Response.Body, leaveOpen: false);
        await WriteDirectoryToTarAsync(tar, dir, modelName, ct);
    }

    private static async Task WriteDirectoryToTarAsync(TarWriter tar, string dir, string entryPrefix, CancellationToken ct)
    {
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{entryPrefix}/{relative}")
            {
                DataStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            };
            await tar.WriteEntryAsync(entry, ct);
            await entry.DataStream.DisposeAsync();
        }
    }
}
