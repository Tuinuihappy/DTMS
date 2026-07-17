using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using DTMS.Api.Auth;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.Api.Middlewares;
using DTMS.Api.Modules;
using DTMS.Api.RobotPositions;
using DTMS.Api.VendorHealth;
using DTMS.Iam.Application.Authorization;
using DTMS.Infrastructure;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Projection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// Phase 4.3 — one-shot VAPID keypair generator. Run via:
//   dotnet run --project src/DTMS.Api -- --generate-vapid-keys
// Prints the keypair + exits before any host setup. Operator pastes
// the values into appsettings.Development.json or .env.
if (args.Contains("--generate-vapid-keys"))
{
    var (pub, priv) = DTMS.Transport.Manual.Infrastructure.Push.VapidKeyHelper.Generate();
    Console.WriteLine("# VAPID keypair — store the private key as a secret.");
    Console.WriteLine($"Push__Vapid__PublicKey={pub}");
    Console.WriteLine($"Push__Vapid__PrivateKey={priv}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

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
    options.AddOperationTransformer<DTMS.Api.OpenApi.IdempotencyKeyOperationTransformer>();
    options.AddDocumentTransformer<DTMS.Api.OpenApi.BearerSecuritySchemeTransformer>();
});
builder.Services.AddAuthorization(o =>
{
    // Phase 4.2 — Operator PWA policies (OperatorOnly / SupervisorOnly
    // / AdminOnly). All require the OperatorJwt scheme so the operator
    // app's JWT can't accidentally authorize against admin endpoints
    // and vice versa.
    o.AddOperatorPolicies();
});

// Permission System Phase A — register the per-request claims transformer
// that pulls role→permission mappings from iam.RolePermissions and the
// handler that evaluates `.RequirePermission(Permissions.<Module>.<Code>)`
// (always the catalog constant, never a raw string — the architecture test
// scans for literals). The transformer uses IMemoryCache (registered below)
// for a 5-minute hot path.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<
    Microsoft.AspNetCore.Authentication.IClaimsTransformation,
    DTMS.Iam.Application.Authorization.PermissionClaimsTransformer>();
builder.Services.AddSingleton<
    Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    DTMS.Iam.Application.Authorization.PermissionAuthorizationHandler>();
// Phase S.3.1a — sibling handler that resolves the permission code from
// the request's {key} route value at enforcement time. Registered as a
// singleton because it carries no per-request state (the route value is
// read from the injected IHttpContextAccessor inside the handler call).
builder.Services.AddSingleton<
    Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    DTMS.Iam.Application.Authorization.SourceSystemPermissionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseUpper));
    // Normalize every request-body DateTime to UTC so non-UTC input (offset or
    // bare) can't 500 on the `timestamp with time zone` columns. Covers the
    // whole write surface (actedAt, ServiceWindow, …) in one place.
    options.SerializerOptions.Converters.Add(new DTMS.Api.Serialization.UtcDateTimeJsonConverter());
});

