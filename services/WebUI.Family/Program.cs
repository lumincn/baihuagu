using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.DataProtection;

using Polly.Extensions.Http;
using Polly;

using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;
using WebUI.Logging;
using WebUI.Middleware;

// Initialize native SQLite provider early to avoid Microsoft.Data.Sqlite type initializer issues
try
{
    // Batteries_V2 is provided by SQLitePCLRaw.bundle_e_sqlite3 package
    SQLitePCL.Batteries_V2.Init();
}
catch
{
    // Ignore if initialization not required or fails; later errors will show up when opening DB
}

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeyPath = Path.Combine(AppContext.BaseDirectory, "data", "dp-keys");
Directory.CreateDirectory(dataProtectionKeyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("WebUI.Family");

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 根据环境设置日志级别
builder.Logging.SetMinimumLevel(builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// 减少第三方库的日志噪音
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);

// 添加内存错误日志收集器（记录 Warning/Error/Critical，供健康检查页查看）
builder.Logging.AddErrorLogCollector(LogLevel.Warning);

var openobserveEnabled = builder.Configuration.GetValue<bool?>("OpenObserve:Enabled") ?? true;
var openobserveUrl = builder.Configuration["OpenObserve:WebUrl"] ?? "";
var openobserveUser = builder.Configuration["OpenObserve:User"] ?? "";
var openobservePass = builder.Configuration["OpenObserve:Password"] ?? "";

// Diagnostic: print OpenObserve config to console to help debug startup URI issues
Console.Error.WriteLine($"[WebUI] OpenObserve.Enabled={openobserveEnabled}, WebUrl='{openobserveUrl}'");

// Serilog 仅用于控制台结构化输出
var serilogConfig = new Serilog.LoggerConfiguration()
    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
    .Enrich.WithProperty("Service", "WebUI")
    .Filter.ByExcluding(e => e.Properties.ContainsKey("SourceContext") &&
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.AspNetCore") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"System.Net.Http") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.AspNetCore.SignalR"))
    .WriteTo.Console();

builder.Logging.AddSerilog(serilogConfig.CreateLogger(), dispose: true);

// 设置关闭超时时间为 5 秒（默认 30 秒太长）
builder.Host.ConfigureHostOptions(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(5);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
        options.DisconnectedCircuitMaxRetained = 100;
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
        options.MaxBufferedUnacknowledgedRenderBatches = 10;
    });

// 添加 API Controller 支持（用于请求指标等端点）
builder.Services.AddControllers();

// Configure SignalR for better stability
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // 开发机本地电路握手不宜过长，否则异常网络下首屏长时间无响应
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 102400; // 100KB
});

// Add API service
builder.Services.AddSingleton<WebUI.Services.IApiService, WebUI.Services.ApiService>();
builder.Services.AddSingleton<TaskRunner.Contracts.Health.ITaskRunnerHealthApi>(sp => sp.GetRequiredService<WebUI.Services.IApiService>());

// Add Settings service
builder.Services.AddSingleton<WebUI.Services.SettingsService>();

// Add SignalR Status Update service
builder.Services.AddSingleton<WebUI.Hubs.StatusUpdateService>();

// Add Authentication service (must be scoped for per-user state)
builder.Services.AddSingleton<WebUI.Services.AuthService>();
builder.Services.AddScoped<WebUI.Services.UserTypeService>();

// Add AI Status service
builder.Services.AddSingleton<WebUI.Services.AIStatusService>(sp => 
    new WebUI.Services.AIStatusService(
        sp.GetRequiredService<WebUI.Services.IApiService>()));

// Add Temporary Storage service
builder.Services.AddSingleton<WebUI.Services.TemporaryStorageService>();

// Add Recent Notes service
builder.Services.AddSingleton<WebUI.Services.IRecentNotesService, WebUI.Services.RecentNotesService>();

// Add Search History service
builder.Services.AddSingleton<WebUI.Services.ISearchHistoryService, WebUI.Services.SearchHistoryService>();

// Add Favorites service
builder.Services.AddSingleton<WebUI.Services.IFavoritesService, WebUI.Services.FavoritesService>();

// Add User Preferences service
builder.Services.AddSingleton<WebUI.Services.IUserPreferencesService, WebUI.Services.UserPreferencesService>();

// Add Search State service (for preserving search results across navigation)
builder.Services.AddSingleton<WebUI.Services.SearchStateService>();

// Add Vaults service (for managing multiple vaults)
builder.Services.AddScoped<WebUI.Services.VaultsService>();

// Add Backup service
builder.Services.AddScoped<WebUI.Services.BackupService>();

