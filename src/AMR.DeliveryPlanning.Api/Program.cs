using System.Text;
using System.Threading.RateLimiting;
using AMR.DeliveryPlanning.Api.Auth;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();

// Configure JWT Authentication
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

// Configure MediatR — scan all module Application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Facility.Application.Queries.GetRouteCost.GetRouteCostQuery).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Fleet.Application.Consumers.VehicleStateChangedConsumer).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip.DispatchTripCommand).Assembly);
});

// Register all module services (DbContexts, Repositories, Domain Services, HttpClients)
builder.Services.AddAllModules(builder.Configuration);

// OpenTelemetry
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("AMR.DeliveryPlanning.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// Health checks — /health (liveness) and /health/ready (readiness with dependency probes)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

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
    }, failureStatus: HealthStatus.Degraded, tags: ["ready"]);

// Rate limiting — fixed window, 100 req/min, applied globally
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Apply EF Core migrations for all module databases on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying database migrations...");

    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Facility.Infrastructure.Data.FacilityDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Fleet.Infrastructure.Data.FleetDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Planning.Infrastructure.Data.PlanningDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Dispatch.Infrastructure.Data.DispatchDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AuthDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<OutboxDbContext>(), logger);
    await ApplyMigrationsAsync(scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data.VendorAdapterDbContext>(), logger);

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

static async Task ApplyMigrationsAsync(DbContext db, Microsoft.Extensions.Logging.ILogger logger)
{
    var dbName = db.GetType().Name;

    // If migrations have been scaffolded for this context, apply them.
    // If not yet scaffolded (empty Migrations assembly), fall back to EnsureCreated
    // so local dev still works. Run 'dotnet ef migrations add InitialCreate' per module
    // to graduate to the proper migration path.
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
    else
    {
        logger.LogWarning(
            "{Context} has no EF migrations — using EnsureCreated (dev fallback). " +
            "Run: dotnet ef migrations add InitialCreate --project <InfraProject> --startup-project src/AMR.DeliveryPlanning.Api",
            dbName);
        await db.Database.EnsureCreatedAsync();
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
app.MapHealthChecks("/health").AllowAnonymous();
// Readiness probe (checks postgres, rabbitmq, redis)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// Map auth endpoint (anonymous)
app.MapAuthEndpoints();

// Map all module Minimal API endpoints (require auth)
app.MapAllModuleEndpoints();

app.Run();