// Configure authentication. Per ADR-014, External Auth (at
// http://10.204.212.28:15000) owns identity; DTMS only validates the
// tokens it issues. The public RSA key + expected iss/aud live in
// the Jwt config section.
{
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException("Jwt config section missing.");
    if (string.IsNullOrWhiteSpace(jwtSettings.PublicKey))
        throw new InvalidOperationException("Jwt:PublicKey is not configured. Set the External Auth RSA public key.");

    var externalAuthRsa = RSA.Create();
    externalAuthRsa.ImportRSAPublicKey(Convert.FromBase64String(jwtSettings.PublicKey.Replace("\r", "").Replace("\n", "")), out _);
    var externalAuthKey = new RsaSecurityKey(externalAuthRsa);

    // Phase S.8b — accept DTMS-signed system JWTs at the SAME JwtBearer
    // pipeline that validates External Auth user JWTs. Trade-off: system
    // JWTs (issued by admins, lifetime up to 365 days) can now hit any
    // [Authorize] endpoint — permission grants in SystemClientPermissions
    // become the sole gate. Path-based scoping (/api/v1/source/*) is no
    // longer the security boundary; permission scope is.
    RsaSecurityKey? systemJwtKey = null;
    if (!string.IsNullOrWhiteSpace(jwtSettings.SystemSigningPublicKey))
    {
        var systemRsa = RSA.Create();
        systemRsa.ImportFromPem(jwtSettings.SystemSigningPublicKey);
        systemJwtKey = new RsaSecurityKey(systemRsa) { KeyId = jwtSettings.SystemTokenKeyId };
    }

    var signingKeys = systemJwtKey is null
        ? new SecurityKey[] { externalAuthKey }
        : new SecurityKey[] { externalAuthKey, systemJwtKey };

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // External Auth ships custom claims (EmployeeId, Email) using
            // their short names. The framework default rewrites these to
            // WS-Federation URIs which breaks ctx.User.FindFirst("EmployeeId").
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Phase 0: External Auth's JWT has no iss/aud claims, so
                // we validate signature + expiry only. Phase 1A will turn
                // these back on once External Auth includes those claims.
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Resolver returns BOTH signing keys unconditionally —
                // JsonWebTokenHandler tries each in turn and one must
                // verify. External Auth JWTs have no kid header (verify
                // with externalAuthKey); system JWTs have kid=dtms-system-...
                // (verify with systemJwtKey). Bypass kid matching for
                // the same reason as SystemJwtValidator — RsaSecurityKey
                // initializer-assigned KeyId doesn't propagate through
                // the handler's internal key collection.
                IssuerSigningKeyResolver = (_, _, _, _) => signingKeys,

                // Pin the WS-Federation URIs External Auth uses so role/name
                // checks don't silently break if a future framework version
                // changes its defaults. System JWTs use `sub` for identity
                // (checked separately in PermissionClaimsTransformer).
                NameClaimType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",

                // Tighten the default 5-min clock skew — leaked tokens stay
                // usable for at most 30 seconds past their exp.
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            options.Events = new JwtBearerEvents
            {
                // SignalR cannot send custom Authorization headers on the
                // WebSocket upgrade. Browsers pass the JWT via ?access_token=...
                // on the negotiate + connection URLs, so re-hydrate the token
                // into ctx.Token when the request targets a /hubs/* path.
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                // Surface the actual rejection reason so ops can tell
                // "expired token" apart from "wrong signature" without
                // turning on Debug logging for the whole auth namespace.
                OnAuthenticationFailed = ctx =>
                {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Auth");
                    logger.LogWarning(
                        "JWT rejected on {Method} {Path}: {Reason}",
                        ctx.HttpContext.Request.Method,
                        ctx.HttpContext.Request.Path,
                        ctx.Exception.Message);
                    return Task.CompletedTask;
                },
            };
        });

    // Phase S.8 — System (M2M) JWT issuance for OAuth client_credentials
    // grant. Options mapped from the same Jwt config section the user-JWT
    // validation above reads from, but kept as a separate Options class so
    // the Iam.Application layer doesn't depend on the API-layer JwtSettings
    // type. Singleton because the underlying RSA + SigningCredentials are
    // built once in the ctor and reused across token mints.
    builder.Services.Configure<DTMS.Iam.Application.Authorization.SystemJwtIssuerOptions>(o =>
    {
        o.PrivateKeyPem = jwtSettings.SystemSigningPrivateKey;
        o.PublicKeyPem = jwtSettings.SystemSigningPublicKey;
        o.Issuer = jwtSettings.SystemTokenIssuer;
        o.Audience = jwtSettings.SystemTokenAudience;
        o.DefaultLifetimeSeconds = jwtSettings.SystemTokenLifetimeSeconds;
        o.KeyId = jwtSettings.SystemTokenKeyId;
    });
    builder.Services.AddSingleton<
        DTMS.Iam.Application.Authorization.ISystemJwtIssuer,
        DTMS.Iam.Application.Authorization.SystemJwtIssuer>();
    builder.Services.AddSingleton<
        DTMS.Iam.Application.Authorization.ISystemJwtValidator,
        DTMS.Iam.Application.Authorization.SystemJwtValidator>();
}

