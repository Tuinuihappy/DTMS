using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using AMR.DeliveryPlanning.Api.Auth;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using AMR.DeliveryPlanning.Api.RobotPositions;
using AMR.DeliveryPlanning.Api.VendorHealth;
using AMR.DeliveryPlanning.SharedKernel.Auth;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var devAuthBypassEnabled = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Auth:Disable");

// T1.3 — extend shutdown window so MassTransit consumers and in-flight HTTP
// requests can drain before SIGKILL. Default is 5s which is far too short
// for the multi-step Planning workflow (dispatch + Trip persistence + vendor
// HTTP can take 10-30s on a healthy run). Combined with docker-compose
// stop_grace_period=90s so Docker doesn't kill the container before .NET
// finishes draining.
builder.Host.ConfigureHostOptions(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});

// Add Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer<AMR.DeliveryPlanning.Api.OpenApi.IdempotencyKeyOperationTransformer>();
});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseUpper)));

// Configure authentication. Auth:Disable is honored only in Development and
// supplies a tenant claim so tenant-scoped APIs still behave realistically.
if (devAuthBypassEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = DevAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = DevAuthenticationHandler.SchemeName;
    }).AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(
        DevAuthenticationHandler.SchemeName,
        _ => { });
}
else
{
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    }).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings?.Issuer ?? "AMR.DeliveryPlanning",
            ValidAudience = jwtSettings?.Audience ?? "AMR.DeliveryPlanning.Api",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    jwtSettings?.Secret
                    ?? throw new InvalidOperationException("Jwt:Secret is not configured. Set it via environment variable or dotnet user-secrets.")))
        };
        // SignalR cannot send custom Authorization headers on the
        // WebSocket upgrade. Browsers pass the JWT via ?access_token=...
        // on the negotiate + connection URLs, so re-hydrate the token
        // into ctx.Token when the request targets a /hubs/* path.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
}

// Configure MediatR — scan all module Application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Facility.Application.Queries.GetRouteCost.GetRouteCostQuery).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Fleet.Application.Consumers.VehicleStateChangedConsumer).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.CreateEnvelopeTrip.CreateEnvelopeTripCommand).Assembly);
    cfg.AddOpenBehavior(typeof(AMR.DeliveryPlanning.SharedKernel.Behaviors.ValidationBehavior<,>));
});

// Register FluentValidation validators from all module Application assemblies
builder.Services.AddValidatorsFromAssembly(typeof(AMR.DeliveryPlanning.Facility.Application.Queries.GetRouteCost.GetRouteCostQuery).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);

// Register all module services (DbContexts, Repositories, Domain Services, HttpClients)
builder.Services.AddAllModules(builder.Configuration);

