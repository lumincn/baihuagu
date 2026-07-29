using Baihua.Core;
using Baihua.Core.Security;
using Baihua.Family.Services;
using Baihua.Core.Notifications;
using Baihua.Data;

using Baihua.Core.Hubs;
using Baihua.Family.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading;
using System.Threading.RateLimiting;
using System.Security.Cryptography;
using Serilog;
using Serilog.Events;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Resources;
using Baihua.Family.OpenTelemetry;
using Baihua.Contracts.Metrics;
using Baihua.Family.Middleware;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

// Initialize native SQLite provider early to avoid Microsoft.Data.Sqlite type initializer issues
try
{
    SQLitePCL.Batteries_V2.Init();
}
catch
{
    // Ignore if initialization not required or fails; later errors will show up when opening DB
}

var builder = WebApplication.CreateBuilder(args);



// Prevent multiple instances from running and binding the same ports
// (skip in test environment)
Mutex? _singleInstanceMutex = null;
var skipMutex = builder.Configuration.GetValue<bool>("TASKRUNNER_SKIP_MUTEX", false);
if (!skipMutex)
{
    var mutexName = "TaskRunner_Service_Mutex";
    var createdNew = false;
    try
    {
        _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[FATAL] Mutex 创建失败: {ex.Message}");
    }

    if (!createdNew)
    {
        Console.WriteLine("Another TaskRunner instance is already running. Exiting to avoid port conflicts.");
        return;
    }
}

// 配置加载顺序（由 WebApplication.CreateBuilder 默认处理）：
//   1. appsettings.json
//   2. appsettings.{Environment}.json
//   3. User Secrets（仅 Development 环境）
//   4. 环境变量（使用 __ 双下划线表示层级，如 OpenObserve__WebUrl）
//   5. 命令行参数
// 环境变量优先级最高，适合覆盖部署时的配置值（密码、URL 等）。
// 无需额外调用 AddEnvironmentVariables()，CreateBuilder 已默认加载。

// 百花统一数据根目录 BAIHUA_HOME 由 Core.Shared.BaihuaPaths 管理
// 已迁移至 BAIHUA_HOME，详见 services/Baihua.Contracts/BaihuaPaths.cs

// Family 版不自动生成分享密钥：未配置时回退到 Bearer Token / IP 白名单验证
// 仅在显式配置了 MobileAuth:SharedSecret 时才启用 HMAC 签名
var mobileAuthSecret = builder.Configuration["MobileAuth:SharedSecret"];
if (!string.IsNullOrEmpty(mobileAuthSecret))
{
}

// Add Localization services (i18n, default: zh-CN)
builder.Services.AddLocalization();

// 添加服务 - 配置 JSON 序列化不转义中文
builder.Services.AddControllers(options =>
{
    // 添加全局异常过滤器
    options.Filters.Add<Baihua.Family.Filters.GlobalExceptionFilter>();
})
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Task Runner API",
        Version = "v1",
        Description = "百花后台服务 - 健康检查与运行状态 API"
    });
});

// 添加 SignalR，配置 JSON 序列化（枚举保持数字格式）
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null; // 保持 PascalCase
    });