// Configure MediatR — scan all module Application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Facility.Application.Queries.GetRouteCost.GetRouteCostQuery).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Fleet.Application.Consumers.VehicleStateChangedConsumer).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Dispatch.Application.Commands.CreateEnvelopeTrip.CreateEnvelopeTripCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip.AcknowledgeTripCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(DTMS.Wms.Application.Commands.SyncWmsLocations.SyncWmsLocationsCommand).Assembly);
    cfg.AddOpenBehavior(typeof(DTMS.SharedKernel.Behaviors.ValidationBehavior<,>));
});

// Register FluentValidation validators from all module Application assemblies
builder.Services.AddValidatorsFromAssembly(typeof(DTMS.Facility.Application.Queries.GetRouteCost.GetRouteCostQuery).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);

// Register all module services (DbContexts, Repositories, Domain Services, HttpClients)
builder.Services.AddAllModules(builder.Configuration);

// OpenTelemetry
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("DTMS.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Phase O4 — outbox publish spans. Trace context is captured at
        // outbox-row commit time (Activity.Current.Id → TraceParent column)
        // and restored in OutboxProcessorService.PublishBatchAsync so the
        // consumer span chains under the original request's root, even
        // across the async gap.
        .AddSource(DTMS.SharedKernel.Diagnostics.OutboxActivitySource.Name)
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
    // P0.3 — projection metrics (lag, throughput, dedup) — Meter name
    // matches DTMS.SharedKernel.Projection.ProjectionMetrics.MeterName.
    // P0 Day 4 — DTMS.SignalR meter for hub invocations / connections /
    // rate-limit drops (HubMetrics class).
    // T1.6 — DTMS.Workflow meter for workflow SLO (stuck orders, consumer
    // retries/faults, outbox pending/age, watchdog replays).
    .WithMetrics(metrics => metrics
        .AddMeter("DTMS.Projection")
        .AddMeter("DTMS.SignalR")
        .AddMeter("DTMS.Workflow")
        .AddMeter(DTMS.Api.Infrastructure.Metrics.PoolMetrics.MeterName)
        // T1.6 — MassTransit native meter emits messaging.* metrics
        // (consume_duration_seconds, receive_total, retry_total, fault_total)
        // so we don't need to write our own observer for consumer retries.
        .AddMeter("MassTransit")
        // PR-H deploy — Prometheus scrape endpoint at /metrics. The
        // ops-side `prometheus` container pulls from here every 15 s
        // and Grafana renders the pool dashboard on top. OTLP export
        // kept side-by-side so any future collector-based pipeline
        // (Tempo/Loki/etc.) still receives the same metrics stream.
        .AddPrometheusExporter()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// T1.6 — singleton holder shared by consumers, watchdog, and outbox processor.
builder.Services.AddSingleton<DTMS.SharedKernel.Diagnostics.WorkflowMetrics>();

// G2 — shutdown observability. Records `total` shutdown phase via lifetime
// hooks; `bus` phase is recorded by BusShutdownTimingObserver wired
// inside AddMassTransit. Both emit log lines so ops see the breakdown
// even before Phase G's Prometheus scrape + Grafana panel lands.
builder.Services.AddHostedService<
    DTMS.Api.Infrastructure.Diagnostics.ShutdownPhaseRecorder>();

// T1.4 — Planning reconciliation watchdog. Bound to the PlanningWatchdog
// config section so ops can toggle Enabled at runtime via appsettings or env.
builder.Services
    .AddOptions<DTMS.Api.Infrastructure.Reconciliation.PlanningWatchdogOptions>()
    .Bind(builder.Configuration.GetSection(
        DTMS.Api.Infrastructure.Reconciliation.PlanningWatchdogOptions.SectionName));
builder.Services.AddHostedService<
    DTMS.Api.Infrastructure.Reconciliation.PlanningReconciliationService>();

// P0 — projection foundation (idempotency, replay stub, metrics singleton).
// Per-module IProjectionInboxRepository implementations register inside
// each module's own infrastructure registration (next to its DbContext).
builder.Services.AddProjectionFoundation();

// P0 — ICurrentActorContext: resolves "who triggered this transition" so
// projectors can stamp TriggeredBy on history rows. HTTP path prefers
// EmployeeId (stable across username changes) and falls back to the JWT
// name claim; MassTransit consumers + background services push an
// explicit ActorContext via BeginScope (wired in P0.B7 / consumer filter).
//
// S.1 — resolver now also stamps Channel (ManualWeb / OperatorPwa /
// SystemApi / InternalJob), Type (User vs System), and DisplayName so
// the audit pipeline can distinguish a web action from a PWA tap from
// a federated source-system callback. The SystemApi branch fires once
// S.2's SystemClientAuthMiddleware sets ctx.Items["principal"] —
// until then every authenticated request is a user.
builder.Services.AddHttpContextAccessor();

builder.Services.AddActorContext(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>();
    return () =>
    {
        var ctx = http.HttpContext;
        if (ctx is null) return null;

        var employeeId = ctx.User.FindFirst("EmployeeId")?.Value
                      ?? ctx.User.FindFirst("employeeCode")?.Value
                      ?? ctx.User.Identity?.Name;
        // DisplayName comes from the JWT's "displayName" claim — the IDP
        // mirrors it in the /auth/login response body too (same value).
        // OperatorSyncMiddleware uses the same claim key for the PWA scheme.
        // Leave empty if absent; mapper collapses to null on the wire and
        // the audit chip renders ActorId-only.
        var displayName = ctx.User.FindFirst("displayName")?.Value
                       ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                       ?? string.Empty;

        // SystemPrincipal lands here after SystemClientAuthMiddleware
        // (Phase S.2). DTMS.Api already references the Iam projects via
        // ModuleServiceRegistration so the typed cast is cheaper than
        // the reflection probe used during S.1 stub-out.
        string principalId;
        DTMS.SharedKernel.Auth.PrincipalType type;
        DTMS.SharedKernel.Auth.SourceChannel channel;
        if (ctx.Items["principal"] is DTMS.Iam.Application.Authorization.SystemPrincipal sp)
        {
            principalId = sp.PrincipalId;
            type = DTMS.SharedKernel.Auth.PrincipalType.System;
            channel = DTMS.SharedKernel.Auth.SourceChannel.SystemApi;
            // Prefer the system's stored DisplayName over whatever the
            // JWT layer wrote — system requests are unauthenticated as
            // a user so JWT-claim displayName will be empty/null anyway.
            if (!string.IsNullOrWhiteSpace(sp.DisplayName))
                displayName = sp.DisplayName;
        }
        else
        {
            principalId = string.IsNullOrWhiteSpace(employeeId)
                ? string.Empty
                : $"user:{employeeId}";
            type = DTMS.SharedKernel.Auth.PrincipalType.User;
            channel = ctx.Request.Path.StartsWithSegments("/api/operator")
                ? DTMS.SharedKernel.Auth.SourceChannel.OperatorPwa
                : DTMS.SharedKernel.Auth.SourceChannel.ManualWeb;
        }

        // Prefer the W3C trace id so cross-service correlation works
        // when the call originated upstream; fall back to ASP.NET's
        // per-request TraceIdentifier. Only land in CorrelationId when
        // it parses as a Guid (the legacy column type) — non-Guid trace
        // ids still flow through structured logs.
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                   ?? ctx.TraceIdentifier;

        return new ActorContext(
            principalId: principalId,
            type: type,
            displayName: displayName,
            channel: channel,
            onBehalfOf: null,
            correlationId: Guid.TryParse(traceId, out var g) ? g : null);
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
builder.Services.AddSingleton<DTMS.Api.Realtime.Observability.HubMetrics>();
builder.Services.AddSingleton<DTMS.Api.Realtime.Filters.TracingHubFilter>();
builder.Services.AddSingleton<DTMS.Api.Realtime.Filters.RateLimitedHubFilter>();
// G1 Phase 1 — pod drain coordination. Singleton state shared between the
// admin endpoint (POST /api/v1/admin/drain-start), the readiness health
// check (flips /health/ready to 503), and the hub filter (rejects new
// connections/invocations during drain).
builder.Services.AddSingleton<
    DTMS.Api.Realtime.Drain.IConnectionDrainService,
    DTMS.Api.Realtime.Drain.ConnectionDrainService>();
builder.Services.AddSingleton<DTMS.Api.Realtime.Drain.DrainAwareHubFilter>();
// Phase F1 follow-up — auto-join every new connection to a per-pod
// group so the drain broadcast can target only THIS pod's clients
// (not the whole cluster via the Redis backplane).
builder.Services.AddSingleton<DTMS.Api.Realtime.Drain.PodGroupHubFilter>();
// Batchers register as both singleton (so projectors can inject and
// enqueue) AND as a hosted service (so the drain loop runs).
builder.Services.AddSingleton<DTMS.Api.Realtime.Pipeline.DashboardCounterBatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DTMS.Api.Realtime.Pipeline.DashboardCounterBatcher>());
builder.Services.AddSingleton<DTMS.Api.Realtime.Pipeline.FleetPositionThrottler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DTMS.Api.Realtime.Pipeline.FleetPositionThrottler>());

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
        // Filters run in registration order. Drain is OUTERMOST so a
        // rejected connection during shutdown short-circuits before we
        // pay for tracing/rate-limit accounting. PodGroup runs second —
        // joins the new connection to its per-pod group BEFORE any
        // application-level invocations fire (so a drain broadcast that
        // races against a fresh connect still finds it in the group).
        // Tracing wraps the rest (records duration including time inside
        // rate-limit check). Rate limit is innermost so a rejected call
        // never reaches the hub method body — and only counts once in
        // the metrics.
        options.AddFilter<DTMS.Api.Realtime.Drain.DrainAwareHubFilter>();
        options.AddFilter<DTMS.Api.Realtime.Drain.PodGroupHubFilter>();
        options.AddFilter<DTMS.Api.Realtime.Filters.TracingHubFilter>();
        options.AddFilter<DTMS.Api.Realtime.Filters.RateLimitedHubFilter>();
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

// Phase S.0 — shared scale infrastructure. Single IConnectionMultiplexer
// for the whole app (DTMS.Infrastructure tiered cache, distributed
// circuit breaker, and the source-system pub/sub channels that S.2-S.3
// add). The health check and SignalR backplane still create their own
// transient connections — they predate this singleton and have their
// own lifecycle requirements (health check needs to fail-fast on Connect).
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
{
    var cfg = StackExchange.Redis.ConfigurationOptions.Parse(redisConn);
    cfg.AbortOnConnectFail = false;
    cfg.ClientName = "dtms-api-infra";
    return StackExchange.Redis.ConnectionMultiplexer.Connect(cfg);
});
builder.Services.AddDtmsTieredCache();
builder.Services.AddDtmsDistributedCircuitBreaker();

// Phase S.2 — batched writer for iam.SystemRequestLog. The drain
// service flushes via SystemRequestLogSink (registered in
// ModuleServiceRegistration alongside the IamDbContext factory).
builder.Services.AddDtmsBatchedLog<DTMS.Iam.Domain.Entities.SystemRequestLogEntry>();

// Phase S.2 — partition maintenance for iam.SystemRequestLog. The
// migration pre-seeds 3 months of partitions; this background service
// rolls forward every 6h and drops anything older than 3 months.
builder.Services.AddDtmsPartitionMaintenance<DTMS.Iam.Infrastructure.Data.IamDbContext>(opts =>
{
    opts.Targets = new[]
    {
        new DTMS.Infrastructure.Database.PartitionTarget(
            // Unquoted — the service Postgres-quotes both segments
            // when emitting DDL so PascalCase table names survive.
            SchemaAndTable: "iam.SystemRequestLog",
            TimeColumn: "OccurredAt",
            AdvanceMonths: 2,
            RetainMonths: 3),
    };
});

// Phase S.2 — register the two middlewares as transient (IMiddleware
// convention so DI resolves them per-request and we can inject scoped
// repos). app.UseWhen wires them into the pipeline below.
builder.Services.AddTransient<DTMS.Api.Middlewares.SystemClientAuthMiddleware>();
builder.Services.AddTransient<DTMS.Api.Middlewares.SystemRequestLoggingMiddleware>();
// Authorization wall — 403s system principals outside /api/v1/source/*.
builder.Services.AddTransient<DTMS.Api.Middlewares.SystemPrincipalConfinementMiddleware>();
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
// Track C (Phase D follow-up) — gate vendor-polling hosted services so
// only the api container hits RIOT3, not the dtms-outbox-worker sibling.
// Default true keeps current api behaviour; the outbox-worker env sets
// Workers__VendorPollers__RunInThisProcess=false so it skips registration
// entirely (no RIOT 401 spam, no doubled vendor call rate, no race on
// the shared VendorHealthStore + Broadcaster).
var runVendorPollers = builder.Configuration
    .GetValue("Workers:VendorPollers:RunInThisProcess", true);

if (runVendorPollers)
{
    builder.Services.AddHostedService<Riot3HealthPollerService>();
}
builder.Services.AddHostedService<InfraHealthPollerService>();   // DB/Redis only — fine to run 2x (idempotent reads)
if (runVendorPollers)
{
    // Broadcaster reads from VendorHealthStore which is populated by the
    // poller above; without that, the broadcaster would push stale data
    // from a never-updated store. Coupled, gated together.
    builder.Services.AddHostedService<VendorHealthBroadcaster>();
}
builder.Services.Configure<VendorHealthWebhookOptions>(
    builder.Configuration.GetSection("VendorHealth:Webhook"));
builder.Services.AddHttpClient(nameof(VendorHealthWebhookNotifier));
if (runVendorPollers)
{
    // Sends webhooks to external systems on health-change events. Doubled
    // = duplicate webhook deliveries to downstream consumers. Must gate.
    builder.Services.AddHostedService<VendorHealthWebhookNotifier>();
}
builder.Services.AddTransient<RiotHealthCheckFromStore>();

// Phase B Step B2 — health-check class reuses the singleton NpgsqlDataSource
// so /health/ready stops opening its own raw connection per probe.
builder.Services.AddSingleton<DTMS.Api.Infrastructure.Health.NpgsqlDataSourceHealthCheck>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is running"))
    .AddCheck<DTMS.Api.Infrastructure.Health.NpgsqlDataSourceHealthCheck>(
        "postgres", tags: ["ready"])
    // G1 Phase 1 — readiness flips to Unhealthy as soon as drain begins,
    // so the K8s service mesh stops routing new traffic. Liveness (/health)
    // stays Healthy so kubelet doesn't restart the draining pod.
    .AddCheck<DTMS.Api.Realtime.Drain.DrainHealthCheck>(
        "drain", tags: ["ready"])
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
    {
        // F3 — bypass infrastructure endpoints. These are probed at fixed
        // intervals by K8s kubelet / service mesh / Prometheus, or carry
        // many clients through a single egress IP (SignalR reconnect
        // storms after a G1 drain). Either case would falsely trip the
        // per-IP limit and break the exact reliability features rate
        // limiting is supposed to protect — readiness flapping, drain
        // reconnects landing on 429 instead of a healthy sibling pod,
        // scrape gaps producing false alerts.
        //
        // Business API paths (/api/*) still rate-limit per IP as before;
        // F3 only carves out infra.
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/hubs") ||
            path.StartsWithSegments("/metrics"))
        {
            return RateLimitPartition.GetNoLimiter("bypass-infra");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rlPermitLimit,
                Window = TimeSpan.FromSeconds(rlWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = rlQueueLimit
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Phase S.8 — fail-fast keypair validation. Singleton resolution is lazy
// by default, so a missing/malformed RSA key would only surface on the
// first /oauth/token call (which might be hours after deploy). Force
// resolution here so a misconfigured keypair throws at startup and the
// container restart loop makes the error obvious in ops dashboards
// rather than silently sitting at "Healthy" until traffic hits.
//
// Both Issuer + Validator must be resolvable — Issuer requires the
// private key, Validator requires the public key. Resolving both also
// asserts they parse as valid PEM.
{
    try
    {
        _ = app.Services.GetRequiredService<DTMS.Iam.Application.Authorization.ISystemJwtIssuer>();
        _ = app.Services.GetRequiredService<DTMS.Iam.Application.Authorization.ISystemJwtValidator>();
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "System JWT keypair failed startup validation. " +
            "Set Jwt__SystemSigningPrivateKey + Jwt__SystemSigningPublicKey env vars " +
            "with a matching PEM-encoded RSA keypair, then restart. " +
            "See docs/system-onboarding.md §4 for openssl commands.");
        throw;
    }
}

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

            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Iam.Infrastructure.Data.IamDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Facility.Infrastructure.Data.FacilityDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Fleet.Infrastructure.Data.FleetDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Planning.Infrastructure.Data.PlanningDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Dispatch.Infrastructure.Data.DispatchDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Transport.Amr.Infrastructure.Data.VendorAdapterDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Transport.Manual.Infrastructure.Data.TransportManualDbContext>(), logger, app.Environment);
            await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<DTMS.Wms.Infrastructure.Data.WmsDbContext>(), logger, app.Environment);

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
    var catalog = scope.ServiceProvider.GetRequiredService<DTMS.Transport.Abstractions.Services.IActionCatalogService>();

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
                new DTMS.Transport.Abstractions.Models.ActionCatalogEntry(vtKey, action, adapterKey, paramsJson));
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
            "Run: dotnet ef migrations add InitialCreate --project <InfraProject> --startup-project src/DTMS.Api");
    }
    else
    {
        logger.LogWarning(
            "{Context} has no EF migrations — using EnsureCreated (dev fallback). " +
            "Run: dotnet ef migrations add InitialCreate --project <InfraProject> --startup-project src/DTMS.Api",
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
    // Scalar API reference — modern docs UI at /scalar/v1. The package
    // is the .NET 9+ minimal-API default replacement for Swagger UI.
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("AMR Delivery Planning API")
               .WithOpenApiRoutePattern("/openapi/v1.json");
    });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "DTMS.Api v1");
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