// OpenTelemetry
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("AMR.DeliveryPlanning.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
    // P0.3 — projection metrics (lag, throughput, dedup) — Meter name
    // matches AMR.DeliveryPlanning.SharedKernel.Projection.ProjectionMetrics.MeterName.
    // P0 Day 4 — DTMS.SignalR meter for hub invocations / connections /
    // rate-limit drops (HubMetrics class).
    // T1.6 — DTMS.Workflow meter for workflow SLO (stuck orders, consumer
    // retries/faults, outbox pending/age, watchdog replays).
    .WithMetrics(metrics => metrics
        .AddMeter("DTMS.Projection")
        .AddMeter("DTMS.SignalR")
        .AddMeter("DTMS.Workflow")
        // T1.6 — MassTransit native meter emits messaging.* metrics
        // (consume_duration_seconds, receive_total, retry_total, fault_total)
        // so we don't need to write our own observer for consumer retries.
        .AddMeter("MassTransit")
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// T1.6 — singleton holder shared by consumers, watchdog, and outbox processor.
builder.Services.AddSingleton<AMR.DeliveryPlanning.SharedKernel.Diagnostics.WorkflowMetrics>();

// T1.4 — Planning reconciliation watchdog. Bound to the PlanningWatchdog
// config section so ops can toggle Enabled at runtime via appsettings or env.
builder.Services
    .AddOptions<AMR.DeliveryPlanning.Api.Infrastructure.Reconciliation.PlanningWatchdogOptions>()
    .Bind(builder.Configuration.GetSection(
        AMR.DeliveryPlanning.Api.Infrastructure.Reconciliation.PlanningWatchdogOptions.SectionName));
builder.Services.AddHostedService<
    AMR.DeliveryPlanning.Api.Infrastructure.Reconciliation.PlanningReconciliationService>();

// P0 — projection foundation (idempotency, replay stub, metrics singleton).
// Per-module IProjectionInboxRepository implementations register inside
// each module's own infrastructure registration (next to its DbContext).
builder.Services.AddProjectionFoundation();

// P0 — ICurrentActorContext: resolves "who triggered this transition" so
// projectors can stamp TriggeredBy on history rows. HTTP path reads the
// JWT name claim; MassTransit consumers + background services push an
// explicit ActorContext via BeginScope (wired in P0.B7 / consumer filter).
builder.Services.AddHttpContextAccessor();
builder.Services.AddActorContext(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>();
    return () =>
    {
        var ctx = http.HttpContext;
        if (ctx is null) return null;
        var userId = ctx.User.Identity?.Name;
        var traceId = ctx.TraceIdentifier;
        return new ActorContext(
            UserId: string.IsNullOrWhiteSpace(userId) ? null : userId,
            Source: "http",
            CorrelationId: Guid.TryParse(traceId, out var g) ? g : null);
    };
});

// Health checks — /health (liveness), /health/ready (readiness), /health/vendors (external vendors)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

// ── P0 Day 3 — SignalR realtime hub stack ──────────────────────────────
// Hubs: 5 focused hubs map to bounded contexts (OrderHub, JobHub, TripHub,
// DashboardHub, FleetHub). Hot-path optimisations:
//   - MessagePack + LZ4: 30-60% smaller payloads, 3-5× faster parse vs JSON.
//   - Redis backplane (env-var gated): enables multi-instance scale-out
//     without per-instance sticky sessions. Defaults to in-memory for the
//     single-container Docker layout. Flip via SignalR__UseRedisBackplane=true.
//   - JWT in ?access_token=: handled in the JwtBearer.OnMessageReceived
//     event above — browsers can't send Authorization headers on
//     WebSocket upgrades.
// P0 Day 4 — observability singletons + throttling background services
// must register BEFORE AddSignalR so the filter types can resolve
// HubMetrics from DI.
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Realtime.Observability.HubMetrics>();
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Realtime.Filters.TracingHubFilter>();
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Realtime.Filters.RateLimitedHubFilter>();
// Batchers register as both singleton (so projectors can inject and
// enqueue) AND as a hosted service (so the drain loop runs).
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Realtime.Pipeline.DashboardCounterBatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AMR.DeliveryPlanning.Api.Realtime.Pipeline.DashboardCounterBatcher>());
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Realtime.Pipeline.FleetPositionThrottler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AMR.DeliveryPlanning.Api.Realtime.Pipeline.FleetPositionThrottler>());

