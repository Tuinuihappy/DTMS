using DTMS.Api.Auth;
using DTMS.Api.Infrastructure.Outbox;
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
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Infrastructure.Data;
using DTMS.Iam.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using DTMS.Fleet.Application.Consumers;
using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.Fleet.Infrastructure.Data;
using DTMS.Fleet.Infrastructure.Repositories;
using DTMS.Fleet.Infrastructure.Services;
using FleetServices = DTMS.Fleet.Infrastructure.Services;
using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Domain.Services;
using DTMS.Planning.Infrastructure.Data;
using DTMS.Planning.Infrastructure.Repositories;
using DTMS.Planning.Infrastructure.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Outbox;
using DTMS.SharedKernel.Exceptions;
using DTMS.Transport.Amr.Infrastructure;
using DTMS.Transport.Amr.Infrastructure.Extensions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;

namespace DTMS.Api.Modules;

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

        // ── Iam Module (permission system, ADR-014 Phase A + B) ───────
        // Permission storage + role→permission mapping + admin CRUD +
        // audit log. Hot-path lookup goes through PermissionRepository
        // (cached at the ClaimsTransformer layer); RoleRepository +
        // AuditLogRepository back the /api/v1/iam/* admin endpoints.
        // Phase S.2 — register the scoped context AND a singleton
        // factory side-by-side. optionsLifetime: Singleton is what
        // unlocks the dual registration: a scoped DbContext consumer
        // and the singleton PartitionMaintenanceService factory can
        // share the same DbContextOptions instance. Without that flag,
        // ServiceProvider validation fails ("cannot consume scoped
        // DbContextOptions from singleton factory").
        services.AddDbContext<IamDbContext>(
            o => o.UseNpgsql(npgsqlDataSource, ConfigureNpgsql),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<IamDbContext>(
            lifetime: ServiceLifetime.Singleton);
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<DTMS.Iam.Application.Repositories.IRoleRepository,
                           DTMS.Iam.Infrastructure.Repositories.RoleRepository>();
        services.AddScoped<DTMS.Iam.Application.Repositories.IAuditLogRepository,
                           DTMS.Iam.Infrastructure.Repositories.AuditLogRepository>();

        // Phase S.2 — federated source-system integration.
        services.AddScoped<DTMS.Iam.Application.Repositories.ISystemClientRepository,
                           DTMS.Iam.Infrastructure.Repositories.SystemClientRepository>();
        services.AddScoped<DTMS.Iam.Application.Repositories.ISystemCredentialRepository,
                           DTMS.Iam.Infrastructure.Repositories.SystemCredentialRepository>();
        // Phase S.8c — audit + backing store for admin-issued JWTs (revoke list).
        services.AddScoped<DTMS.Iam.Application.Repositories.ISystemIssuedTokenRepository,
                           DTMS.Iam.Infrastructure.Repositories.SystemIssuedTokenRepository>();
        // Phase S.8c — Redis-backed revocation list, singleton because
        // IConnectionMultiplexer is a singleton and we hold no per-request state.
        services.AddSingleton<DTMS.Iam.Application.Authorization.ISystemJwtRevocationList,
                              DTMS.Iam.Infrastructure.Authorization.RedisSystemJwtRevocationList>();
        // CachedCredentialReader takes ITieredCache (singleton) but
        // depends on the scoped ISystemCredentialRepository on cache
        // miss — register scoped so the dependency chain resolves.
        services.AddScoped<DTMS.Iam.Application.Authorization.CachedCredentialReader>();
        // SourceSystem migration P1 — mirror of CachedCredentialReader.
        // Same scoping (scoped) for the same reason: depends on the
        // scoped ISystemClientRepository on cache miss.
        services.AddScoped<DTMS.Iam.Application.Authorization.CachedSystemClientReader>();
        // Cross-module glue: DeliveryOrder handlers inject
        // IOrderOriginResolver to stamp SourceSystemKey +
        // SourceSystemDisplayName from the authenticated context. Lives
        // in the API host (not DeliveryOrder.Infrastructure) so the
        // DeliveryOrder module doesn't take a project reference on IAM.
        services.AddScoped<DTMS.DeliveryOrder.Application.Services.IOrderOriginResolver,
                           DTMS.Api.Auth.OrderOriginResolver>();
        // Sink resolved per drain-batch scope by BatchedLogDrainService;
        // sink ctor takes the scoped IamDbContext for one bulk INSERT
        // per flush.
        services.AddScoped<
            DTMS.SharedKernel.Logging.IBatchedLogSink<DTMS.Iam.Domain.Entities.SystemRequestLogEntry>,
            DTMS.Iam.Infrastructure.Logging.SystemRequestLogSink>();

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

        // ── WMS Module ────────────────────────────────────────────────
        // Snapshot cache of the external WMS (Warehouse Management System)
        // location catalogue used by Manual/Fleet transport-mode order
        // routing. Sync-to-local pattern: background poller pulls every N
        // minutes, Order Submit reads from the local table so a WMS outage
        // doesn't block user-facing writes.
        services.AddDbContext<DTMS.Wms.Infrastructure.Data.WmsDbContext>(o =>
            o.UseNpgsql(npgsqlDataSource, ConfigureNpgsql));
        services.Configure<DTMS.Wms.Infrastructure.Services.WmsOptions>(
            configuration.GetSection(DTMS.Wms.Infrastructure.Services.WmsOptions.SectionName));
        services.AddTransient<DTMS.Wms.Infrastructure.Services.WmsBearerTokenHandler>();
        services.AddScoped<DTMS.Wms.Domain.Repositories.IWmsLocationRepository,
                           DTMS.Wms.Infrastructure.Repositories.WmsLocationRepository>();
        // Sync config bridge — Application handler needs PageSize + MaxRows
        // but stays free of IOptions dependency; adapter projects WmsOptions
        // onto the small IWmsSyncConfig interface.
        services.AddScoped<DTMS.Wms.Application.Commands.SyncWmsLocations.IWmsSyncConfig,
                           DTMS.Wms.Infrastructure.Services.WmsSyncConfigAdapter>();

        var wmsBaseUrl = configuration.GetValue<string>("Wms:BaseUrl") ?? "";
        var wmsTimeoutSeconds = configuration.GetValue<int>("Wms:HttpTimeoutSeconds", 15);
        services.AddHttpClient<DTMS.Wms.Application.Services.IWmsClient,
                               DTMS.Wms.Infrastructure.Services.WmsClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(wmsBaseUrl))
                client.BaseAddress = new Uri(wmsBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(wmsTimeoutSeconds);
        })
        .AddHttpMessageHandler<DTMS.Wms.Infrastructure.Services.WmsBearerTokenHandler>()
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Sync poller — gated to the api container along with the other
        // vendor pollers so the outbox worker doesn't double-poll.
        if (runVendorPollers)
        {
            services.AddHostedService<DTMS.Wms.Infrastructure.Services.WmsLocationSyncService>();
        }

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
        services.AddSingleton<DTMS.Api.RobotPositions.IRobotPositionStore,
                              DTMS.Api.RobotPositions.InMemoryRobotPositionStore>();
        if (runVendorPollers)
        {
            services.AddHostedService<DTMS.Api.RobotPositions.Riot3PositionPollerService>();
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
        // Cross-module read port — lets Dispatch's trip-detail query resolve
        // the claiming operator's display name (Trip.ClaimedByOperatorId → name).
        services.AddScoped<DTMS.SharedKernel.Operators.IOperatorDirectory,
                           DTMS.Transport.Manual.Infrastructure.Services.OperatorDirectory>();
        services.AddScoped<DTMS.Transport.Manual.Application.Services.IOperatorSyncService,
                           DTMS.Transport.Manual.Application.Services.OperatorSyncService>();
        services.AddScoped<DTMS.Api.Auth.OperatorSyncMiddleware>();

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
        // WMS PR-3 — geofence radius for Manual drop-scan against WMS locations
        // (legacy warehouse trips continue using Warehouse.GeofenceRadiusM).
        services.Configure<DTMS.Transport.Manual.Application.Options.RecordDropGeofenceOptions>(
            configuration.GetSection(
                DTMS.Transport.Manual.Application.Options.RecordDropGeofenceOptions.SectionName));
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
                           DTMS.Api.Auth.HttpContextCurrentUserAccessor>();
        services.AddScoped<DTMS.Planning.Application.Services.ICurrentUserAccessor,
                           DTMS.Api.Auth.HttpContextCurrentUserAccessor>();
        services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
        services.AddScoped<FacilityStationLookup>();
        services.AddScoped<IStationLookup, CachedStationLookup>();
        services.AddScoped<IStationValidationService, StationValidationService>();
        // Phase 2.6 — Warehouse lookup adapter (DeliveryOrder.Application
        // contract → Facility module's read service). Not yet consumed by
        // order validation (MarkAsValidated still uses stations only);
        // Phase 4 Manual mode will inject this for operator scope checks.
        // WMS PR-1/PR-3b — location lookup used by Manual/Fleet submit path
        // to resolve PickupLocationCode → WmsLocation. Delegates to the WMS
        // module's repository so the DeliveryOrder Application layer stays
        // free of a direct WMS.Domain dependency.
        services.AddScoped<DTMS.DeliveryOrder.Application.Services.IWmsLocationLookup,
                           DTMS.DeliveryOrder.Infrastructure.Services.WmsLocationLookup>();
        // Cross-module read port — lets Dispatch's Trips list / detail resolve
        // the order requester's name (Trip.DeliveryOrderId → RequestedBy) so
        // manual / self-managed trips (no vendor vehicle, no claiming operator)
        // still show who requested the order. Mirrors IOperatorDirectory.
        services.AddScoped<DTMS.SharedKernel.Operators.IDeliveryOrderDirectory,
                           DTMS.DeliveryOrder.Infrastructure.Services.DeliveryOrderDirectory>();
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
                              DTMS.Api.Realtime.Publishers.SignalROrderRealtimePublisher>();
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
                              DTMS.Api.Realtime.Publishers.BatchedDashboardRealtimePublisher>();
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
        services.AddSingleton<DTMS.Planning.Application.Projections.IJobRealtimePublisher,
                              DTMS.Api.Realtime.Publishers.SignalRJobRealtimePublisher>();
        services.AddScoped<DTMS.Planning.Application.Projections.IJobStatusHistoryReadRepository,
                           DTMS.Planning.Infrastructure.Projections.JobStatusHistoryReadRepository>();
        services.AddScoped<DTMS.Planning.Application.Projections.IJobStatusHistoryProjectionStore,
                           DTMS.Planning.Infrastructure.Projections.JobStatusHistoryProjectionStore>();
        // Phase P5.2 — BI fact table for jobs (bi.JobFacts).
        services.AddScoped<DTMS.Planning.Application.Projections.IJobFactsReadRepository,
                           DTMS.Planning.Infrastructure.Projections.JobFactsReadRepository>();
        services.AddScoped<DTMS.Planning.Application.Projections.IJobFactsProjectionStore,
                           DTMS.Planning.Infrastructure.Projections.JobFactsProjectionStore>();
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
            services.AddScoped<IRobotOrderDispatcher, DTMS.Api.Adapters.Riot3OrderDispatcherAdapter>();
        else
            services.AddScoped<IRobotOrderDispatcher, DTMS.Api.Adapters.NoOpOrderDispatcherAdapter>();

        // Log which vendor adapters got picked at boot so it's visible without
        // having to trigger an order first. Single line at INF level.
        services.AddHostedService<DTMS.Api.Adapters.CompositionLogger>();
        services.AddScoped<DTMS.Dispatch.Application.Services.IVendorEnvelopeOperationService,
            DTMS.Api.Adapters.Riot3VendorEnvelopeOperationAdapter>();
        services.AddScoped<DTMS.Dispatch.Application.Services.IVendorRobotOperationService,
            DTMS.Api.Adapters.Riot3VendorRobotOperationAdapter>();

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
            DTMS.Api.Adapters.AmrDispatchStrategy>();
        // Phase 3c — Manual strategy stub. Returns Failure with a clear
        // "not yet implemented" reason so Manual orders end up at Failed
        // (visible in the UI) instead of Confirmed-forever. Phase 4 swaps
        // the body for the real operator-assignment flow.
        services.AddScoped<DTMS.Dispatch.Application.Services.IDispatchStrategy,
            DTMS.Api.Adapters.ManualDispatchStrategy>();
        // Self-managed dispatch (source system runs transport itself). Not an
        // IDispatchStrategy — it's mode-orthogonal, selected by the order's
        // SelfManaged flag in DeliveryOrderValidatedConsumer, not by the
        // registry. Creates trip + auto ack + pickup; no vendor/pool.
        services.AddScoped<DTMS.Dispatch.Application.Services.ISelfManagedDispatchService,
            DTMS.Api.Adapters.SelfManagedDispatchStrategy>();
        services.AddScoped<IDispatchOrderTemplateService, DispatchOrderTemplateService>();
        services.Configure<DTMS.Planning.Application.Options.DispatchOptions>(
            configuration.GetSection(DTMS.Planning.Application.Options.DispatchOptions.SectionName));
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
        services.AddHostedService<DTMS.Api.Infrastructure.FleetUtilizationSnapshotService>();

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
                              DTMS.Api.Realtime.Publishers.SignalRTripRealtimePublisher>();
        // WMS PR-4b (PR-D) — operator pool realtime broadcaster + REST list
        // handler. Broadcaster is stateless (only holds IHubContext) so
        // singleton scope is fine. The query handler needs DispatchDbContext
        // (scoped) so it stays scoped.
        services.AddSingleton<DTMS.Transport.Manual.Application.Services.IOperatorPoolBroadcaster,
                              DTMS.Api.Realtime.Publishers.SignalROperatorPoolBroadcaster>();
        // WMS PR-4b (PR-H) — pool metrics + depth polling. Meter is a
        // singleton because Meter instances are process-global by design
        // (they belong to the OTel meter provider); the sink is a thin
        // module-boundary shim so the Application handler can record
        // outcomes without referencing Api types.
        services.AddSingleton<DTMS.Api.Infrastructure.Metrics.PoolMetrics>();
        services.AddSingleton<DTMS.Transport.Manual.Application.Services.IPoolMetricsSink,
                              DTMS.Api.Infrastructure.Metrics.PoolMetricsSink>();
        services.AddHostedService<DTMS.Api.Infrastructure.Metrics.PoolDepthPollingService>();
        // Register as the MediatR-expected IRequestHandler<> shape (not
        // just IQueryHandler<>) — MediatR resolves by the concrete request-
        // handler pair, and IQueryHandler is a marker interface on top.
        // Existing handlers in *.Application assemblies get picked up by
        // MediatR's assembly scan; this handler lives in DTMS.Api (composition
        // root) so it needs an explicit registration.
        services.AddScoped<MediatR.IRequestHandler<
                              DTMS.Transport.Manual.Application.Queries.GetPoolTrips.GetPoolTripsQuery,
                              DTMS.SharedKernel.Messaging.Result<IReadOnlyList<
                                  DTMS.Transport.Manual.Application.Queries.GetPoolTrips.PoolTripDto>>>,
                           DTMS.Api.Adapters.PoolTripsQueryHandler>();
        // WMS PR-4b (PR-G) — dispatcher pool summary. Same registration
        // shape as PoolTripsQueryHandler (Api-layer handler crosses two
        // DbContexts, so MediatR needs the explicit binding).
        services.AddScoped<MediatR.IRequestHandler<
                              DTMS.Transport.Manual.Application.Queries.GetPoolSummary.GetPoolSummaryQuery,
                              DTMS.SharedKernel.Messaging.Result<
                                  DTMS.Transport.Manual.Application.Queries.GetPoolSummary.PoolSummaryDto>>,
                           DTMS.Api.Adapters.PoolSummaryQueryHandler>();
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
            DTMS.Api.Adapters.PlanningTripRetryDispatcher>();
        // Composition-root seam — lets ReissueTripCommandHandler check the
        // parent Order's status without taking a direct ref on
        // DeliveryOrder.Application. Fixes the scenario-5 bug where a
        // Cancelled-order's Trip could still be retried.
        services.AddScoped<DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader,
            DTMS.Api.Adapters.DeliveryOrderStatusReader>();
        services.AddScoped<IShelfManifestRepository, ShelfManifestRepository>();

        // ── VendorAdapter Module ──────────────────────────────────────
        services.AddVendorAdapterInfrastructure(configuration);

        // ── OmsAdapter Module — REMOVED (Phase 4) ─────────────────────
        // Outbound OMS callbacks now run through the federated pipeline
        // (subscriptions + SystemCredentials + ICallbackPayloadFormatter +
        // ISourceCallbackDispatcher). The legacy HTTP client / options /
        // target-resolver were deleted; nothing to register here anymore.

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
        services.AddDbContext<DTMS.Planning.Infrastructure.Data.OrchestrationDbContext>(
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
                DTMS.Planning.Infrastructure.Sagas.OrchestrationSchemaInitializer>();
        }

        // G2 — bus shutdown timing observer (records `bus` phase metric +
        // log line during host shutdown). Registered before AddMassTransit
        // so it's resolvable when the bus calls IBusObserver instances.
        services.AddSingleton<
            DTMS.Api.Infrastructure.Diagnostics.BusShutdownTimingObserver>();

        services.AddMassTransit(bus =>
        {
            // G2 — wire the bus observer so PreStop/PostStop timings flow
            // into WorkflowMetrics + structured logs during shutdown.
            bus.AddBusObserver<
                DTMS.Api.Infrastructure.Diagnostics.BusShutdownTimingObserver>();

            // Auto-scan consumers from all module Application assemblies
            bus.AddConsumers(
                typeof(DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder.SubmitDeliveryOrderCommand).Assembly,
                typeof(DTMS.Planning.Application.Commands.CreateJobFromOrder.CreateJobFromOrderCommand).Assembly,
                typeof(DTMS.Dispatch.Application.Commands.CreateEnvelopeTrip.CreateEnvelopeTripCommand).Assembly,
                typeof(VehicleStateChangedConsumer).Assembly,
                // Transport.Amr hosts CaptureFinalSnapshotConsumer — must
                // be scanned explicitly; otherwise terminal-state events go
                // past it and the snapshot is never persisted.
                typeof(DTMS.Transport.Amr.Consumers.CaptureFinalSnapshotConsumer).Assembly,
                // Phase S.3.1b — DTMS.Api hosts the fan-out consumer for
                // outbound callbacks (next to OutboxDbContext which it
                // writes to). Must be scanned explicitly because the
                // consumer is in the api assembly, not a module.
                typeof(DTMS.Api.Infrastructure.Callbacks.OrderDeliveredCallbackFanoutConsumer).Assembly
            );

            // T2 POC — opt-in Saga registration. While disabled the saga's
            // queue isn't subscribed so no events route to it; the legacy T1
            // consumer remains the sole authority. When enabled, both run
            // (dual-mode shadow phase per plan section 3.3); the cutover to
            // saga-only happens by retiring the consumer in a later commit.
            if (useSaga)
            {
                bus.AddSagaStateMachine<
                        DTMS.Planning.Infrastructure.Sagas.DeliveryOrderSagaStateMachine,
                        DTMS.Planning.Infrastructure.Sagas.DeliveryOrderSagaInstance>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<
                            DTMS.Planning.Infrastructure.Data.OrchestrationDbContext>();
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
                });
                // Trimmed the 1h bucket: transient errors (5xx, network)
                // typically recover within minutes.
                cfg.UseDelayedRedelivery(r =>
                {
                    r.Intervals(
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(15));
                    // VendorVehicleUnavailableException = shipment.started fan-out
                    // with no robot name yet. The in-process UseMessageRetry
                    // above (~65s) covers the real sub-second save race; if the
                    // name still isn't there we skip the minutes-scale ladder so
                    // it dead-letters fast instead of faulting ~21m after the
                    // trip already completed (audit time-reversal). Ignore must
                    // repeat here — MassTransit treats in-process retry and
                    // delayed redelivery as two independent filters.
                    r.Ignore<VendorVehicleUnavailableException>();
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
            .AddOptions<DTMS.Api.Infrastructure.Outbox.OutboxOptions>()
            .Bind(configuration.GetSection(
                DTMS.Api.Infrastructure.Outbox.OutboxOptions.SectionName));
        services.AddSingleton<IOutboxProcessor, OutboxProcessorService>();

        // Phase O2 — wake signal is a singleton bounded channel shared
        // between OutboxListenerService (writer, on Postgres NOTIFY) and
        // OutboxProcessorService (reader, in the outer loop). Registered
        // unconditionally so the processor can inject it whether or not
        // the listener is enabled.
        services.AddSingleton<DTMS.SharedKernel.Outbox.IOutboxWakeSignal,
                              DTMS.SharedKernel.Outbox.OutboxWakeSignal>();

        // Phase O3 — DLQ store + router. Both scoped so they share the
        // per-tick OutboxDbContext with the processor and can look up
        // module DbContexts for replay routing.
        services.AddScoped<DTMS.SharedKernel.Outbox.IDeadLetterStore,
                           DTMS.Api.Infrastructure.Outbox.DeadLetterStore>();
        services.AddScoped<DTMS.Api.Infrastructure.Outbox.IDeadLetterReplayRouter,
                           DTMS.Api.Infrastructure.Outbox.DeadLetterReplayRouter>();

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
        {
            services.AddHostedService<OutboxProcessorService>();

            // Phase O2 — Postgres LISTEN/NOTIFY driver. Only run alongside
            // the processor (they share the wake signal channel); flip off
            // via Outbox:UseListenNotify=false for a poll-only posture
            // during migration or debugging.
            if (configuration.GetValue<bool>("Outbox:UseListenNotify", true))
                services.AddHostedService<OutboxListenerService>();

            // Phase O3 — DLQ size reporter. 30s tick updates the
            // outbox_dlq_size gauge on WorkflowMetrics. Gated behind
            // RunInThisProcess so only the drain container runs it —
            // otherwise it would double-report across api + worker.
            services.AddHostedService<DlqSizeReporterService>();
        }

        // Phase S.3 — partitioned outbox drain for federated source
        // callbacks. Same RunInThisProcess gate as the legacy processor
        // above so the dedicated outbox-worker container drains both
        // queues without the api container double-processing.
        //
        // Phase S.3.1b swaps in the real HTTP dispatcher (replaces the
        // S.3 LoggingSourceCallbackDispatcher dev stub). Per-system
        // HttpClient with sane defaults — the per-credential timeout
        // overrides on the request itself.
        services.AddHttpClient<DTMS.Iam.Application.Callbacks.ISourceCallbackDispatcher,
                               DTMS.Iam.Infrastructure.Callbacks.HttpSourceCallbackDispatcher>(c =>
            {
                // Hard ceiling — actual per-call timeout comes from
                // SystemCredential.CallbackTimeoutMs. This protects us
                // from a misconfigured credential or a connection that
                // hangs before TLS completes.
                c.Timeout = TimeSpan.FromSeconds(30);
            });

        // Phase S.3.1b — subscription registry: repo + cached lookup +
        // OMS payload formatter (keyed by PayloadFormatKey on the
        // subscription row). Add a new system later = add a new
        // AddKeyedScoped here with its key + impl, plus a DB row.
        services.AddScoped<DTMS.Iam.Application.Repositories.ISystemEventSubscriptionRepository,
                           DTMS.Iam.Infrastructure.Repositories.SystemEventSubscriptionRepository>();
        services.AddScoped<DTMS.Iam.Application.Callbacks.ISubscriptionLookup,
                           DTMS.Iam.Infrastructure.Callbacks.CachedSubscriptionLookup>();
        services.AddKeyedScoped<DTMS.Iam.Application.Callbacks.ICallbackPayloadFormatter,
                                DTMS.Iam.Infrastructure.Callbacks.OmsShipmentCancelledFormatter>(
            DTMS.Iam.Infrastructure.Callbacks.OmsShipmentCancelledFormatter.FormatKey);
        // Phase S.5 (B2) — OMS shipment started/arrived formatters (keep the
        // legacy /api/shipments + /{id}/arrived contract via RelativePath).
        services.AddKeyedScoped<DTMS.Iam.Application.Callbacks.ICallbackPayloadFormatter,
                                DTMS.Iam.Infrastructure.Callbacks.OmsShipmentStartedFormatter>(
            DTMS.Iam.Infrastructure.Callbacks.OmsShipmentStartedFormatter.FormatKey);
        services.AddKeyedScoped<DTMS.Iam.Application.Callbacks.ICallbackPayloadFormatter,
                                DTMS.Iam.Infrastructure.Callbacks.OmsShipmentArrivedFormatter>(
            DTMS.Iam.Infrastructure.Callbacks.OmsShipmentArrivedFormatter.FormatKey);
        // Phase C — runtime-key formatter resolution for Application-layer
        // resend handlers (they read the key off the subscription row, so
        // [FromKeyedServices] can't apply).
        services.AddScoped<DTMS.Iam.Application.Callbacks.ICallbackFormatterResolver,
                           DTMS.Api.Infrastructure.Callbacks.KeyedCallbackFormatterResolver>();

        if (runOutboxHere)
            services.AddHostedService<DTMS.Api.Infrastructure.Outbox.MultiPartitionOutboxProcessor>();

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