// 注册核心服务
// Family 数据库上下文
builder.Services.AddDbContext<Baihua.Data.FamilyDbContext>(options =>
{
    var dbPath = Baihua.Data.FamilyDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("Baihua.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<Baihua.Data.FamilyDbContext>(options =>
{
    var dbPath = Baihua.Data.FamilyDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("Baihua.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Singleton);

// Vault 数据库上下文（Family 需要读取知识库信息）
builder.Services.AddDbContext<Baihua.Data.VaultDbContext>(options =>
{
    var dbPath = Baihua.Data.VaultDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("Baihua.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<Baihua.Data.VaultDbContext>(options =>
{
    var dbPath = Baihua.Data.VaultDbContext.GetDbPath();
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("Baihua.Data"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}, ServiceLifetime.Singleton);

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

builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<AiSettingsService>();
builder.Services.AddSingleton<AiConfigService>();

builder.Services.AddSingleton<LocalModelSettingsService>();

builder.Services.AddSingleton<IVaultNameResolver, VaultNameResolver>();
builder.Services.AddSingleton<VaultSettingsService>();
builder.Services.AddHostedService<StartupOrchestratorHostedService>();
builder.Services.AddSingleton<DefaultPromptProvider>();
builder.Services.AddAiClientServices();
builder.Services.AddSingleton<AiFunctionService>();

builder.Services.AddSingleton<NoteParser>();
builder.Services.AddSingleton<CardRepository>();
builder.Services.AddSingleton<AtomNoteSplitter>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<VaultNoteIndexer>();
builder.Services.AddSingleton<RagService>();
        builder.Services.AddSingleton<ChatMemoryService>();
        builder.Services.AddSingleton<MasterPromptBuilder>();
        builder.Services.AddHostedService<MasterDataRetentionService>();
builder.Services.AddSingleton<AnkiCardGenerator>();
builder.Services.AddSingleton<DailyCardService>();
builder.Services.AddSingleton<LearnerService>();
builder.Services.AddSingleton<AchievementEngine>();
builder.Services.AddSingleton<LeaderboardService>();
builder.Services.AddHostedService<StudyRecordMigrationService>();
builder.Services.AddSingleton<Baihua.Core.WebSocket.DeviceWebSocketHub>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<PairingService>();

// Family 版固定使用 Family 配对和同步授权策略
builder.Services.AddSingleton<Baihua.Family.Services.Strategies.IPairingStrategy, Baihua.Family.Services.Strategies.FamilyPairingStrategy>();
builder.Services.AddSingleton<Baihua.Family.Services.Strategies.ISyncAuthorizationStrategy, Baihua.Family.Services.Strategies.FamilySyncAuthorizationStrategy>();
builder.Services.AddSingleton<ServerAddressService>();
builder.Services.AddSingleton<WebUINotificationService>();
builder.Services.AddSingleton<RequestSignatureService>();

// MobileContract 接口适配器
// 移动端接口
builder.Services.AddSingleton<MobileContract.Services.IPairingService, Baihua.Family.Services.Adapters.MobileDeviceServiceAdapter>();
// 管理后台接口
builder.Services.AddSingleton<MobileContract.Admin.IDeviceAdminService, Baihua.Family.Services.Adapters.MobileDeviceServiceAdapter>();
builder.Services.AddSingleton<MobileContract.Admin.IPushAdminService, Baihua.Family.Services.Adapters.MobileDeviceServiceAdapter>();
builder.Services.AddSingleton<MobileContract.Admin.IOneHopAdminService, Baihua.Family.Services.Adapters.OneHopAdminServiceAdapter>();

// 注册OneHop服务（基于TCP的局域网设备连接）
builder.Services.AddSingleton<IOneHopService, OneHopService>();
builder.Services.AddSingleton<OneHopManager>();

// 注册mDNS服务（标准DNS-SD协议，供移动端发现）
builder.Services.AddSingleton<MDnsService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MDnsService>());

// 注册 AI 配置服务（Data Protection + SQLite）
builder.Services.AddDataProtection();
builder.Services.AddSingleton<Baihua.Core.Security.ApiKeyProtectionService>();
builder.Services.AddSingleton<Baihua.Core.Security.DataEncryptionService>();

builder.Services.AddSingleton<Baihua.Family.Services.RestoreService>();
builder.Services.AddSingleton<Baihua.Family.Services.BackupService>();
builder.Services.AddSingleton<Baihua.Family.Services.NotesMdCliService>();

// 注册全局异常过滤器
builder.Services.AddScoped<Baihua.Family.Filters.GlobalExceptionFilter>();

// 注册系统健康检查服务

// 添加 HttpClientFactory
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("WebUI", c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient("OllamaLibrary", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("SystemHealth", c =>
{
    c.Timeout = TimeSpan.FromSeconds(1);
    c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TaskRunner-Health/1.0");
});

// 添加内存缓存（用于本地模型页等高频查询）
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<SystemHealthService>();
builder.Services.AddSingleton<HardwareInfoService>();
builder.Services.AddSingleton<CapabilityService>();
builder.Services.AddSingleton<OllamaLibraryClient>();
builder.Services.AddSingleton<ModelRecommendationEngine>();
builder.Services.AddSingleton<OllamaService>();
builder.Services.AddSingleton<LmStudioDownloadService>();
builder.Services.AddSingleton<LmStudioService>();
builder.Services.AddSingleton<LlamaCppService>();
builder.Services.AddSingleton<LocalModelDeploymentService>();
builder.Services.AddSingleton<AiMetricsService>();
builder.Services.AddSingleton<BenchmarkRepository>();
builder.Services.AddSingleton<ModelBenchmarkService>();
builder.Services.AddSingleton<OpenClawConfigService>();
builder.Services.AddSingleton<ILocalAiConfigService, LocalAiConfigService>();
builder.Services.AddSingleton<IOpenClawModelProfileService, OpenClawModelProfileService>();
builder.Services.AddSingleton<IOpenClawTaskService, OpenClawTaskService>();
builder.Services.AddSingleton<McpServerService>();

// API 限流（配对码防暴力破解）
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("pair", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1)
            }));
});

// 注册后台服务
builder.Services.AddHostedService<TaskCleanupService>();
builder.Services.AddHostedService<ObsidianWarmupHostedService>();
builder.Services.AddHostedService<OneHopManager>();
// VaultIndexSchedulerService 已在 TaskRunner.Vault 中注册，避免两个进程同时重建索引
builder.Services.AddHostedService<BackupSchedulerService>();
builder.Services.AddHostedService<LocalModelsCacheWarmupService>();

// 添加健康检查
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// 配置 CORS（支持 WebSocket，但限制为本地/内网来源）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                // 允许 localhost / 127.0.0.1 / ::1（Family 版内网部署）
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    if (uri.Host is "localhost" or "127.0.0.1" or "::1")
                        return true;
                    // 也允许局域网 IP（192.168.x.x, 10.x.x.x, 172.16-31.x.x）
                    // 移动端原生 WebSocket 客户端有时会发送 Origin 头
                    if (IsPrivateIpAddress(uri.Host))
                        return true;
                }
                // 无 Origin 头的原生请求（如 ArkTS webSocket）也放行
                return string.IsNullOrEmpty(origin);
            })
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();  // 允许携带凭证（SignalR 需要）
    });
});

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 结构化JSON Lines文件日志（所有类别共享Writer，异步批量写入，避免多Writer冲突）
var logsDir = Path.Combine(builder.Environment.ContentRootPath ?? AppContext.BaseDirectory, "logs");
var fileLogMinLevel = builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information;
builder.Logging.AddProvider(new Baihua.Family.Logging.JsonLineLoggerProvider(
    logsDir, "taskrunner", retentionDays: 7,
    globalMinimumLevel: fileLogMinLevel,
    categoryFilters: new Dictionary<string, LogLevel>
    {
        { "Microsoft.AspNetCore", LogLevel.Warning },
        { "System.Net.Http", LogLevel.Warning },
        { "Microsoft.EntityFrameworkCore", LogLevel.Warning },
        { "Microsoft.Extensions.Http", LogLevel.Warning },
        { "TaskRunner", LogLevel.Information },
    }));