// CORS for browser→hub direct connection. Same-origin reverse-proxy setups
// (Nginx fronting both frontend + backend) wouldn't need this — but the
// docker-compose dev layout has the frontend on :3000 and API on :5219,
// so SignalR's WebSocket upgrade needs an explicit allowlist + credentials.
// Origins come from the env var Cors__HubsAllowedOrigins (comma-separated).
const string HubsCorsPolicy = "HubsCorsPolicy";
var hubsAllowedOrigins = (builder.Configuration["Cors:HubsAllowedOrigins"]
    ?? "http://localhost:3000,http://localhost:3001")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy(HubsCorsPolicy, policy => policy
        .WithOrigins(hubsAllowedOrigins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var signalRBuilder = builder.Services.AddSignalR(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        // 16 KB cap — DTMS hub methods are subscription-only, no large
        // client → server payloads should reach this limit. Acts as a
        // belt-and-suspenders guard against accidental misuse.
        options.MaximumReceiveMessageSize = 16 * 1024;
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        // Filters run in registration order. Tracing wraps everything
        // (records duration including time inside rate-limit check).
        // Rate limit is innermost so a rejected call never reaches the
        // hub method body — and only counts once in the metrics.
        options.AddFilter<AMR.DeliveryPlanning.Api.Realtime.Filters.TracingHubFilter>();
        options.AddFilter<AMR.DeliveryPlanning.Api.Realtime.Filters.RateLimitedHubFilter>();
    })
    .AddMessagePackProtocol();

var useRedisBackplane = builder.Configuration.GetValue<bool>("SignalR:UseRedisBackplane");
if (useRedisBackplane)
{
    signalRBuilder.AddStackExchangeRedis(redisConn, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("dtms:sr");
        options.Configuration.AbortOnConnectFail = false;
    });
}
var rabbitConfig = builder.Configuration.GetSection("RabbitMq");
var rabbitHost = rabbitConfig["Host"] ?? "localhost";
var rabbitUser = rabbitConfig["Username"] ?? "guest";
var rabbitPass = rabbitConfig["Password"] ?? "guest";
var riot3BaseUrl = builder.Configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:12000";
var riot3ApiKey  = builder.Configuration.GetValue<string>("VendorAdapter:Riot3:ApiKey") ?? string.Empty;

// Vendor health monitoring — background poller keeps an in-memory snapshot of
// each vendor's health so /health/vendors answers in <1ms instead of issuing
// an HTTP call to the vendor on every probe. State machine debounces transient
// network blips so the dashboard doesn't flap.
builder.Services.Configure<VendorHealthOptions>(
    builder.Configuration.GetSection("VendorHealth"));
builder.Services.AddSingleton<IVendorHealthStore, InMemoryVendorHealthStore>();
builder.Services.AddHttpClient<IRiot3HealthProbe, Riot3HealthProbe>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<VendorHealthOptions>>().CurrentValue.Riot3;
    client.BaseAddress = new Uri(riot3BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds) + 1);
    if (!string.IsNullOrWhiteSpace(riot3ApiKey))
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
});
builder.Services.AddHostedService<Riot3HealthPollerService>();
builder.Services.AddHostedService<InfraHealthPollerService>();
builder.Services.AddHostedService<VendorHealthBroadcaster>();
builder.Services.Configure<VendorHealthWebhookOptions>(
    builder.Configuration.GetSection("VendorHealth:Webhook"));
builder.Services.AddHttpClient(nameof(VendorHealthWebhookNotifier));
builder.Services.AddHostedService<VendorHealthWebhookNotifier>();
builder.Services.AddTransient<RiotHealthCheckFromStore>();

// Phase B Step B2 — health-check class reuses the singleton NpgsqlDataSource
// so /health/ready stops opening its own raw connection per probe.
builder.Services.AddSingleton<AMR.DeliveryPlanning.Api.Infrastructure.Health.NpgsqlDataSourceHealthCheck>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is running"))
    .AddCheck<AMR.DeliveryPlanning.Api.Infrastructure.Health.NpgsqlDataSourceHealthCheck>(
        "postgres", tags: ["ready"])
    .AddCheck("redis", () =>
    {
        try
        {
            var muxer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn);
            var db = muxer.GetDatabase();
            db.Ping();
            muxer.Close();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(ex.Message);
        }
    }, tags: ["ready"])
    .AddCheck("rabbitmq", () =>
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = rabbitHost,
                UserName = rabbitUser,
                Password = rabbitPass,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
            };
            using var conn = factory.CreateConnection();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }, tags: ["ready"])
    .AddCheck<RiotHealthCheckFromStore>("riot3", tags: ["vendors"]);

