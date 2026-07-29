using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;
using Serilog;
using Baihua.Core;
using Baihua.Core.Notifications;
using Baihua.Core.Security;
using Baihua.Family.Services;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 显式设置监听地址，确保命令行 --urls 和环境变量 ASPNETCORE_URLS 覆盖 appsettings 默认值
var urls = builder.Configuration["urls"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? builder.Configuration["Kestrel:Endpoints:Http:Url"]
    ?? "http://0.0.0.0:8790";
builder.WebHost.UseUrls(urls);

// 百花统一数据根目录 BAIHUA_HOME 由 Core.Shared.BaihuaPaths 管理
// 已迁移至 BAIHUA_HOME，详见 services/TaskRunner.Contracts/BaihuaPaths.cs

// 添加控制器与 JSON 序列化
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TaskRunner Vault API",
        Version = "v1",
        Description = "百花知识库服务 - Vault、同步、搜索"
    });
});

// Vault 数据库上下文
builder.Services.AddDbContext<Baihua.Data.VaultDbContext>(options =>
{
    var dbPath = Baihua.Data.VaultDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("TaskRunner.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<Baihua.Data.VaultDbContext>(options =>
{
    var dbPath = Baihua.Data.VaultDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("TaskRunner.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Singleton);

// Family 和 AI 域数据库上下文（Core.Shared 中的 TaskManager/AiClientService 依赖）
builder.Services.AddDbContext<Baihua.Data.FamilyDbContext>(options =>
{
    var dbPath = Baihua.Data.FamilyDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;")
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<Baihua.Data.FamilyDbContext>(options =>
{
    var dbPath = Baihua.Data.FamilyDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;")
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Singleton);

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

// 核心知识库服务
builder.Services.AddSingleton<IVaultNameResolver, VaultNameResolver>();
builder.Services.AddSingleton<VaultSettingsService>();
builder.Services.AddSingleton<VaultNoteIndexer>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<ServerAddressService>();
builder.Services.AddSingleton<RequestSignatureService>();
builder.Services.AddSingleton<WebUINotificationService>();
builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<AiSettingsService>();
builder.Services.AddAiClientServices();
builder.Services.AddHostedService<VaultIndexSchedulerService>();

// 同步授权策略（家庭版）
builder.Services.AddSingleton<Baihua.Family.Services.Strategies.ISyncAuthorizationStrategy, Baihua.Family.Services.Strategies.FamilySyncAuthorizationStrategy>();

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
    .Enrich.WithProperty("Service", "TaskRunner.Vault")
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
builder.Logging.AddFilter("TaskRunner.Vault", LogLevel.Information);

// OpenTelemetry 导出到 OpenObserve
var openobserveEnabled = builder.Configuration.GetValue<bool?>("OpenObserve:Enabled") ?? true;
var openobserveUrl = builder.Configuration["OpenObserve:WebUrl"] ?? "";
var openobserveUser = builder.Configuration["OpenObserve:User"] ?? "";
var openobservePass = builder.Configuration["OpenObserve:Password"] ?? "";

{
    var otelBuilder = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("TaskRunner.Vault"));

    if (openobserveEnabled && !string.IsNullOrWhiteSpace(openobserveUrl))
    {
        var baseUrl = openobserveUrl.TrimEnd('/');
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{openobserveUser}:{openobservePass}"));
        var authHeader = $"Authorization=Basic {authValue}";
        var isDevelopment = builder.Environment.IsDevelopment();

        otelBuilder.WithMetrics(metrics =>
        {
            metrics.AddMeter("TaskRunner.AI")
                   .AddView("search.latency_ms", new OpenTelemetry.Metrics.ExplicitBucketHistogramConfiguration
                   {
                       Boundaries = new double[] { 0, 10, 25, 50, 100, 250, 500, 1000, 2500 }
                   })
                   .AddView("sync.operation_duration_ms", new OpenTelemetry.Metrics.ExplicitBucketHistogramConfiguration
                   {
                       Boundaries = new double[] { 0, 100, 500, 1000, 5000, 15000, 30000, 60000, 120000 }
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
                .AddSource("TaskRunner.Vault")
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

// API Key 加密保护（EmbeddingService 依赖）
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
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskRunner Vault API V1");
        c.RoutePrefix = "swagger";
    });
}

app.UseForwardedHeaders();
app.UseHealthChecks("/health");
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
    if (remoteIp != null && (IPAddress.IsLoopback(remoteIp) || remoteIp.ToString() == "127.0.0.1" || remoteIp.ToString() == "::1"))
    {
        await next();
        return;
    }

    logger.LogWarning("[AccessControl] Blocked non-loopback request to Vault API from {RemoteIP}: {Path}",
        remoteIp?.ToString(), path);
    context.Response.StatusCode = 403;
    await context.Response.WriteAsJsonAsync(new { error = "Vault API 仅允许本机访问。" });
});

app.UseAuthorization();
app.MapControllers();

// 执行核心数据库迁移
var logger = app.Services.GetRequiredService<ILogger<Program>>();
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Baihua.Data.VaultDbContext>();
    db.Database.Migrate();
    var conn = db.Database.GetDbConnection();
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info(Vaults)";
    var columns = new HashSet<string>();
    using (var reader = cmd.ExecuteReader())
    {
        while (reader.Read()) columns.Add(reader.GetString(1));
    }
    if (!columns.Contains("PushedByDeviceId"))
    {
        cmd.CommandText = "ALTER TABLE Vaults ADD COLUMN PushedByDeviceId TEXT NOT NULL DEFAULT ''";
        cmd.ExecuteNonQuery();
    }
    if (!columns.Contains("PushedByDeviceName"))
    {
        cmd.CommandText = "ALTER TABLE Vaults ADD COLUMN PushedByDeviceName TEXT NOT NULL DEFAULT ''";
        cmd.ExecuteNonQuery();
    }
    if (!columns.Contains("PushedAt"))
    {
        cmd.CommandText = "ALTER TABLE Vaults ADD COLUMN PushedAt TEXT NULL";
        cmd.ExecuteNonQuery();
    }
    conn.Close();
    logger.LogInformation("Vault 数据库迁移完成");
}
catch (Exception ex)
{
    logger.LogError(ex, "核心数据库迁移失败");
}

logger.LogInformation("===========================================");
logger.LogInformation("TaskRunner.Vault Service Starting...");
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
        logger2?.LogCritical(ex, "TaskRunner.Vault terminated unexpectedly");
    }
    catch { }
    throw;
}