// 配置日志级别（生产环境减少噪音）
builder.Logging.SetMinimumLevel(builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// 针对特定类别的日志级别调整
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);      // 减少 ASP.NET 内部日志
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);           // 减少 HTTP 客户端日志
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning); // 减少数据库日志
builder.Logging.AddFilter("TaskRunner", LogLevel.Information);            // 确保 TaskRunner 命名空间的日志可见

// OpenObserve 结构化日志：通过 OpenTelemetry OTLP 导出到 OpenObserve
// 先创建配置实例（DI 容器尚未 Build，无法注入），后续注册为 Singleton
var logSinkConfig = new LogSinkConfigService(
    Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<LogSinkConfigService>());
var ooConfig = logSinkConfig.GetConfig();

// 兼容旧环境变量/配置
var envUrl = builder.Configuration["OpenObserve:WebUrl"];
if (!string.IsNullOrEmpty(envUrl)) ooConfig.WebUrl = envUrl;
var envUser = builder.Configuration["OpenObserve:User"];
if (!string.IsNullOrEmpty(envUser)) ooConfig.User = envUser;
var envPass = builder.Configuration["OpenObserve:Password"];
if (!string.IsNullOrEmpty(envPass)) ooConfig.Password = envPass;

// Serilog 仅用于控制台结构化输出
var serilogConfig = new Serilog.LoggerConfiguration()
    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
    .Enrich.WithProperty("Service", "TaskRunner")
    .Filter.ByExcluding(e => e.Properties.ContainsKey("SourceContext") &&
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.AspNetCore") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"System.Net.Http") ||
        ((Serilog.Events.LogEventPropertyValue)e.Properties["SourceContext"]).ToString()
            .StartsWith("\"Microsoft.EntityFrameworkCore"))
    .WriteTo.Console();