// Rate limiting — fixed window per remote IP. Defaults to 100 req/min; override
// via RateLimit__PermitLimit / RateLimit__WindowSeconds / RateLimit__QueueLimit
// for load tests (e.g. PermitLimit=100000, WindowSeconds=1).
var rlPermitLimit = builder.Configuration.GetValue<int?>("RateLimit:PermitLimit") ?? 100;
var rlWindowSeconds = builder.Configuration.GetValue<int?>("RateLimit:WindowSeconds") ?? 60;
var rlQueueLimit = builder.Configuration.GetValue<int?>("RateLimit:QueueLimit") ?? 5;
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rlPermitLimit,
                Window = TimeSpan.FromSeconds(rlWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = rlQueueLimit
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Apply EF Core migrations for all module databases on startup.
// Wrapped in a retry loop with exponential backoff so a brief postgres
// outage (compose restart, brief network blip, slow startup ordering)
// doesn't enter a fatal crash loop on the api container — root cause of
// the prod-shape incident on 2026-06-20 where stopping postgres for a
// smoke test crashed api repeatedly because every restart re-ran the
// migration check against an unreachable DB.
{
    const int maxAttempts = 12;
    var bootstrapLogger = app.Services.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            if (attempt == 1)
                logger.LogInformation("Applying database migrations...");
            else
                logger.LogInformation("Migration retry attempt {Attempt}/{Max}", attempt, maxAttempts);

            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Facility.Infrastructure.Data.FacilityDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Fleet.Infrastructure.Data.FleetDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Planning.Infrastructure.Data.PlanningDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Dispatch.Infrastructure.Data.DispatchDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AuthDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data.VendorAdapterDbContext>(), logger, app.Environment);

            logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            // Cap delay at 30s — total max wait ~3-4 minutes before final exit.
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
            bootstrapLogger.LogWarning(
                "Migration attempt {Attempt}/{Max} failed: {Message}. Retrying in {Delay}s",
                attempt, maxAttempts, ex.Message, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }
}

// Seed ActionCatalog defaults (upsert — safe to run every startup)
await SeedActionCatalogAsync(app.Services);

// Seed default admin user
await AuthEndpoints.SeedDefaultUserAsync(app.Services);

// --migrate-only — when set the process exits cleanly after migrations
// + seeds. Used by the dtms-migrator compose service to apply schema
// changes BEFORE the api container starts (via depends_on
// service_completed_successfully), preventing the crash-chain where
// api startup races against a partially-ready DB. The api service
// still runs the retry-wrapped migration block above as a defence in
// depth — second invocation is a no-op when nothing is pending.
if (args.Contains("--migrate-only"))
{
    app.Logger.LogInformation("Migrate-only mode — exiting cleanly.");
    return;
}

