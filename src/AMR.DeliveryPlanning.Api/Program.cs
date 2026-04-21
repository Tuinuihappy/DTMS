using AMR.DeliveryPlanning.Api.Middlewares;
using AMR.DeliveryPlanning.Api.Modules;
using Scalar.AspNetCore;
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

    var facilityDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Facility.Infrastructure.Data.FacilityDbContext>();
    await facilityDb.Database.EnsureCreatedAsync();

    var fleetDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Fleet.Infrastructure.Data.FleetDbContext>();
    await fleetDb.Database.EnsureCreatedAsync();

    var deliveryOrderDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data.DeliveryOrderDbContext>();
    await deliveryOrderDb.Database.EnsureCreatedAsync();

    var planningDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Planning.Infrastructure.Data.PlanningDbContext>();
    await planningDb.Database.EnsureCreatedAsync();

    var dispatchDb = scope.ServiceProvider.GetRequiredService<AMR.DeliveryPlanning.Dispatch.Infrastructure.Data.DispatchDbContext>();
    await dispatchDb.Database.EnsureCreatedAsync();

    logger.LogInformation("Database migrations applied successfully.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

// Map all module Minimal API endpoints
app.MapAllModuleEndpoints();

app.Run();