builder.Logging.AddSerilog(serilogConfig.CreateLogger(), dispose: true);

// 注册 LogSinkConfigService 为 Singleton
builder.Services.AddSingleton<LogSinkConfigService>(logSinkConfig);

var openobserveEnabled = builder.Configuration.GetValue<bool?>("OpenObserve:Enabled") ?? true;
var ooBaseUrl = string.IsNullOrWhiteSpace(ooConfig.WebUrl) ? "http://localhost:5082" : ooConfig.WebUrl.TrimEnd('/');

// 注册业务指标（单例，全局共享）
builder.Services.AddSingleton<ServiceMetrics>();

// 配置 OpenTelemetry（Metrics + Logs + Traces），通过 OTLP 推送到 OpenObserve（受 openobserveEnabled 控制）
builder.Services.AddOpenObserveTelemetry(
    serviceName: "TaskRunner",
    meterNames: new[] { AiMetricsService.MeterName, ServiceMetrics.MeterName },
    webUrl: ooBaseUrl,
    user: ooConfig.User,
    password: ooConfig.Password,
    enabled: openobserveEnabled,
    environmentName: builder.Environment.EnvironmentName
);



// 配置反向代理头部转发（支持 nginx 等反向代理）
// 仅信任来自 loopback 的代理头，防止客户端伪造 X-Forwarded-For 绕过访问控制
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    // 显式添加受信任的代理：loopback（nginx 通常与后端在同一主机）
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

// 启动编排由 StartupOrchestratorHostedService 在应用启动时自动执行

// 中间件管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Task Runner API V1");
        c.RoutePrefix = "swagger";
    });
}

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseServiceMetrics();
app.UseHealthChecks("/health");
app.UseRateLimiter();
app.UseCors("AllowAll");

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("zh-CN"),
    SupportedCultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") },
    SupportedUICultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") }
});

// 移动端请求签名验证中间件
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var signatureService = context.RequestServices.GetService<RequestSignatureService>();

    // 只对移动端公开 API 端点进行签名验证
    var mobileApiPaths = new[]
    {
        "/vault/manifest", "/vault/file", "/vault/file_chunk",
        "/api/vaults", "/vault/pair", "/pair",
        "/api/sync/notes", "/api/sync/system", "/api/sync",

        "/mobile-vaults/push",
        // MobileGateway 风格路径别名
        "/mg/manifest", "/mg/file", "/mg/cards",
        "/mg/vaults", "/mg/pair"
    };

    // 以下路径为公开路径，无需 HMAC 签名（设备注册、密钥获取等初始化流程）
    var publicApiPaths = new[]
    {
        "/api/onehop/register-device",
        "/mg/onehop/register-device",
        "/mg/auth/config"
    };

    // WebUI 专用浏览 API 不需要移动端签名
    var isWebUiBrowse = path.Contains("/browse");

    var isPublicPath = publicApiPaths.Any(p => path.StartsWith(p));

    if (signatureService != null &&
        mobileApiPaths.Any(p => path.StartsWith(p)) &&
        !isWebUiBrowse &&
        !isPublicPath)
    {
        logger.LogInformation("[SignatureDebug] path={Path} isConfigured={IsConfigured}", path, signatureService.IsConfigured);

        // 读取请求体
        string? body = null;
        if (context.Request.ContentLength > 0 &&
            (context.Request.Method == "POST" || context.Request.Method == "PUT" || context.Request.Method == "PATCH"))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        if (signatureService.IsConfigured)
        {
            // Cloud 版：使用 HMAC 签名验证
            var signatureHeader = context.Request.Headers["X-Mobile-Signature"].FirstOrDefault();
            if (!signatureService.VerifySignature(context.Request.Method, context.Request.Path + context.Request.QueryString, body, signatureHeader))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "无效的请求签名" });
                return;
            }
        }
        else
        {
            // Family 版：未配置 sharedSecret 时，回退到 Bearer Token 验证
            // 已授权设备的 IP 直接放行（与 FamilySyncAuthorizationStrategy 保持一致）
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            var deviceService = context.RequestServices.GetService<DeviceService>();
            var isAuthorizedIp = !string.IsNullOrEmpty(remoteIp) && deviceService != null &&
                deviceService.GetAuthorizedDevices().Any(d => remoteIp.Equals(d.IpAddress, StringComparison.OrdinalIgnoreCase));

            if (isAuthorizedIp)
            {
                logger.LogInformation("[Signature] Allowing authorized device from IP: {RemoteIP} for {Path}", remoteIp, path);
            }
            else
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "需要 Bearer Token 认证" });
                    return;
                }
                var token = authHeader.Substring("Bearer ".Length).Trim();
                if (deviceService == null || !deviceService.ValidateAccessToken(token))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "无效的访问令牌" });
                    return;
                }
            }
        }
    }

    await next();
});