static async Task SeedActionCatalogAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var catalog = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services.IActionCatalogService>();

    // Both liftup and feeder are the same vendor — both use the RIOT3 adapter.
    // Unified JSON format: {"actionType": "<ActionID>", "0": "<P0>", "1": "<P1>"}
    //
    // ── OASIS liftup (Action ID 4) ────────────────────────────────────────────
    // Source: Oasis series action command instruction manual
    var defaults = new[]
    {
        // Canonical action      AdapterKey  ActionID  P0    P1
        ("liftup", "LIFT",               "riot3", 4,   1,   "0"),   // Align and Lift Shelf (max height)
        ("liftup", "DROP",               "riot3", 4,   2,   "0"),   // Lower Shelf and Return to Zero
        ("liftup", "LIFT_FRONT_REAR",    "riot3", 4,   5,   "0"),   // Align and Lift Shelf (Front/Rear only)
        ("liftup", "LIFT_PLATFORM_SAFE", "riot3", 4,   9,   "0"),   // Lift Platform with Overload Detection
        ("liftup", "LIFT_PLATFORM",      "riot3", 4,   11,  "0"),   // Lift Platform (no overload detect)
        ("liftup", "ROTATE_PLATFORM",    "riot3", 4,   12,  "0"),   // Rotate Platform (P1 = direction × 0.1°)
        ("liftup", "SYNC_ROTATE_ON",     "riot3", 4,   13,  "0"),   // Enable Chassis-Platform Synchronized Rotation
        ("liftup", "SYNC_ROTATE_OFF",    "riot3", 4,   14,  "0"),   // Disable Chassis-Platform Synchronized Rotation
        ("liftup", "PLATFORM_INIT",      "riot3", 4,   15,  "0"),   // Platform Initialization
        ("liftup", "SHELF_CORRECT",      "riot3", 4,   21,  "0"),   // Manual Shelf Correction
        ("liftup", "LIFT_CALIBRATE",     "riot3", 4,   22,  "0"),   // Lift Current Calibration

        // ── Feeder type (Action ID 192) ──────────────────────────────────────
        // Same vendor as liftup → uses riot3 adapter, same parameter format
        ("feeder", "INIT",               "riot3", 192, 100, "100"), // Initialization
        ("feeder", "LIFT",               "riot3", 192, 1,   "3"),   // Left Side Loading
        ("feeder", "DROP",               "riot3", 192, 1,   "4"),   // Left Side Unloading
        ("feeder", "RIGHT_LOAD",         "riot3", 192, 2,   "3"),   // Right Side Loading
        ("feeder", "RIGHT_UNLOAD",       "riot3", 192, 2,   "4"),   // Right Side Unloading
        ("feeder", "FRONT_PROBE",        "riot3", 192, 22,  "0"),   // Front Probe Module Action (P1 = height W mm, -50<W<50)
        ("feeder", "FRONT_AXIS_HOME",    "riot3", 192, 100, "102"), // Front Axis Home Position Movement
        ("feeder", "LEFT_STOP_HOME",     "riot3", 192, 201, "1"),   // Left Stopper Home Position
        ("feeder", "LEFT_STOP_BLOCK",    "riot3", 192, 201, "2"),   // Left Stopper Blocking Position
        ("feeder", "RIGHT_STOP_HOME",    "riot3", 192, 201, "11"),  // Right Stopper Home Position
        ("feeder", "RIGHT_STOP_BLOCK",   "riot3", 192, 201, "21"),  // Right Stopper Blocking Position
    };

    foreach (var (vtKey, action, adapterKey, actionId, p0, p1) in defaults)
    {
        var paramsJson = $"{{\"actionType\":\"{actionId}\",\"0\":\"{p0}\",\"1\":\"{p1}\"}}";
        var existing = await catalog.GetAsync(vtKey, action);
        if (existing == null)
            await catalog.UpsertAsync(
                new AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models.ActionCatalogEntry(vtKey, action, adapterKey, paramsJson));
    }
}

static async Task ApplyMigrationsAsync(DbContext db, Microsoft.Extensions.Logging.ILogger logger, IWebHostEnvironment env)
{
    var dbName = db.GetType().Name;

    var hasMigrations = db.Database.GetMigrations().Any();
    if (hasMigrations)
    {
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            logger.LogInformation("Applying {Count} pending migration(s) for {Context}",
                pending.Count(), dbName);
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogDebug("{Context} schema is up to date", dbName);
        }
    }
    else if (env.IsProduction())
    {
        // Hard fail: production must never start without migrations. If this throws,
        // it means a new DbContext was added without running 'dotnet ef migrations add'.
        throw new InvalidOperationException(
            $"Production startup aborted: {dbName} has no EF migrations. " +
            "Run: dotnet ef migrations add InitialCreate --project <InfraProject> --startup-project src/AMR.DeliveryPlanning.Api");
    }
    else
    {
        logger.LogWarning(
            "{Context} has no EF migrations — using EnsureCreated (dev fallback). " +
            "Run: dotnet ef migrations add InitialCreate --project <InfraProject> --startup-project src/AMR.DeliveryPlanning.Api",
            dbName);
        var created = await db.Database.EnsureCreatedAsync();
        if (!created)
        {
            // Database already existed (e.g. created by POSTGRES_DB env var before API started).
            // EnsureCreated returns false and skips table creation in this case, so we
            // call CreateTablesAsync explicitly to create any missing schema/tables.
            try
            {
                var creator = db.GetService<IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync();
                logger.LogInformation("{Context} schema created via CreateTablesAsync", dbName);
            }
            catch (Exception ex)
            {
                logger.LogDebug("{Context} tables already exist or creation skipped: {Message}", dbName, ex.Message);
            }
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "AMR.DeliveryPlanning.Api v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "AMR Delivery Planning API";
    });
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.UseRateLimiter();

