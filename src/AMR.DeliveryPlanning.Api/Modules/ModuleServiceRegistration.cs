using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.Modules;

/// <summary>
/// Registers all module services (DbContexts, Repositories, Domain Services) into the DI container.
/// Each module gets its own DbContext with a separate schema for clean separation.
/// </summary>
public static class ModuleServiceRegistration
{
    public static IServiceCollection AddAllModules(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // ── Facility Module ───────────────────────────────────────────
        services.AddDbContext<FacilityDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IMapRepository, MapRepository>();

        // ── Fleet Module ──────────────────────────────────────────────
        services.AddDbContext<FleetDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IVehicleTypeRepository, VehicleTypeRepository>();

        // ── DeliveryOrder Module ──────────────────────────────────────
        services.AddDbContext<DeliveryOrderDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();

        // ── Planning Module ───────────────────────────────────────────
        services.AddDbContext<PlanningDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IVehicleSelector, GreedyVehicleSelector>();
        services.AddScoped<IRouteCostCalculator, SimpleRouteCostCalculator>();
        services.AddScoped<IPatternClassifier, PatternClassifier>();
        services.AddScoped<IRouteSolver, NearestNeighborTspSolver>();

        // ── Dispatch Module ───────────────────────────────────────────
        services.AddDbContext<DispatchDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<ITripRepository, TripRepository>();

        // ── VendorAdapter Module ──────────────────────────────────────
        services.AddVendorAdapterInfrastructure(configuration);

        // ── MassTransit + RabbitMQ ────────────────────────────────────
        services.AddMassTransit(bus =>
        {
            // Auto-scan consumers from all module Application assemblies
            bus.AddConsumers(
                typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly,
                typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly,
                typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip.DispatchTripCommand).Assembly
            );

            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbitConfig = configuration.GetSection("RabbitMq");
                cfg.Host(rabbitConfig["Host"] ?? "localhost", h =>
                {
                    h.Username(rabbitConfig["Username"] ?? "guest");
                    h.Password(rabbitConfig["Password"] ?? "guest");
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IEventBus, MassTransitEventBus>();

        return services;
    }
}