// Authorization wall — confine system (M2M) principals to the source
// data-plane. Runs right after authentication (ctx.User populated) so any
// system JWT (sub=system:*) presented outside /api/v1/source/* is 403'd
// structurally, before permission checks — a system can never reach the
// control-plane even if it was granted an admin permission by mistake.
app.UseMiddleware<DTMS.Api.Middlewares.SystemPrincipalConfinementMiddleware>();

// Phase S.2 — federated source-system auth + request log. MUST run
// BEFORE UseAuthorization so SystemClientAuthMiddleware can stamp
// context.User with the system principal + permission claims before
// the endpoint policy is evaluated. Branched at the path level so
// user traffic on /api/v1/delivery-orders etc. never pays the
// SystemClient lookup overhead. Auth runs first so SystemPrincipal
// lands in HttpContext.Items before the logging middleware reads it;
// logging stamps duration/status AFTER next() so the row reflects
// the final response.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/v1/source"),
    branch =>
    {
        branch.UseMiddleware<DTMS.Api.Middlewares.SystemClientAuthMiddleware>();
        branch.UseMiddleware<DTMS.Api.Middlewares.SystemRequestLoggingMiddleware>();
    });

app.UseAuthorization();

// Phase 4.2 — Operator PWA sync. Mounted only on /api/operator/* so it
// doesn't run for admin/dispatcher endpoints. Runs after UseAuthorization
// so the OperatorJwt scheme has already validated + populated User
// claims; the middleware then upserts the DTMS-side Operator row.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/operator"),
    branch => branch.UseMiddleware<DTMS.Api.Auth.OperatorSyncMiddleware>());