// CORS must run BEFORE auth so the preflight OPTIONS request gets the
// allow headers even on unauthenticated origins.
app.UseCors(HubsCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Liveness probe (always 200 if process is up)
app.MapHealthChecks("/health").AllowAnonymous()
    .WithTags("Health").WithSummary("Liveness").WithDescription("Returns 200 if the process is running.");
// Readiness probe (checks postgres, rabbitmq, redis)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteJsonResponse
}).AllowAnonymous()
    .WithTags("Health").WithSummary("Readiness").WithDescription("Checks core dependencies: PostgreSQL, Redis, RabbitMQ.");
// Vendor probe (checks external systems: RIOT3)
app.MapHealthChecks("/health/vendors", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("vendors"),
    ResponseWriter = WriteJsonResponse
}).AllowAnonymous()
    .WithTags("Health").WithSummary("Vendors").WithDescription("Checks external vendor connectivity: RIOT3.");

// OpenAPI-visible wrappers — Swagger shows these under the Health tag
app.MapGet("/health/status/liveness", async (HealthCheckService svc) =>
{
    var report = await svc.CheckHealthAsync(check => check.Name == "self");
    return WriteHealthResult(report);
})
.AllowAnonymous()
.WithTags("Health")
.WithSummary("Liveness")
.WithDescription("Returns Healthy if the process is running.");

app.MapGet("/health/status/ready", async (HealthCheckService svc) =>
{
    var report = await svc.CheckHealthAsync(check => check.Tags.Contains("ready"));
    return WriteHealthResult(report);
})
.AllowAnonymous()
.WithTags("Health")
.WithSummary("Readiness")
.WithDescription("Checks core dependencies: PostgreSQL, Redis, RabbitMQ.");

app.MapGet("/health/status/vendors", async (HealthCheckService svc) =>
{
    var report = await svc.CheckHealthAsync(check => check.Tags.Contains("vendors"));
    return WriteHealthResult(report);
})
.AllowAnonymous()
.WithTags("Health")
.WithSummary("Vendors")
.WithDescription("Checks external vendor connectivity: RIOT3.");

// Map auth endpoint (anonymous)
app.MapAuthEndpoints();

// Map all module Minimal API endpoints (require auth)
app.MapAllModuleEndpoints();

// Map live robot positions endpoint (lives in the Api project because the
// store + DTO are composition-root concerns, not a Facility domain concept).
app.MapRobotPositionEndpoints();

// Vendor health snapshot endpoint — reads from in-memory store updated by
// Riot3HealthPollerService. Push notifications go through DashboardHub
// (boardKey="vendor-health") via VendorHealthBroadcaster.
app.MapVendorHealth();

// ── P0 Day 3 — SignalR hub endpoints ──────────────────────────────────
// Five focused hubs (one per bounded context). Browser pages connect only
// to the hub their UI subscribes to — lazy connection keeps idle WS count
// low. Auth enforced via [Authorize] on the hub classes; JWT comes in on
// the access_token query param (see OnMessageReceived in JwtBearer setup).
app.MapHub<AMR.DeliveryPlanning.Api.Realtime.Hubs.OrderHub>("/hubs/orders");
app.MapHub<AMR.DeliveryPlanning.Api.Realtime.Hubs.JobHub>("/hubs/jobs");
app.MapHub<AMR.DeliveryPlanning.Api.Realtime.Hubs.TripHub>("/hubs/trips");
app.MapHub<AMR.DeliveryPlanning.Api.Realtime.Hubs.DashboardHub>("/hubs/dashboard");
app.MapHub<AMR.DeliveryPlanning.Api.Realtime.Hubs.FleetHub>("/hubs/fleet");

app.Run();

static IResult WriteHealthResult(HealthReport report)
{
    var status = report.Status == HealthStatus.Healthy ? 200 : 503;
    var body = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name        = e.Key,
            status      = e.Value.Status.ToString(),
            description = e.Value.Description,
            error       = e.Value.Exception?.Message
        })
    };
    return status == 200 ? Results.Ok(body) : Results.Json(body, statusCode: 503);
}

static Task WriteJsonResponse(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    ctx.Response.StatusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
    var result = System.Text.Json.JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name   = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            error  = e.Value.Exception?.Message
        })
    });
    return ctx.Response.WriteAsync(result);
}
