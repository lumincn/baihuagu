using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;
using Serilog;
using Baihua.Core;
using Baihua.Family.Services;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// 显式设置监听地址，确保命令行 --urls 和环境变量 ASPNETCORE_URLS 覆盖 appsettings 默认值
var urls = builder.Configuration["urls"]                                   // dotnet run --urls
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? builder.Configuration["Kestrel:Endpoints:Http:Url"]
    ?? "http://0.0.0.0:8791";
builder.WebHost.UseUrls(urls);

// 百花统一数据根目录 BAIHUA_HOME 由 Core.Shared.BaihuaPaths 管理
// 已迁移至 BAIHUA_HOME，详见 services/Baihua.Contracts/BaihuaPaths.cs


// 添加控制器与 JSON 序列化
builder.Services.AddLocalization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // 禁用 [ApiController] 自动把 4xx/5xx 包装成 ProblemDetails
        // 本项目所有 API 统一使用 { error: "本地化错误消息" } 格式，ProblemDetails 会覆盖自定义 JSON
        options.SuppressMapClientErrors = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Baihua AI API",
        Version = "v1",
        Description = "Baihua AI Service - Models, Chat, Search, Metrics"
    });
});

// AI 域数据库上下文
builder.Services.AddDbContext<Baihua.Data.AIDbContext>(options =>
{
    var dbPath = Baihua.Data.AIDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;")
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<Baihua.Data.AIDbContext>(options =>
{
    var dbPath = Baihua.Data.AIDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;")
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Singleton);

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// AI 配置与客户端基础设施（共享自 Core.Shared）
builder.Services.AddSingleton<AiSettingsService>();
builder.Services.AddSingleton<AiConfigService>();
builder.Services.AddSingleton<MigrationService>();
builder.Services.AddSingleton<Baihua.Core.Notifications.WebUINotificationService>();
builder.Services.AddSingleton<Baihua.Family.Services.HardwareInfoService>();
builder.Services.AddSingleton<Baihua.Family.Services.CapabilityService>();
builder.Services.AddAiClientServices();

// 编程 Agent（Microsoft Agent Framework）
builder.Services.AddSingleton<Baihua.AI.Services.CodeAgentService>();

// 本地模型推理后端（GGUF / ONNX），实现位于 Baihua.AI.Provider
builder.Services.AddSingleton<Baihua.AI.Provider.ILocalModelInference, Baihua.AI.Provider.LlamaSharpInference>();
builder.Services.AddSingleton<Baihua.AI.Provider.ILocalModelInference, Baihua.AI.Provider.OnnxRuntimeGenAIInference>();
builder.Services.AddSingleton<Baihua.AI.Provider.ILocalModelInference, Baihua.AI.Provider.OpenVinoChatInference>();

// 本地视觉分析（Qwen2.5-VL + OpenVINO）
builder.Services.Configure<Baihua.AI.Provider.LocalVisionOptions>(
    builder.Configuration.GetSection("LocalVision"));
builder.Services.AddSingleton<Baihua.AI.Provider.OpenVinoVisionService>();



// 健康检查
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// CORS（仅本地/内网）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return uri.Host is "localhost" or "127.0.0.1" or "::1";
                }
                return false;
            })
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// 日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var serilogConfig = new Serilog.LoggerConfiguration()
    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
    .Enrich.WithProperty("Service", "Baihua.AI")
    .Filter.ByExcluding(e => e.Properties.ContainsKey("SourceContext") &&
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.AspNetCore") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"System.Net.Http") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.EntityFrameworkCore"))
    .WriteTo.Console();

builder.Logging.AddSerilog(serilogConfig.CreateLogger(), dispose: true);
builder.Logging.SetMinimumLevel(builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Baihua.AI", LogLevel.Information);

// OpenTelemetry 导出到 OpenObserve
var openobserveEnabled = builder.Configuration.GetValue<bool?>("OpenObserve:Enabled") ?? true;
var openobserveUrl = builder.Configuration["OpenObserve:WebUrl"] ?? "";
var openobserveUser = builder.Configuration["OpenObserve:User"] ?? "";
var openobservePass = builder.Configuration["OpenObserve:Password"] ?? "";

{
    var otelBuilder = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("Baihua.AI"));

    if (openobserveEnabled && !string.IsNullOrWhiteSpace(openobserveUrl))
    {
        var baseUrl = openobserveUrl.TrimEnd('/');
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{openobserveUser}:{openobservePass}"));
        var authHeader = $"Authorization=Basic {authValue}";
        var isDevelopment = builder.Environment.IsDevelopment();

        otelBuilder.WithMetrics(metrics =>
        {
            metrics.AddMeter("Baihua.AI")
                   .AddMeter("Microsoft.Extensions.AI")
                   .AddView("ai.latency_ms", new OpenTelemetry.Metrics.ExplicitBucketHistogramConfiguration
                   {
                       Boundaries = new double[] { 0, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000, 60000 }
                   })
                   .AddOtlpExporter(options =>
                   {
                       options.Endpoint = new Uri($"{baseUrl}/api/default/v1/metrics");
                       options.Protocol = OtlpExportProtocol.HttpProtobuf;
                       options.TimeoutMilliseconds = 30000;
                       options.Headers = authHeader;
                   });
        })
        .WithLogging(logging =>
        {
            logging.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri($"{baseUrl}/api/default/v1/logs");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.TimeoutMilliseconds = 30000;
                options.Headers = authHeader;
            });
        })
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("Baihua.AI")
                .AddSource("Microsoft.Extensions.AI")
                .SetSampler(isDevelopment
                    ? new AlwaysOnSampler()
                    : new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri($"{baseUrl}/api/default/v1/traces");
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.TimeoutMilliseconds = 30000;
                    options.Headers = authHeader;
                });
        });
    }
}

