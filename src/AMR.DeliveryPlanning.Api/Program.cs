using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddOpenApi();

// Configure MediatR — scan all module Application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Facility.Application.Class1).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Fleet.Application.Class1).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip.DispatchTripCommand).Assembly);
});

// Register all module services (DbContexts, Repositories, Domain Services, HttpClients)
builder.Services.AddAllModules(builder.Configuration);

var app = builder.Build();

// Auto-migrate all module databases on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying database migrations...");

    // 1. Facility — creates the DB + its tables
    var facilityDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Facility.Infrastructure.Data.FacilityDbContext>();
    await facilityDb.Database.EnsureCreatedAsync();

    // 2. Fleet
    var fleetDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Fleet.Infrastructure.Data.FleetDbContext>();
    await CreateSchemaAndTables(fleetDb, "fleet", logger);

    // 3. DeliveryOrder
    var deliveryOrderDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>();
    await CreateSchemaAndTables(deliveryOrderDb, "deliveryorder", logger);

    // 4. Planning
    var planningDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Planning.Infrastructure.Data.PlanningDbContext>();
    await CreateSchemaAndTables(planningDb, "planning", logger);

    // 5. Dispatch
    var dispatchDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Dispatch.Infrastructure.Data.DispatchDbContext>();
    await CreateSchemaAndTables(dispatchDb, "dispatch", logger);

    logger.LogInformation("Database migrations applied successfully.");
}

static async Task CreateSchemaAndTables(DbContext db, string schemaName, Microsoft.Extensions.Logging.ILogger logger)
{
    try
    {
        // Create schema — using NpgsqlCommand directly to avoid EF SQL injection checks
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"";
        await cmd.ExecuteNonQueryAsync();

        // Create tables from model
        var creator = db.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync();
        logger.LogInformation("Created tables for schema {Schema}", schemaName);
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
    {
        logger.LogInformation("Tables for schema {Schema} already exist, skipping.", schemaName);
    }
    finally
    {
        await db.Database.GetDbConnection().CloseAsync();
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

// Map all module Minimal API endpoints
app.MapAllModuleEndpoints();

app.Run();