// 访问控制中间件（密码机制已移除，仅保留 loopback 检查和公开路径）
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    // 使用 ForwardedHeadersMiddleware 处理后的 RemoteIpAddress
    // 不再自行解析 X-Forwarded-For（防止客户端伪造 IP 绕过 loopback 限制）
    var remoteIp = context.Connection.RemoteIpAddress;

    // 公开端点：移动端同步、配对等只读服务
    var publicPaths = new[]
    {
        "/health", "/api/health", "/swagger",
        "/mcp",
        "/vault/manifest", "/vault/file", "/vault/file_chunk",
        "/api/vaults", "/vault/pair", "/pair",
        "/api/sync/notes", "/api/sync/system", "/api/sync",
        "/api/onehop/register-device",
        "/api/discovery", "/mg/discovery",
        "/mobile-vaults/push",
        "/mg/vaults", "/mg/manifest", "/mg/file", "/mg/cards",
        "/mg/pair", "/mg/pair/check",
        "/mg/onehop/register-device",
        "/mg/auth/config", "/mg/verify-token",
    };

    if (publicPaths.Any(p => path.StartsWith(p)))
    {
        logger.LogInformation("[AccessControl] Allowing public path: {Path}, RemoteIP: {RemoteIP}", path, remoteIp?.ToString());
        await next();
        return;
    }

    logger.LogInformation("[AccessControl] Path: {Path}, RemoteIP: {RemoteIP}, IsLoopback: {IsLoopback}",
        path, remoteIp?.ToString(), remoteIp != null && IPAddress.IsLoopback(remoteIp));

    // 非公开路径仅允许本机访问（WebUI 通过 loopback 调用 TaskRunner）
    if (remoteIp != null && (IPAddress.IsLoopback(remoteIp) || remoteIp.ToString() == "127.0.0.1" || remoteIp.ToString() == "::1"))
    {
        logger.LogInformation("[AccessControl] Allowing local request for path: {Path}", path);
        await next();
        return;
    }

    // 非本机访问非公开端点 → 拒绝
    context.Response.StatusCode = 403;
    await context.Response.WriteAsJsonAsync(new
    {
        error = "管理 API 仅允许本机访问。请通过 WebUI 界面操作。"
    });
});

app.UseAuthorization();