// AI 配置服务依赖（ApiKey 加密）
builder.Services.AddDataProtection();
builder.Services.AddSingleton<Baihua.Core.Security.ApiKeyProtectionService>();

// 反向代理头部转发
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    // RFC1918 私有网段（Docker 172.x / kind 10.244.x / k3s 10.42.x）
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Baihua AI API V1");
        c.RoutePrefix = "swagger";
    });
}

app.UseForwardedHeaders();
app.UseHealthChecks("/health");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("zh-CN"),
    SupportedCultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") },
    SupportedUICultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") }
});

app.UseCors("AllowAll");

// 访问控制：非公开路径仅允许 loopback
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    var publicPaths = new[] { "/health", "/swagger" };
    if (publicPaths.Any(p => path.StartsWith(p)))
    {
        await next();
        return;
    }

    var remoteIp = context.Connection.RemoteIpAddress;
    // Docker 部署：WebUI 等容器通过 bridge 网络直连（172.16.0.0/12）；
    // kind/k3s 下是 Pod 网段（10.244.x / 10.42.x）。RFC1918 私有网段全部放行（与 Family 一致）
    var isDockerNetwork = false;
    if (remoteIp != null)
    {
        try
        {
            isDockerNetwork =
                new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8).Contains(remoteIp) ||
                new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12).Contains(remoteIp) ||
                new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16).Contains(remoteIp);
        }
        catch { isDockerNetwork = false; }
    }
    if (remoteIp != null && (IPAddress.IsLoopback(remoteIp) || remoteIp.ToString() == "127.0.0.1" || remoteIp.ToString() == "::1" || isDockerNetwork))
    {
        await next();
        return;
    }

    logger.LogWarning("[AccessControl] Blocked non-loopback request to AI API from {RemoteIP}: {Path}",
        remoteIp?.ToString(), path);
    context.Response.StatusCode = 403;
    await context.Response.WriteAsJsonAsync(new { error = "AI Management API only allows local access." });
});

app.UseAuthorization();
app.MapControllers();

// 执行 AI 数据库迁移与 API Key 加密密钥迁移
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// 先迁移 API Key（不依赖 Migrate，Migrate 可能因表已存在而失败）
try
{
    using var scope = app.Services.CreateScope();
    var aiDb = scope.ServiceProvider.GetRequiredService<Baihua.Data.AIDbContext>();
    var migrationService = scope.ServiceProvider.GetRequiredService<MigrationService>();
    migrationService.MigrateApiKeysIfNeeded(aiDb);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "API Key 迁移失败（不影响启动）");
}

// 再执行 EF 数据库迁移
// 注意：必须先清空 SQLite 连接池——MigrateApiKeysIfNeeded 的查询会打开到 ai.db 的连接
// 且被连接池保留（物理连接仍持有读锁），此时 Migrate() 的 BEGIN EXCLUSIVE 会 SQLITE_BUSY 无限等待
// （AcquireDatabaseLock 自锁，进程卡死、端口不监听）
try
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    using var scope = app.Services.CreateScope();
    var aiDb = scope.ServiceProvider.GetRequiredService<Baihua.Data.AIDbContext>();
    aiDb.Database.Migrate();
    logger.LogInformation("AI 数据库迁移完成");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "AI 数据库迁移失败（不影响启动，表已存在则跳过）");
}
logger.LogInformation("===========================================");
logger.LogInformation("Baihua.AI Service Starting...");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Health: /health");
logger.LogInformation("===========================================");

try
{
    app.Run();
}
catch (Exception ex)
{
    try
    {
        var logger2 = app.Services.GetService<ILogger<Program>>();
        logger2?.LogCritical(ex, "Baihua.AI terminated unexpectedly");
    }
    catch { }
    throw;
}
