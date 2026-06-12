using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using AMR.DeliveryPlanning.Api.Auth;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using AMR.DeliveryPlanning.Api.RobotPositions;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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
    .WithMetrics(metrics => metrics
        .AddMeter("DTMS.Projection")
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// P0 — projection foundation (idempotency, replay stub, metrics singleton).
// Per-module IProjectionInboxRepository implementations register inside
// each module's own infrastructure registration (next to its DbContext).
builder.Services.AddProjectionFoundation();

// Health checks — /health (liveness), /health/ready (readiness), /health/vendors (external vendors)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var rabbitConfig = builder.Configuration.GetSection("RabbitMq");
var rabbitHost = rabbitConfig["Host"] ?? "localhost";
var rabbitUser = rabbitConfig["Username"] ?? "guest";
var rabbitPass = rabbitConfig["Password"] ?? "guest";
var riot3BaseUrl = builder.Configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:12000";
var riot3ApiKey  = builder.Configuration.GetValue<string>("VendorAdapter:Riot3:ApiKey") ?? string.Empty;

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is running"))
    .AddCheck("postgres", () =>
    {
        try
        {
            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }, tags: ["ready"])
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
    .AddCheck("riot3", (CancellationToken ct) =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = http.GetAsync($"{riot3BaseUrl}/api/v4/maps?pageSize=1", ct).GetAwaiter().GetResult();
            sw.Stop();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return HealthCheckResult.Degraded("RIOT3 reachable but ApiKey is invalid (401)");

            response.EnsureSuccessStatusCode();
            return HealthCheckResult.Healthy($"RIOT3 responded in {sw.ElapsedMilliseconds}ms");
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("RIOT3 connection timed out (>5s)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"RIOT3 unreachable: {ex.Message}");
        }
    }, tags: ["vendors"]);

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

// Apply EF Core migrations for all module databases on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying database migrations...");

    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Facility.Infrastructure.Data.FacilityDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Fleet.Infrastructure.Data.FleetDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Planning.Infrastructure.Data.PlanningDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Dispatch.Infrastructure.Data.DispatchDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AuthDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), logger, app.Environment);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data.VendorAdapterDbContext>(), logger, app.Environment);

    logger.LogInformation("Database migrations applied successfully.");
}

// Seed ActionCatalog defaults (upsert — safe to run every startup)
await SeedActionCatalogAsync(app.Services);

// Seed default admin user
await AuthEndpoints.SeedDefaultUserAsync(app.Services);

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