// PR-H deploy — Prometheus scrape endpoint. Anonymous (Prometheus can't
// carry a bearer through service discovery) and text-format only. On the
// prod cluster this goes behind a firewall / mesh policy so external
// callers can't hit it; locally it's just http://localhost:5219/metrics.
app.MapPrometheusScrapingEndpoint().AllowAnonymous();

// Liveness probe (always 200 if process is up). G1 Phase 1 — excludes the
// "drain" check so a draining pod stays liveness-Healthy and kubelet
// doesn't restart it. (Readiness still flips to 503 — see /health/ready.)
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name != "drain"
}).AllowAnonymous()
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

// Phase S.8 — OAuth 2.0 client_credentials token endpoint for federated
// source systems whose AuthScheme=bearer-jwt. Mounted at /oauth/token (no
// /api/v1 prefix — matches standard OAuth discovery conventions); marked
// AllowAnonymous inside the extension so it bypasses both auth schemes.
app.MapOauthTokenEndpoint();

// Phase S.8d — RFC 7517 JSON Web Key Set. Anonymous, publishes the
// system-JWT public key so partners / gateways can verify DTMS-issued
// tokens without embedding the raw PEM.
app.MapJwksEndpoint();

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
app.MapHub<DTMS.Api.Realtime.Hubs.OrderHub>("/hubs/orders");
app.MapHub<DTMS.Api.Realtime.Hubs.JobHub>("/hubs/jobs");
app.MapHub<DTMS.Api.Realtime.Hubs.TripHub>("/hubs/trips");
app.MapHub<DTMS.Api.Realtime.Hubs.DashboardHub>("/hubs/dashboard");
app.MapHub<DTMS.Api.Realtime.Hubs.FleetHub>("/hubs/fleet");
// Phase 4.6 — dispatcher Manual operator board realtime hints.
app.MapHub<DTMS.Api.Realtime.Hubs.ManualBoardHub>("/hubs/manual-board");
// WMS PR-4b (PR-D) — operator PWA pool broadcast hub. Universal group
// "operator-pool" auto-joined on connect; every connected PWA receives
// PoolTripAdded / PoolTripClaimed / PoolTripRemoved so local lists stay
// in lock-step with the server-side CAS.
app.MapHub<DTMS.Api.Realtime.Hubs.OperatorPoolHub>("/hubs/operator-pool");