// 将移动端同步 API 转发到 TaskRunner.Vault（8790）
// VaultController/SyncController 已迁移到独立服务，但移动端仍通过 8788 发现服务器
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    // 精确匹配已迁移到 Vault 服务的移动端 API 路径
    var vaultPaths = new[]
    {
        "/mg/manifest", "/mg/file", "/mg/file_chunk", "/mg/cards",
        "/mg/vaults", "/mg/auth/config", "/mg/verify-token", "/mg/note-count",
        "/api/sync/",
        "/vault/manifest", "/vault/file", "/vault/file_chunk",
        "/mobile-vaults/push"
    };
    if (vaultPaths.Any(p => path.StartsWith(p)))
    {
        var vaultBase = Environment.GetEnvironmentVariable("TASKRUNNER_VAULT_URL") ?? "http://127.0.0.1:8790";
        var targetUrl = vaultBase.TrimEnd('/') + path + context.Request.QueryString;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

            // 复制请求头（跳过 Host/Content-Length 以及含非 ASCII 字符的头）
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = header.Value.ToArray();
                // HttpClient 要求请求头仅含 ASCII 字符，否则抛出 HttpRequestException
                if (values.Any(v => v != null && v.Any(c => c > 127)))
                    continue;
                request.Headers.TryAddWithoutValidation(header.Key, values);
            }

            // 为 Vault 的 FamilySyncAuthorizationStrategy 附加 Bearer Token：
            // Family 已完成 HMAC 签名验证，按来源 IP 找到已授权设备，把其 AccessToken 转发给 Vault。
            if (!request.Headers.Contains("Authorization"))
            {
                try
                {
                    var deviceService = context.RequestServices.GetService<DeviceService>();
                    var remoteIp = context.Connection.RemoteIpAddress?.ToString();
                    if (!string.IsNullOrEmpty(remoteIp) && deviceService != null)
                    {
                        var authorizedDevice = deviceService.GetAuthorizedDevices()
                            .FirstOrDefault(d => remoteIp.Equals(d.IpAddress, StringComparison.OrdinalIgnoreCase));
                        if (authorizedDevice != null && !string.IsNullOrEmpty(authorizedDevice.AccessToken))
                        {
                            request.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizedDevice.AccessToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var logger = context.RequestServices.GetService<ILogger<Program>>();
                    logger?.LogWarning(ex, "为 Vault 转发请求附加 Bearer Token 失败");
                }
            }

            // 复制请求体
            if (context.Request.ContentLength > 0 ||
                (context.Request.Headers.ContentLength.HasValue && context.Request.Headers.ContentLength.Value > 0))
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.Headers.ContentType.Any())
                {
                    request.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.Headers.ContentType!);
                }
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers)
            {
                // Transfer-Encoding 由 Kestrel 自行管理，复制上游值会导致重复 chunked 编码
                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body);
            return;
        }
        catch (HttpRequestException ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "转发移动端请求到 Vault 服务失败: {TargetUrl}", targetUrl);
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = "Vault 服务不可用" });
            return;
        }
    }

    await next();
});

app.MapControllers();


// 根路径健康检查（快速响应，供外部探活使用）
app.MapGet("/health", (Baihua.Family.Services.ServerAddressService sas) =>
{
    var settings = sas.GetSettings();
    return Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow.ToString("o"),
        message = "Task Runner Service is running",
        serverId = settings.ServerInstanceId,
        serverName = settings.DisplayName
    });
});

app.MapHub<TaskProgressHub>("/hubs/task-progress");
app.MapHub<DeviceHub>("/hubs/devices");

// 纯 WebSocket 端点（供移动端使用，无需 SignalR 协议）
app.Map("/ws/devices", async (HttpContext context, Baihua.Core.WebSocket.DeviceWebSocketHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var deviceName = context.Request.Query["deviceName"].ToString();
    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.AcceptAsync(webSocket, deviceName);
});

// 启动信息
var host = app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// 全局未捕获异常与未观察到的任务异常处理，以提高可观测性
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        logger.LogCritical(ex, "Unhandled domain exception occurred");
    }
    catch { /* 日志记录器本身也可能失效，静默兜底 */ }
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    try
    {
        logger.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
    catch { /* 日志记录器本身也可能失效，静默兜底 */ }
};

// 记录启动
var startupMonitor = Baihua.Family.Services.StartupMonitor.Instance;
startupMonitor.RecordStartup();

