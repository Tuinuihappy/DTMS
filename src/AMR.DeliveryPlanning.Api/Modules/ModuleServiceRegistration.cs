using AMR.DeliveryPlanning.Api.Auth;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Services;
using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Services;
using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using AMR.DeliveryPlanning.Facility.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Facility.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using AMR.DeliveryPlanning.Fleet.Application.Consumers;
using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Services;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;

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

        // ── Auth Module ────────────────────────────────────────────────
        services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(connectionString));

        // ── Facility Module ───────────────────────────────────────────
        services.AddDbContext<FacilityDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IMapRepository, MapRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IRouteEdgeRepository, RouteEdgeRepository>();
        services.AddScoped<ITopologyOverlayRepository, TopologyOverlayRepository>();
        services.AddScoped<IFacilityResourceRepository, FacilityResourceRepository>();
        services.AddScoped<IFacilityReadService, FacilityReadService>();
        var riot3BaseUrl = configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:5100";
        var riot3ApiKey = configuration.GetValue<string>("VendorAdapter:Riot3:ApiKey");
        services.AddHttpClient<IFacilityResourceCommandService, Riot3FacilityResourceCommandService>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        });

        // Route edge sync — pulls live costs from RIOT3 and upserts into facility.RouteEdges
        var syncIntervalMinutes = configuration.GetValue<int>("VendorAdapter:Riot3:RouteSync:IntervalMinutes", 30);
        services.AddHttpClient<IRiot3RouteClient, Riot3RouteClient>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        });
        services.AddHostedService(sp => new RouteEdgeSyncService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRiot3RouteClient>(),
            sp.GetRequiredService<ILogger<RouteEdgeSyncService>>(),
            TimeSpan.FromMinutes(syncIntervalMinutes)));

        services.AddHostedService<TopologyOverlayExpiryService>();

        // ── Fleet Module ──────────────────────────────────────────────
        services.AddScoped<FleetDomainEventMapper>();
        services.AddDbContext<FleetDbContext>((sp, o) => o
            .UseNpgsql(connectionString)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<FleetDomainEventMapper>())));
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IVehicleTypeRepository, VehicleTypeRepository>();
        services.AddScoped<IChargingPolicyRepository, ChargingPolicyRepository>();
        services.AddScoped<IMaintenanceRecordRepository, MaintenanceRecordRepository>();
        services.AddScoped<IVehicleGroupRepository, VehicleGroupRepository>();
        services.AddScoped<IFleetReadService, FleetReadService>();
        services.AddScoped<IFleetOutbox, FleetOutbox>();

        // ── DeliveryOrder Module ──────────────────────────────────────
        services.AddScoped<DeliveryOrderDomainEventMapper>();
        services.AddDbContext<DeliveryOrderDbContext>((sp, o) => o
            .UseNpgsql(connectionString)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<DeliveryOrderDomainEventMapper>())));
        services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
        services.AddScoped<IStationLookup, FacilityStationLookup>();
        services.AddScoped<StationValidationService>();
        services.AddScoped<IOrderAmendmentRepository, OrderAmendmentRepository>();
        services.AddScoped<IOrderAuditEventRepository, OrderAuditEventRepository>();
        services.Configure<DeliveryOrderOptions>(
            configuration.GetSection(DeliveryOrderOptions.SectionName));

        // ── Planning Module ───────────────────────────────────────────
        services.AddScoped<PlanningDomainEventMapper>();
        services.AddDbContext<PlanningDbContext>((sp, o) => o
            .UseNpgsql(connectionString)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<PlanningDomainEventMapper>())));
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ICostModelService, DbCostModelService>();
        services.AddScoped<IVehicleSelector, GreedyVehicleSelector>();
        services.AddScoped<SimpleRouteCostCalculator>();
        services.AddScoped<IRouteCostCalculator, CachedRouteCostCalculator>();
        services.AddScoped<IPatternClassifier, PatternClassifier>();
        services.AddScoped<IRouteSolver, NearestNeighborTspSolver>();
        services.AddScoped<IFleetVehicleProvider, FleetVehicleProvider>();
        services.AddHostedService<SlaRiskBackgroundService>();

        // ── Dispatch Module ───────────────────────────────────────────
        services.AddScoped<DispatchDomainEventMapper>();
        services.AddDbContext<DispatchDbContext>((sp, o) => o
            .UseNpgsql(connectionString)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<DispatchDomainEventMapper>())));
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<ITaskDispatcher, VendorAdapterTaskDispatcher>();

        // ── VendorAdapter Module ──────────────────────────────────────
        services.AddVendorAdapterInfrastructure(configuration);

        // ── MassTransit + RabbitMQ ────────────────────────────────────
        services.AddMassTransit(bus =>
        {
            // Auto-scan consumers from all module Application assemblies
            bus.AddConsumers(
                typeof(AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly,
                typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly,
                typeof(AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip.DispatchTripCommand).Assembly,
                typeof(VehicleStateChangedConsumer).Assembly
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

        // Use OutboxEventBus for at-least-once delivery guarantee
        services.AddScoped<IEventBus, OutboxEventBus>();

        // Outbox infrastructure
        services.AddDbContext<OutboxDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IOutboxProcessor, OutboxProcessorService>();
        services.AddHostedService<OutboxProcessorService>();

        // Redis distributed cache
        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);

        return services;
    }
}