// Add Vault Status service (Singleton 确保所有组件共享同一实例和事件)
builder.Services.AddSingleton<WebUI.Services.VaultStatusService>();

// Add Global State service (Scoped，绑定到用户会话，通过 SignalR 实时更新)
builder.Services.AddScoped<WebUI.Services.GlobalStateService>();

// Add Simple Status service (简单状态服务，直接从API获取)
builder.Services.AddScoped<WebUI.Services.SimpleStatusService>();

// OpenTelemetry Metrics 导出到 OpenObserve（仅在明确启用时配置 exporter）
// 使用与 TaskRunner 相同的安全保护：先构建基础的 OpenTelemetry builder，若未启用或 WebUrl 为空则直接返回，不配置导出器。
{
    var otelBuilder = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("WebUI"));

    if (openobserveEnabled && !string.IsNullOrWhiteSpace(openobserveUrl))
    {
        var baseUrl = openobserveUrl.TrimEnd('/');
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{openobserveUser}:{openobservePass}"));
        var authHeader = $"Authorization=Basic {authValue}";
        var isDevelopment = builder.Environment.IsDevelopment();

        otelBuilder.WithMetrics(metrics =>
        {
            metrics.AddMeter("TaskRunner.WebUI")
                   .AddView("http.request.duration_ms", new ExplicitBucketHistogramConfiguration
                   {
                       Boundaries = new double[] { 0, 10, 25, 50, 100, 200, 500, 1000, 2000, 5000, 10000 }
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
                .AddSource("WebUI")
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

// Add Request Metrics service (WebUI incoming requests)
builder.Services.AddSingleton<WebUI.Services.RequestMetricsService>();

// Add API Call Metrics service (WebUI → TaskRunner calls)
builder.Services.AddSingleton<WebUI.Services.ApiCallMetricsService>();

// Add End-to-End Performance Monitoring service
builder.Services.AddSingleton<WebUI.Services.EndToEndPerformanceService>();

// Add Component Performance Monitoring service
builder.Services.AddSingleton<WebUI.Services.ComponentPerformanceService>();

// Add Error Log service (内存中保留最近的错误日志)
builder.Services.AddSingleton<WebUI.Services.ErrorLogService>();

// Add Obsidian Status service
builder.Services.AddScoped<WebUI.Services.ObsidianStatusService>();

// Add Devices service (for device authorization management)
builder.Services.AddScoped<WebUI.Services.DevicesService>();

// Add Pairing service (for QR code pairing)

// Add Onboarding service (for first-time setup and initialization tasks)
builder.Services.AddScoped<WebUI.Services.OnboardingService>();
builder.Services.AddSingleton<WebUI.Services.CapabilityService>();

// Add HttpClient with API base address + Polly retry
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(1.5, retryAttempt - 1)));

var taskRunnerBaseUrl = builder.Configuration["TaskRunnerApi:BaseUrl"] ?? "http://127.0.0.1:8788/";
builder.Services.AddHttpClient("TaskRunnerApi", client =>
{
    client.BaseAddress = new Uri(taskRunnerBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddPolicyHandler(retryPolicy);

var taskRunnerAiBaseUrl = builder.Configuration["TaskRunnerAiApi:BaseUrl"] ?? "http://127.0.0.1:8791/";
builder.Services.AddHttpClient("TaskRunnerAiApi", client =>
{
    client.BaseAddress = new Uri(taskRunnerAiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddPolicyHandler(retryPolicy);

var taskRunnerVaultBaseUrl = builder.Configuration["TaskRunnerVaultApi:BaseUrl"] ?? "http://127.0.0.1:8790/";
builder.Services.AddHttpClient("TaskRunnerVaultApi", client =>
{
    client.BaseAddress = new Uri(taskRunnerVaultBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddPolicyHandler(retryPolicy);

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("TaskRunnerApi"));

// Add HttpContextAccessor for accessing HttpContext in Blazor components
builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    // API 路由限流（只限 /api/*），不影响 Blazor SignalR / 静态文件
    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1)
            }));
});

WebApplication app;
try
{
    app = builder.Build();
}
catch (Exception ex)
{
    // Make sure the startup exception is visible in console/logs for easier debugging
    Console.Error.WriteLine("[WebUI] Startup build failed: " + ex.ToString());
    throw;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// 支持通过子路径部署（如 /admin/）
var basePath = builder.Configuration.GetValue<string>("BasePath") ?? "/";
if (basePath != "/")
{
    app.UsePathBase(basePath);
}

app.UseRouting();
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();

// 请求关联ID中间件（最早阶段添加，确保所有日志都有 CorrelationId）
app.UseCorrelationId();

// 请求统计中间件（在 CorrelationId 之后）
app.UseRequestMetrics();

app.UseWebUIAuthentication();

app.MapRazorComponents<WebUI.Components.App>()
    .AddInteractiveServerRenderMode();

// Map API Controllers
app.MapControllers();

// Map SignalR Hub
app.MapHub<WebUI.Hubs.StatusHub>("/hubs/status");

// CLI 一次性令牌端点：仅供本机 loopback 调用，用于命令行一键授权
app.MapPost("/api/auth/cli-token", (HttpContext context, WebUI.Services.AuthService authService) =>
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp != null && !System.Net.IPAddress.IsLoopback(remoteIp))
    {
        return Results.Json(new { error = "Forbidden", message = "CLI token 仅允许本机请求" }, statusCode: 403);
    }
    var token = authService.GenerateCliToken();
    return Results.Ok(new { token });
});

app.MapPost("/api/auth/logout", (HttpContext context, WebUI.Services.AuthService authService) =>
{
    var cookieVal = context.Request.Cookies[WebUI.Services.AuthService.AuthCookieName];
    if (!string.IsNullOrEmpty(cookieVal))
    {
        authService.RevokeToken(cookieVal);
    }
    context.Response.Cookies.Delete(WebUI.Services.AuthService.AuthCookieName, new CookieOptions { Path = "/" });
    return Results.Ok(new { success = true });
});

// 请求统计 API
app.MapGet("/api/metrics/summary", (WebUI.Services.RequestMetricsService metrics) =>
{
    var summary = metrics.GetSummary();
    return Results.Ok(summary);
});

app.MapGet("/api/metrics/slowest", (WebUI.Services.RequestMetricsService metrics, int count = 10) =>
{
    var requests = metrics.GetSlowestRequests(count);
    return Results.Ok(requests);
});

app.MapGet("/api/metrics/frequent", (WebUI.Services.RequestMetricsService metrics, int count = 10) =>
{
    var paths = metrics.GetMostFrequentPaths(count);
    return Results.Ok(paths);
});

app.MapGet("/api/metrics/errors", (WebUI.Services.RequestMetricsService metrics, int count = 10) =>
{
    var errors = metrics.GetRecentErrors(count);
    return Results.Ok(errors);
});

app.MapPost("/api/metrics/clear", (WebUI.Services.RequestMetricsService metrics) =>
{
    metrics.Clear();
    return Results.Ok(new { message = "统计数据已清空" });
});

// 内部通知回调：供 TaskRunner 在状态变化时主动推送
// 仅允许 loopback 访问，防止外部滥用
app.MapPost("/api/internal/notify-state-change", (HttpContext context, WebUI.Hubs.StatusUpdateService status, [Microsoft.AspNetCore.Mvc.FromBody] NotifyStateChangeRequest request) =>
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp != null && !System.Net.IPAddress.IsLoopback(remoteIp))
    {
        return Results.StatusCode(403);
    }
    if (request?.Type == "ai")
    {
        _ = status.NotifyAIStatusChangedAsync();
    }
    else if (request?.Type == "vault")
    {
        _ = status.NotifyVaultStatusChangedAsync();
    }
    return Results.Ok();
});

// Global exception handlers for better observability
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        var exLogger = app.Services.GetService<ILogger<Program>>();
        exLogger?.LogCritical(ex, "Unhandled domain exception occurred");
    }
    catch { }
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    try
    {
        var exLogger = app.Services.GetService<ILogger<Program>>();
        exLogger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
    catch { }
};

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("========================================");
logger.LogInformation("WebUI Service Starting...");
logger.LogInformation("PID: {ProcessId}", Environment.ProcessId);
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("========================================");

// Graceful shutdown logging
var cts = new CancellationTokenSource();
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("WebUI Service Stopping...");
    // 5秒后强制取消，确保快速退出
    cts.CancelAfter(TimeSpan.FromSeconds(5));
});

app.Lifetime.ApplicationStopped.Register(() =>
{
    logger.LogInformation("WebUI Service Stopped");
    cts.Dispose();
});

try
{
    app.Run();
}
catch (Exception ex)
{
    // Ensure unhandled startup/run exceptions are visible in console/logs
    try { var exLogger = app?.Services?.GetService<ILogger<Program>>(); exLogger?.LogCritical(ex, "Unhandled exception in WebUI Run"); } catch { }
    Console.Error.WriteLine("[WebUI] Unhandled exception: " + ex.ToString());
    throw;
}

public class NotifyStateChangeRequest
{
    public string Type { get; set; } = string.Empty;
}