// Phase S.2 smoke test endpoint — returns the authenticated source
// system's principal id + display name. Doubles as the "test key" probe
// fired from the admin one-time-secret banner after a client secret is
// minted or rotated.
//
// Phase S.8e (P3) — canonical URL only. The former legacy variant
// /api/v1/source/{key}/whoami was retired; identity comes from the
// JWT sub claim so the URL carries no system slug.
//
// Phase 5 — authentication only, no permission grant. This used to pin
// `dtms:source:oms:order:read`, which 403'd every non-OMS system (grants
// are per-system slugs). No grant is the right gate: reaching this handler
// already proves a valid system JWT + active SystemClient + registered
// credential (SystemClientAuthMiddleware, which also overwrites User so a
// user-JWT can't be replayed here), and the response only ever discloses
// the caller's OWN identity. It is also a *credential* probe — gating it on
// a grant would fail a correctly-provisioned client that simply has not
// been granted order:read.
app.MapGet("/api/v1/source/whoami",
    (HttpContext ctx) =>
    {
        if (ctx.Items[DTMS.Api.Middlewares.SystemClientAuthMiddleware.PrincipalItemKey]
            is not DTMS.Iam.Application.Authorization.SystemPrincipal sp)
            return Results.Unauthorized();
        return Results.Ok(new
        {
            principalId = sp.PrincipalId,
            displayName = sp.DisplayName,
            permissions = ctx.User.FindAll("permission").Select(c => c.Value).ToArray(),
        });
    }).RequireAuthorization();

// (using directive added at the top of Program.cs)

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