logger.LogInformation("===========================================");
logger.LogInformation("Task Runner Service Starting...");
logger.LogInformation("启动时间：{StartTime}", startupMonitor.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
logger.LogInformation("PID: {ProcessId}", Environment.ProcessId);
logger.LogInformation("Content Root: {ContentRoot}", host);
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
var listenUrlForLog = ResolveConfiguredListenUrl(app.Configuration);
var displayBaseUrl = ToDisplayBaseUrlForLogs(listenUrlForLog);
logger.LogInformation("Swagger UI: {BaseUrl}/swagger", displayBaseUrl);
logger.LogInformation("API: {BaseUrl}/api/tasks", displayBaseUrl);
logger.LogInformation("Health: {BaseUrl}/health", displayBaseUrl);
logger.LogInformation("Full Health: {BaseUrl}/api/health/full", displayBaseUrl);
logger.LogInformation("Component Check: {BaseUrl}/api/health/check/{{component}}", displayBaseUrl);
logger.LogInformation("监听开始后将在后台执行自检与 Obsidian 初始化（不阻塞 API/SignalR）");


// 测试 PairingService 是否能被正确解析
try
{
    using var scope = app.Services.CreateScope();
    var pairingService = scope.ServiceProvider.GetRequiredService<PairingService>();
    logger.LogInformation("[Program] PairingService 成功解析，DeviceHub 注入状态: {Status}", 
        pairingService.GetType().GetProperty("_deviceHub", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(pairingService) == null ? "NULL" : "已注入");
}
catch (Exception ex)
{
    logger.LogError(ex, "[Program] 解析 PairingService 失败");
}

// 勿在 app.Run() 前 await 自检：健康检查与 InitializeObsidianAsync（含固定延迟）会推迟 Kestrel 接受连接，
// WebUI/WebSocket 会长时间连不上或反复重试。
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var healthService = app.Services.GetRequiredService<SystemHealthService>();
            // 自检信息可以稍后做（Obsidian warmup 由 HostedService 负责）
            var report = await healthService.GetHealthReportAsync();
            var healthMessage = report.Status == "healthy"
                ? $"健康度：{report.HealthScore}%"
                : $"健康度：{report.HealthScore}% (问题：{string.Join(", ", report.Components.Where(c => c.Status != "healthy").Select(c => c.Name))})";
            logger.LogInformation("System Status: {Status} - {HealthMessage}", report.Status, healthMessage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "后台启动自检或 Obsidian 初始化未完成");
        }

        // 后台刷新 Ollama Library 模型列表
        try
        {
            var ollamaLibrary = app.Services.GetService<OllamaLibraryClient>();
            if (ollamaLibrary != null)
            {
                await ollamaLibrary.RefreshAsync();
                // 每 4 小时自动刷新一次
                _ = Task.Run(async () =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromHours(4));
                    while (await timer.WaitForNextTickAsync())
                    {
                        try { await ollamaLibrary.RefreshAsync(); }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Ollama Library 后台刷新失败");
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama Library 后台刷新初始化失败");
        }
    });
});

logger.LogInformation("===========================================");
logger.LogInformation("Task Runner is running at {ListenUrl} (log hints use {DisplayUrl})", listenUrlForLog, displayBaseUrl);
logger.LogInformation("Health Dashboard: {BaseUrl}/swagger", displayBaseUrl);
logger.LogInformation("Full Health Report: {BaseUrl}/api/health/full", displayBaseUrl);

// 优雅关闭
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("Task Runner Service Stopping...");
});

app.Lifetime.ApplicationStopped.Register(() =>
{
    logger.LogInformation("Task Runner Service Stopped");
});

// 监听地址由 Kestrel 配置（appsettings*.json）、ASPNETCORE_URLS、命令行 --urls 等决定，勿在此硬编码
try
{
    app.Run();
}
catch (Exception ex)
{
    // 确保启动/运行时异常被记录，并且释放单实例互斥量后优雅退出
    try
    {
        var logger2 = app.Services.GetService<ILogger<Program>>();
        logger2?.LogCritical(ex, "Host terminated unexpectedly");
    }
    catch { /* 服务已终止，logger 可能不可用，静默兜底 */ }
    throw;
}
finally
{
    try
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
    }
    catch { /* 释放互斥量失败（如未持有），无需处理 */ }
}

static string ResolveConfiguredListenUrl(IConfiguration configuration)
{
    // 优先使用 HTTP 端点
    var httpUrl = configuration["Kestrel:Endpoints:Http:Url"];
    if (!string.IsNullOrWhiteSpace(httpUrl))
        return httpUrl.Trim();

    var urlsEnv = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(urlsEnv))
    {
        var first = urlsEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
            return first;
    }

    return "http://localhost:8788";
}

// 检查是否为私有 IP 地址（局域网地址）
static bool IsPrivateIpAddress(string host)
{
    if (System.Net.IPAddress.TryParse(host, out var ip))
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 127.0.0.0/8 (loopback)
            if (bytes[0] == 127) return true;
        }
    }
    return false;
}

// 将 0.0.0.0 / + / * 等绑定地址转为日志中可点击的 localhost 提示
static string ToDisplayBaseUrlForLogs(string bindUrl)
{
    if (string.IsNullOrWhiteSpace(bindUrl))
        return "http://localhost:8788";

    var trimmed = bindUrl.Trim();
    if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        return trimmed.TrimEnd('/');

    var host = uri.Host;
    if (host is "0.0.0.0" or "+" or "*")
        host = "localhost";

    return uri.IsDefaultPort
        ? $"{uri.Scheme}://{host}"
        : $"{uri.Scheme}://{host}:{uri.Port}";
}
