using AMR.DeliveryPlanning.Api.Auth;
using AMR.DeliveryPlanning.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.DeliveryOrder.Infrastructure.Repositories;
using DTMS.DeliveryOrder.Infrastructure.Services;
using DTMS.DeliveryOrder.Presentation.Idempotency;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Repositories;
using DTMS.Dispatch.Infrastructure.Services;
using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Repositories;
using DTMS.Facility.Domain.Services;
using DTMS.Facility.Infrastructure.Data;
using DTMS.Facility.Infrastructure.Repositories;
using DTMS.Facility.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using DTMS.Fleet.Application.Consumers;
using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using DTMS.Fleet.Infrastructure.Repositories;
using DTMS.Fleet.Infrastructure.Services;
using FleetServices = DTMS.Fleet.Infrastructure.Services;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Repositories;
using AMR.DeliveryPlanning.Planning.Infrastructure.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;
using AMR.DeliveryPlanning.OmsAdapter;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Exceptions;
using DTMS.Transport.Amr.Infrastructure;
using DTMS.Transport.Amr.Infrastructure.Extensions;
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
    // Centralised Npgsql configuration applied to every DbContext below.
    // EnableRetryOnFailure swallows transient connection drops at the
    // EF Core level so a brief DB blip becomes a 1-2s delay instead of
    // an exception bubbled to the request handler. maxRetryDelay is
    // intentionally short (10s) so requests fail fast rather than
    // hanging the thread pool when the DB is genuinely unavailable.
    //
    // IMPORTANT: any code that calls db.Database.BeginTransactionAsync()
    // MUST wrap the transaction in db.Database.CreateExecutionStrategy()
    // — otherwise EF Core throws because the retry strategy cannot
    // safely replay a user-initiated transaction. See OutboxProcessor
    // (SKIP LOCKED path) and Facility RouteEdgeSyncService for examples.
    private static void ConfigureNpgsql(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }

    // Manual migrations diverge from EF's auto-detected model whenever a
    // hand-written migration encodes behaviour the model differ can't see
    // (Sql() statements for backfill, multi-step column moves like Phase 3b's
    // AmrTripExtension split, etc.). EF Core 8+ throws on any divergence by
    // default; log it instead so the migrator can apply our hand-authored
    // migration list and the DBA / engineer sees the warning without an
    // outage. See `feedback_migration_manual.md` for the broader manual-
    // migration convention.
    private static void SuppressPendingModelChangesWarning(
        Microsoft.EntityFrameworkCore.Diagnostics.WarningsConfigurationBuilder warnings)
    {
        warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
    }

    public static IServiceCollection AddAllModules(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Track C (Phase D follow-up) — gate the vendor-polling background
        // services so they only run in the api container, not the
        // dtms-outbox-worker sibling. See top-level Program.cs for the
        // full rationale; this flag is read in three places that all
        // register pollers calling out to RIOT3.
        var runVendorPollers = configuration
            .GetValue("Workers:VendorPollers:RunInThisProcess", true);

        // Phase B Step B1 — single NpgsqlDataSource shared by every DbContext
        // below. Before: each AddDbContext(UseNpgsql(connString)) implicitly
        // used Npgsql's static pool keyed by connection string — 9 DbContexts
        // × default 100 = potentially 900 connections under load, which the
        // alpine default max_connections=100 couldn't survive (2026-06-16
        // perf REPORT finding #2). After: ONE pool, shared across all 9
        // DbContexts + the /health probe, capped at MaxPoolSize on the
        // connection string. EnableDynamicJson keeps the existing jsonb
        // columns (Dispatch + DeliveryOrder + Planning module) round-tripping
        // through System.Text.Json the same way they do today.
        var npgsqlDataSource = new Npgsql.NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();
        services.AddSingleton<Npgsql.NpgsqlDataSource>(npgsqlDataSource);

        // ── Auth Module ────────────────────────────────────────────────
        services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(npgsqlDataSource, ConfigureNpgsql));

        // ── Facility Module ───────────────────────────────────────────
        services.AddDbContext<FacilityDbContext>(o => o.UseNpgsql(npgsqlDataSource, ConfigureNpgsql));
        services.AddScoped<IMapRepository, MapRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IRouteEdgeRepository, RouteEdgeRepository>();
        services.AddScoped<ITopologyOverlayRepository, TopologyOverlayRepository>();
        services.AddScoped<IFacilityResourceRepository, FacilityResourceRepository>();
        services.AddScoped<IShelfRepository, ShelfRepository>();
        services.AddScoped<ICarrierTypeProfileRepository, CarrierTypeProfileRepository>();
        services.AddScoped<ILoadUnitProfileRepository, LoadUnitProfileRepository>();
        // Phase 2.6 — Warehouse aggregate persistence + lookup. The
        // IWarehouseLookup binding (in DeliveryOrder module section below)
        // depends on IFacilityReadService being registered, which it is.
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IFacilityReadService, FacilityReadService>();
        var riot3BaseUrl = configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:5100";
        var riot3ApiKey = configuration.GetValue<string>("VendorAdapter:Riot3:ApiKey");
        services.AddHttpClient<IFacilityResourceCommandService, Riot3FacilityResourceCommandService>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Route edge sync — pulls live costs from RIOT3 and upserts into facility.RouteEdges
        var syncIntervalMinutes = configuration.GetValue<int>("VendorAdapter:Riot3:RouteSync:IntervalMinutes", 30);
        services.AddHttpClient<IRiot3RouteClient, Riot3RouteClient>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());
        services.AddHttpClient<DTMS.Facility.Application.Services.IRiot3FacilityClient, Riot3FacilityClient>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());
        // MapStationSync runs at 15s delay; RouteEdgeSync runs at 2min delay to ensure stations are synced first.
        // MapStationSync now dispatches SyncMapStationsCommand per map — same code path as the manual endpoint.
        // Track C: both poll RIOT3 — gated so only the api container runs them.
        if (runVendorPollers)
        {
            services.AddHostedService(sp => new MapStationSyncService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<MapStationSyncService>>(),
                TimeSpan.FromMinutes(syncIntervalMinutes)));
            services.AddHostedService(sp => new RouteEdgeSyncService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IRiot3RouteClient>(),
                sp.GetRequiredService<ILogger<RouteEdgeSyncService>>(),
                TimeSpan.FromMinutes(syncIntervalMinutes)));
        }

        services.AddHostedService<TopologyOverlayExpiryService>();

        // ── Fleet Module ──────────────────────────────────────────────
        services.AddScoped<FleetDomainEventMapper>();
        services.AddDbContext<FleetDbContext>((sp, o) => o
            .UseNpgsql(npgsqlDataSource, ConfigureNpgsql)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<FleetDomainEventMapper>())));
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        // Phase P3.2 — Fleet projections (vehicle state history + utilization snapshots).
        services.AddScoped<DTMS.Fleet.Application.Projections.IVehicleStateHistoryProjectionStore,
                           DTMS.Fleet.Infrastructure.Projections.VehicleStateHistoryProjectionStore>();
        services.AddScoped<DTMS.Fleet.Application.Projections.IFleetUtilizationReadRepository,
                           DTMS.Fleet.Infrastructure.Projections.FleetUtilizationReadRepository>();
        services.AddScoped<DTMS.Fleet.Application.Projections.IFleetUtilizationSnapshotWriter,
                           DTMS.Fleet.Infrastructure.Projections.FleetUtilizationSnapshotWriter>();
        services.AddScoped<IVehicleTypeRepository, VehicleTypeRepository>();
        services.AddScoped<IChargingPolicyRepository, ChargingPolicyRepository>();
        services.AddScoped<IMaintenanceRecordRepository, MaintenanceRecordRepository>();
        services.AddScoped<IVehicleGroupRepository, VehicleGroupRepository>();
        services.AddScoped<IFleetReadService, FleetReadService>();
        services.AddScoped<IFleetOutbox, FleetOutbox>();
        services.AddHttpClient<IRiot3FleetClient, FleetServices.Riot3FleetClient>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Live robot positions — singleton store fed by a 1 Hz poller against
        // RIOT3. The map page polls the store (not RIOT3 directly) so the
        // upstream load stays at one request per second regardless of UI fan-out.
        // Track C: the STORE stays registered (DI consumers in the worker
        // would fail to resolve otherwise), but the poller itself is gated
        // — worker container's store stays empty, which is fine because no
        // map UI is served from the worker.
        services.AddSingleton<AMR.DeliveryPlanning.Api.RobotPositions.IRobotPositionStore,
                              AMR.DeliveryPlanning.Api.RobotPositions.InMemoryRobotPositionStore>();
        if (runVendorPollers)
        {
            services.AddHostedService<AMR.DeliveryPlanning.Api.RobotPositions.Riot3PositionPollerService>();
        }

        // ── Transport.Manual Module (Phase 4.1 + 4.2) ─────────────────
        // 4.1: DbContext + Domain.
        // 4.2: Operator repositories + sync service + scoped sync
        //      middleware that the /api/operator/* pipeline runs.
        // PendingModelChangesWarning is silenced because migrations are
        // hand-written (per feedback_migration_manual memory) and EF's
        // model differ throws otherwise.
        services.AddDbContext<DTMS.Transport.Manual.Infrastructure.Data.TransportManualDbContext>((_, o) => o
            .UseNpgsql(npgsqlDataSource, ConfigureNpgsql)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<DTMS.Transport.Manual.Domain.Repositories.IOperatorRepository,
                           DTMS.Transport.Manual.Infrastructure.Repositories.OperatorRepository>();
        services.AddScoped<DTMS.Transport.Manual.Domain.Repositories.IGeofenceOverrideRequestRepository,
                           DTMS.Transport.Manual.Infrastructure.Repositories.GeofenceOverrideRequestRepository>();
        services.AddScoped<DTMS.Transport.Manual.Domain.Repositories.IManualTripExtensionRepository,
                           DTMS.Transport.Manual.Infrastructure.Repositories.ManualTripExtensionRepository>();
        services.AddScoped<DTMS.Transport.Manual.Application.Services.IOperatorSyncService,
                           DTMS.Transport.Manual.Application.Services.OperatorSyncService>();
        services.AddScoped<AMR.DeliveryPlanning.Api.Auth.OperatorSyncMiddleware>();

        // Phase 4.3 — Object storage (MinIO) for POD photos (ADR-015).
        services.Configure<DTMS.Transport.Manual.Infrastructure.Storage.ObjectStorageOptions>(
            configuration.GetSection(DTMS.Transport.Manual.Infrastructure.Storage.ObjectStorageOptions.SectionName));
        services.AddSingleton<DTMS.Transport.Manual.Application.Services.IObjectStorageService,
                              DTMS.Transport.Manual.Infrastructure.Storage.MinioObjectStorageService>();
        services.AddSingleton<DTMS.Transport.Manual.Application.Services.IPodBucketProvider>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
                DTMS.Transport.Manual.Infrastructure.Storage.ObjectStorageOptions>>().Value;
            return new PodBucketProvider(opts.PodBucket);
        });
        services.AddHostedService<DTMS.Transport.Manual.Infrastructure.Storage.ObjectStorageBucketInitializer>();

        // Phase 4.3 — Web Push gateway (VAPID, ADR-013).
        services.Configure<DTMS.Transport.Manual.Infrastructure.Push.VapidOptions>(
            configuration.GetSection(DTMS.Transport.Manual.Infrastructure.Push.VapidOptions.SectionName));
        services.AddSingleton<DTMS.Transport.Manual.Application.Services.IVapidPublicKeyProvider>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
                DTMS.Transport.Manual.Infrastructure.Push.VapidOptions>>().Value;
            return new VapidPublicKeyProvider(opts.PublicKey);
        });
        services.AddScoped<DTMS.Transport.Manual.Application.Services.IPushNotificationGateway,
                           DTMS.Transport.Manual.Infrastructure.Push.WebPushGateway>();

        // Phase 4.4 — Operator assignment policy + ManualDispatchStrategy
        // bindings. The strategy itself is registered alongside the AMR
        // strategy further down (look for IDispatchStrategy registrations).
        services.Configure<DTMS.Transport.Manual.Application.Services.ManualDispatchOptions>(
            configuration.GetSection(
                DTMS.Transport.Manual.Application.Services.ManualDispatchOptions.SectionName));
        services.AddScoped<DTMS.Transport.Manual.Application.Services.IOperatorAssignmentPolicy,
                           DTMS.Transport.Manual.Application.Services.WarehouseAwareOperatorAssignmentPolicy>();

        // ── DeliveryOrder Module ──────────────────────────────────────
        services.AddScoped<DeliveryOrderDomainEventMapper>();
        services.AddDbContext<DeliveryOrderDbContext>((sp, o) => o
            .UseNpgsql(npgsqlDataSource, ConfigureNpgsql)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                new AuditSaveChangesInterceptor(),
                new DomainEventOutboxSaveChangesInterceptor(
                    sp.GetRequiredService<DeliveryOrderDomainEventMapper>())));
        services.AddHttpContextAccessor();
        services.AddScoped<DTMS.DeliveryOrder.Application.Services.ICurrentUserAccessor,
                           AMR.DeliveryPlanning.Api.Auth.HttpContextCurrentUserAccessor>();
        services.AddScoped<AMR.DeliveryPlanning.Planning.Application.Services.ICurrentUserAccessor,
                           AMR.DeliveryPlanning.Api.Auth.HttpContextCurrentUserAccessor>();
        services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
        services.AddScoped<FacilityStationLookup>();
        services.AddScoped<IStationLookup, CachedStationLookup>();
        services.AddScoped<IStationValidationService, StationValidationService>();
        // Phase 2.6 — Warehouse lookup adapter (DeliveryOrder.Application
        // contract → Facility module's read service). Not yet consumed by
        // order validation (MarkAsValidated still uses stations only);
        // Phase 4 Manual mode will inject this for operator scope checks.
        services.AddScoped<IWarehouseLookup, FacilityWarehouseLookup>();
        services.AddScoped<IOrderAmendmentRepository, OrderAmendmentRepository>();
        services.AddScoped<IOrderAuditEventRepository, OrderAuditEventRepository>();
        // Phase P5.3 — Dispatch-side bridge so the vendor adapter can
        // populate TripStartedIntegrationEvent.Items without taking a
        // hard dependency on DeliveryOrderDbContext. Implementation lives
        // here (in DeliveryOrder.Infrastructure) where the data is.
        services.AddScoped<DTMS.Dispatch.Domain.Services.ITripItemSnapshotProvider,
                           DTMS.DeliveryOrder.Infrastructure.Services.DeliveryOrderTripItemSnapshotProvider>();
        // Phase P1 — projection infrastructure for the DeliveryOrder module.
        // Read repo serves the status-history query endpoint.
        // Projection store backs OrderStatusHistoryProjector: combines inbox
        // dedup + history write + SaveChanges into a single transaction.
        // Both are concrete-typed per module so adding the Planning + Dispatch
        // analogs in upcoming P1 work won't conflict in DI.
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderStatusHistoryReadRepository,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderStatusHistoryReadRepository>();
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderStatusHistoryProjectionStore,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderStatusHistoryProjectionStore>();
        // Phase P1 — SignalR-backed realtime publisher. Projector pushes to
        // OrderHub's "order:{id:N}" group after each successful timeline row.
        services.AddSingleton<DTMS.DeliveryOrder.Application.Projections.IOrderRealtimePublisher,
                              AMR.DeliveryPlanning.Api.Realtime.Publishers.SignalROrderRealtimePublisher>();
        // Phase P2 — unified order activity timeline projection.
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderActivityReadRepository,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderActivityReadRepository>();
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderActivityProjectionStore,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderActivityProjectionStore>();
        // Phase P3 — hour-bucketed order funnel projection (dashboard).
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderFunnelReadRepository,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderFunnelReadRepository>();
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderFunnelProjectionStore,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderFunnelProjectionStore>();
        // Phase P3 — dashboard realtime publisher. OrderFunnelProjector
        // enqueues "data changed" hints via this interface; the
        // composition-root implementation forwards into the existing
        // DashboardCounterBatcher (P0.B11) so frontend receives one
        // CountersUpdated push per board per 250 ms window.
        services.AddSingleton<DTMS.DeliveryOrder.Application.Projections.IDashboardRealtimePublisher,
                              AMR.DeliveryPlanning.Api.Realtime.Publishers.BatchedDashboardRealtimePublisher>();
        // Phase P4 — denormalized order list/search view projection.
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderListViewReadRepository,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderListViewReadRepository>();
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderListViewProjectionStore,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderListViewProjectionStore>();
        // Phase P5 — BI fact table for reports (bi.OrderFacts).
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderFactsReadRepository,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderFactsReadRepository>();
        services.AddScoped<DTMS.DeliveryOrder.Application.Projections.IOrderFactsProjectionStore,
                           DTMS.DeliveryOrder.Infrastructure.Projections.OrderFactsProjectionStore>();
        services.Configure<DeliveryOrderOptions>(
            configuration.GetSection(DeliveryOrderOptions.SectionName));
        services.Configure<UomOptions>(configuration.GetSection(UomOptions.SectionName));
        services.AddSingleton<IUomNormalizer, UomNormalizer>();

        // ── Planning Module ───────────────────────────────────────────
        services.AddScoped<PlanningDomainEventMapper>();
        services.AddDbContext<PlanningDbContext>((sp, o) => o
            .UseNpgsql(npgsqlDataSource, ConfigureNpgsql)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<PlanningDomainEventMapper>())));
        services.AddScoped<IJobRepository, JobRepository>();
        // Phase P1 — projection infrastructure for the Planning module.
        // Same shape as DeliveryOrder's wiring: store + read repo per module.
        services.AddSingleton<AMR.DeliveryPlanning.Planning.Application.Projections.IJobRealtimePublisher,
                              AMR.DeliveryPlanning.Api.Realtime.Publishers.SignalRJobRealtimePublisher>();
        services.AddScoped<AMR.DeliveryPlanning.Planning.Application.Projections.IJobStatusHistoryReadRepository,
                           AMR.DeliveryPlanning.Planning.Infrastructure.Projections.JobStatusHistoryReadRepository>();
        services.AddScoped<AMR.DeliveryPlanning.Planning.Application.Projections.IJobStatusHistoryProjectionStore,
                           AMR.DeliveryPlanning.Planning.Infrastructure.Projections.JobStatusHistoryProjectionStore>();
        // Phase P5.2 — BI fact table for jobs (bi.JobFacts).
        services.AddScoped<AMR.DeliveryPlanning.Planning.Application.Projections.IJobFactsReadRepository,
                           AMR.DeliveryPlanning.Planning.Infrastructure.Projections.JobFactsReadRepository>();
        services.AddScoped<AMR.DeliveryPlanning.Planning.Application.Projections.IJobFactsProjectionStore,
                           AMR.DeliveryPlanning.Planning.Infrastructure.Projections.JobFactsProjectionStore>();
        services.AddScoped<IActionTemplateRepository, ActionTemplateRepository>();
        services.AddScoped<IOrderTemplateRepository, OrderTemplateRepository>();
        services.AddScoped<IOrderTemplateResolver, OrderTemplateResolver>();
        // Vendor seam: swap to a no-op adapter when VendorAdapter:Riot3:Enabled=false
        // so load tests / dev scenarios can drive orders through the full DTMS
        // pipeline to "Dispatched" without contacting the real RIOT3 cluster.
        // Default true — production safety, opt-out explicitly. The NoOp adapter
        // logs every skip at INF so it's never silent.
        var riot3Enabled = configuration.GetValue<bool>("VendorAdapter:Riot3:Enabled", true);
        if (riot3Enabled)
            services.AddScoped<IRobotOrderDispatcher, AMR.DeliveryPlanning.Api.Adapters.Riot3OrderDispatcherAdapter>();
        else
            services.AddScoped<IRobotOrderDispatcher, AMR.DeliveryPlanning.Api.Adapters.NoOpOrderDispatcherAdapter>();

        // Log which vendor adapters got picked at boot so it's visible without
        // having to trigger an order first. Single line at INF level.
        services.AddHostedService<AMR.DeliveryPlanning.Api.Adapters.CompositionLogger>();
        services.AddScoped<DTMS.Dispatch.Application.Services.IVendorEnvelopeOperationService,
            AMR.DeliveryPlanning.Api.Adapters.Riot3VendorEnvelopeOperationAdapter>();
        services.AddScoped<DTMS.Dispatch.Application.Services.IVendorRobotOperationService,
            AMR.DeliveryPlanning.Api.Adapters.Riot3VendorRobotOperationAdapter>();

        // ── Phase 1 foundation: strategy registry + vendor operations router ──
        // Both auto-discover their adapters via IEnumerable<> injection. Adding
        // Manual / Fleet later = register the new adapter + (optionally) its
        // IDispatchStrategy — registry + router pick them up without changes
        // here. The router is what Pause/Resume/Cancel handlers will use once
        // Phase 3 refactors them away from the static IVendorEnvelopeOperationService
        // binding above; for now both registrations co-exist (no behaviour change).
        services.AddScoped<DTMS.Dispatch.Application.Services.IDispatchStrategyRegistry,
            DTMS.Dispatch.Application.Services.DispatchStrategyRegistry>();
        services.AddScoped<DTMS.Dispatch.Application.Services.IVendorOperationsRouter,
            DTMS.Dispatch.Application.Services.VendorOperationsRouter>();
        // Phase 3c — AMR strategy wired into production. Routes through
        // IDispatchOrderTemplateService.DispatchByRouteAsync (the existing
        // OrderTemplate → RIOT3 envelope flow) but goes through the strategy
        // contract so Manual / Fleet can plug their own implementations in.
        services.AddScoped<DTMS.Dispatch.Application.Services.IDispatchStrategy,
            AMR.DeliveryPlanning.Api.Adapters.AmrDispatchStrategy>();
        // Phase 3c — Manual strategy stub. Returns Failure with a clear
        // "not yet implemented" reason so Manual orders end up at Failed
        // (visible in the UI) instead of Confirmed-forever. Phase 4 swaps
        // the body for the real operator-assignment flow.
        services.AddScoped<DTMS.Dispatch.Application.Services.IDispatchStrategy,
            AMR.DeliveryPlanning.Api.Adapters.ManualDispatchStrategy>();
        services.AddScoped<IDispatchOrderTemplateService, DispatchOrderTemplateService>();
        services.Configure<AMR.DeliveryPlanning.Planning.Application.Options.DispatchOptions>(
            configuration.GetSection(AMR.DeliveryPlanning.Planning.Application.Options.DispatchOptions.SectionName));
        services.AddScoped<ICostModelService, DbCostModelService>();
        services.AddScoped<IVehicleSelector, GreedyVehicleSelector>();
        services.AddScoped<SimpleRouteCostCalculator>();
        services.AddScoped<IRouteCostCalculator, CachedRouteCostCalculator>();
        services.AddScoped<IPatternClassifier, PatternClassifier>();
        services.AddScoped<IRouteSolver, NearestNeighborTspSolver>();
        services.AddScoped<IFleetVehicleProvider, FleetVehicleProvider>();
        services.AddHostedService<SlaRiskBackgroundService>();
        // Phase P3.2 — hourly fleet utilization snapshot (ticks every minute,
        // writes to FleetUtilizationHourly).
        services.AddHostedService<AMR.DeliveryPlanning.Api.Infrastructure.FleetUtilizationSnapshotService>();

        // ── Dispatch Module ───────────────────────────────────────────
        services.AddScoped<DispatchDomainEventMapper>();
        services.AddDbContext<DispatchDbContext>((sp, o) => o
            .UseNpgsql(npgsqlDataSource, ConfigureNpgsql)
            .ConfigureWarnings(SuppressPendingModelChangesWarning)
            .AddInterceptors(new DomainEventOutboxSaveChangesInterceptor(
                sp.GetRequiredService<DispatchDomainEventMapper>())));
        services.AddScoped<ITripRepository, TripRepository>();
        // Phase P1 — projection infrastructure for the Dispatch module.
        services.AddSingleton<DTMS.Dispatch.Application.Projections.ITripRealtimePublisher,
                              AMR.DeliveryPlanning.Api.Realtime.Publishers.SignalRTripRealtimePublisher>();
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripStatusHistoryReadRepository,
                           DTMS.Dispatch.Infrastructure.Projections.TripStatusHistoryReadRepository>();
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripStatusHistoryProjectionStore,
                           DTMS.Dispatch.Infrastructure.Projections.TripStatusHistoryProjectionStore>();
        // Phase P5.2 — BI fact table for trips (bi.TripFacts).
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripFactsReadRepository,
                           DTMS.Dispatch.Infrastructure.Projections.TripFactsReadRepository>();
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripFactsProjectionStore,
                           DTMS.Dispatch.Infrastructure.Projections.TripFactsProjectionStore>();
        // Phase P5.3 — TripItems read model (Trip ↔ Item binding).
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripItemsReadRepository,
                           DTMS.Dispatch.Infrastructure.Projections.TripItemsReadRepository>();
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripItemsProjectionStore,
                           DTMS.Dispatch.Infrastructure.Projections.TripItemsProjectionStore>();
        // Operator Trips list (GET /api/v1/dispatch/trips). Reads dispatch.Trips
        // joined to dispatch.TripItems for the OrderRef column.
        services.AddScoped<DTMS.Dispatch.Application.Projections.ITripQueueReadRepository,
                           DTMS.Dispatch.Infrastructure.Projections.TripQueueReadRepository>();
        services.AddScoped<DTMS.Dispatch.Domain.Repositories.ITripRetryEventRepository,
            DTMS.Dispatch.Infrastructure.Repositories.TripRetryEventRepository>();
        services.AddScoped<DTMS.Dispatch.Domain.Repositories.ITripMissionEventRepository,
            DTMS.Dispatch.Infrastructure.Repositories.TripMissionEventRepository>();
        services.AddScoped<DTMS.Dispatch.Application.Services.ITripRetryDispatcher,
            AMR.DeliveryPlanning.Api.Adapters.PlanningTripRetryDispatcher>();
        // Composition-root seam — lets ReissueTripCommandHandler check the
        // parent Order's status without taking a direct ref on
        // DeliveryOrder.Application. Fixes the scenario-5 bug where a
        // Cancelled-order's Trip could still be retried.
        services.AddScoped<DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader,
            AMR.DeliveryPlanning.Api.Adapters.DeliveryOrderStatusReader>();
        services.AddScoped<IShelfManifestRepository, ShelfManifestRepository>();

        // ── VendorAdapter Module ──────────────────────────────────────
        services.AddVendorAdapterInfrastructure(configuration);

        // ── OmsAdapter Module ─────────────────────────────────────────
        // Outbound notifications to upstream OMS (POST /api/shipments).
        // Consumer wiring lands in Phase 2; Phase 1 just registers the
        // HTTP client so DI graph is satisfied.
        services.AddOmsAdapter(configuration);

        // ── MassTransit + RabbitMQ ────────────────────────────────────
        // T1.3 — MassTransitHostOptions controls how the bus integrates with
        // the host lifetime. WaitUntilStarted blocks the host's startup until
        // the bus is connected (so we don't serve traffic before consumers
        // are ready); StopTimeout=45s gives in-flight consumers a budget to
        // finish before the broker connection closes. Total shutdown window
        // is ShutdownTimeout (60s, set in Program.cs) ≥ StopTimeout (45s)
        // ≥ docker stop_grace_period (90s) so each layer drains in order.
        services.AddOptions<MassTransit.MassTransitHostOptions>()
            .Configure(o =>
            {
                o.WaitUntilStarted = true;
                o.StartTimeout = TimeSpan.FromSeconds(30);
                o.StopTimeout = TimeSpan.FromSeconds(45);
            });

        // T2 POC — Saga DbContext. Registered unconditionally so its
        // migrations are always applied; whether the Saga actually receives
        // events is gated on the Workflow:UseSaga feature flag below.
        services.AddDbContext<AMR.DeliveryPlanning.Planning.Infrastructure.Data.OrchestrationDbContext>(
            opts => opts.UseNpgsql(npgsqlDataSource, ConfigureNpgsql));

        var useSaga = configuration.GetValue<bool>("Workflow:UseSaga");

        // T2 POC — schema bootstrap. Registered at the IServiceCollection
        // level (NOT inside AddMassTransit's lambda — that lambda is for
        // bus-only configuration and putting services.AddHostedService inside
        // it leaves the registration on a discarded scope, so the hosted
        // service never starts and the schema never materializes).
        if (useSaga)
        {
            services.AddHostedService<
                AMR.DeliveryPlanning.Planning.Infrastructure.Sagas.OrchestrationSchemaInitializer>();
        }

        // G2 — bus shutdown timing observer (records `bus` phase metric +
        // log line during host shutdown). Registered before AddMassTransit
        // so it's resolvable when the bus calls IBusObserver instances.
        services.AddSingleton<
            AMR.DeliveryPlanning.Api.Infrastructure.Diagnostics.BusShutdownTimingObserver>();

        services.AddMassTransit(bus =>
        {
            // G2 — wire the bus observer so PreStop/PostStop timings flow
            // into WorkflowMetrics + structured logs during shutdown.
            bus.AddBusObserver<
                AMR.DeliveryPlanning.Api.Infrastructure.Diagnostics.BusShutdownTimingObserver>();

            // Auto-scan consumers from all module Application assemblies
            bus.AddConsumers(
                typeof(DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly,
                typeof(AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly,
                typeof(DTMS.Dispatch.Application.Commands.CreateEnvelopeTrip.CreateEnvelopeTripCommand).Assembly,
                typeof(VehicleStateChangedConsumer).Assembly,
                // Transport.Amr hosts CaptureFinalSnapshotConsumer — must
                // be scanned explicitly; otherwise terminal-state events go
                // past it and the snapshot is never persisted.
                typeof(DTMS.Transport.Amr.Consumers.CaptureFinalSnapshotConsumer).Assembly
            );

            // T2 POC — opt-in Saga registration. While disabled the saga's
            // queue isn't subscribed so no events route to it; the legacy T1
            // consumer remains the sole authority. When enabled, both run
            // (dual-mode shadow phase per plan section 3.3); the cutover to
            // saga-only happens by retiring the consumer in a later commit.
            if (useSaga)
            {
                bus.AddSagaStateMachine<
                        AMR.DeliveryPlanning.Planning.Infrastructure.Sagas.DeliveryOrderSagaStateMachine,
                        AMR.DeliveryPlanning.Planning.Infrastructure.Sagas.DeliveryOrderSagaInstance>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<
                            AMR.DeliveryPlanning.Planning.Infrastructure.Data.OrchestrationDbContext>();
                        r.UsePostgres();
                    });
            }

            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbitConfig = configuration.GetSection("RabbitMq");
                cfg.Host(rabbitConfig["Host"] ?? "localhost", h =>
                {
                    h.Username(rabbitConfig["Username"] ?? "guest");
                    h.Password(rabbitConfig["Password"] ?? "guest");
                });

                // T1.1 — crash-recovery posture.
                //   UseMessageRetry      : 5 in-process retries 1s→30s for transient faults.
                //   UseDelayedRedelivery : after retries, re-queue at 1m/5m/15m/1h so the
                //                          broker (not the dying pod) holds the message.
                //   UseInMemoryOutbox    : buffer Publish/Send calls until the consumer
                //                          succeeds, so a mid-consume crash never leaves
                //                          half-published events.
                //   UseKillSwitch        : trip the receive endpoint when ≥15% of the last
                //                          10 messages fault, so a poison message can't
                //                          tar-pit the queue.
                //   PrefetchCount        : bounded so a slow consumer doesn't hoard messages
                //                          another pod could process.
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromSeconds(1),
                        maxInterval: TimeSpan.FromSeconds(30),
                        intervalDelta: TimeSpan.FromSeconds(5));
                    // OmsPermanentException = 4xx data-rejected by OMS that
                    // retry will never fix (e.g. 404 "LotNo not found").
                    // Skip the retry ladder entirely so a poison message
                    // dead-letters in ~1s instead of dragging the queue's
                    // fault rate up and tripping the Kill Switch — which
                    // would block unrelated trips waiting in the same
                    // endpoint for up to a minute per cycle.
                    r.Ignore<OmsPermanentException>();
                });
                // Trimmed the 1h bucket: transient OMS errors (5xx, network)
                // typically recover within minutes. The extra hour-long wait
                // mostly served as a soft floor for poison messages we now
                // fast-fail above, so leaving it in just delays real recovery.
                //
                // Ignore<OmsPermanentException> must be repeated on the outer
                // redelivery pipeline — MassTransit treats UseMessageRetry
                // (in-process) and UseDelayedRedelivery (re-queue) as two
                // independent filters. Without the ignore here, a 4xx
                // poison still bounces through 1m/5m/15m before DLQ.
                cfg.UseDelayedRedelivery(r =>
                {
                    r.Intervals(
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(15));
                    r.Ignore<OmsPermanentException>();
                });
                cfg.UseInMemoryOutbox(context);
                cfg.UseKillSwitch(s => s
                    .SetActivationThreshold(10)
                    .SetTripThreshold(0.15)
                    .SetRestartTimeout(TimeSpan.FromMinutes(1)));
                cfg.PrefetchCount = 16;

                cfg.ConfigureEndpoints(context);
            });
        });

        // Use OutboxEventBus for at-least-once delivery guarantee
        services.AddScoped<IEventBus, OutboxEventBus>();

        // Outbox infrastructure
        services.AddDbContext<OutboxDbContext>(o => o.UseNpgsql(npgsqlDataSource, ConfigureNpgsql));
        services
            .AddOptions<AMR.DeliveryPlanning.Api.Infrastructure.Outbox.OutboxOptions>()
            .Bind(configuration.GetSection(
                AMR.DeliveryPlanning.Api.Infrastructure.Outbox.OutboxOptions.SectionName));
        services.AddSingleton<IOutboxProcessor, OutboxProcessorService>();

        // Phase D — OutboxProcessor as IHostedService is conditional. Default
        // true so existing single-container deployments keep working. When the
        // dtms-outbox-worker container ships, the api container sets
        // Outbox:RunInThisProcess=false so the worker container is the sole
        // drainer — API serves requests, worker drains outbox. SKIP LOCKED
        // (A2 part 1) makes it safe to even run on both: each instance fetches
        // a disjoint row set per tick, no fight. The IOutboxProcessor singleton
        // registration above stays unconditional so the /admin replay endpoint
        // can still call ProcessUnpublishedEventsAsync on demand from the API.
        var runOutboxHere = configuration.GetValue<bool>("Outbox:RunInThisProcess", true);
        if (runOutboxHere)
            services.AddHostedService<OutboxProcessorService>();

        // Redis distributed cache
        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);

        // Idempotency-Key filter — singleton (stateless, depends on IDistributedCache only)
        services.AddSingleton<IdempotencyKeyFilter>();

        return services;
    }
}

// Phase 4.3 — tiny value-holders that let Application stay free of
// Microsoft.Extensions.Options + Infrastructure-specific config types.
// Registered as singletons against the IPodBucketProvider /
// IVapidPublicKeyProvider interfaces above.
internal sealed record PodBucketProvider(string PodBucket)
    : DTMS.Transport.Manual.Application.Services.IPodBucketProvider;

internal sealed record VapidPublicKeyProvider(string PublicKey)
    : DTMS.Transport.Manual.Application.Services.IVapidPublicKeyProvider;
