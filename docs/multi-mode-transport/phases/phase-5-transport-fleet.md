# Phase 5 — Implement Transport.Fleet

- **Sprint**: 9-10
- **Risk**: Medium-High (depends on 3PL provider quality)
- **Schema change**: Yes (additive — new tables)
- **Frontend impact**: Medium (waybill tracker + provider status)
- **Depends on**: [Phase 1](phase-1-foundation.md), [Phase 2](phase-2-facility-vehicle-split.md), [Phase 3](phase-3-dispatch-abstraction.md)

## Goal

Implement Fleet transport mode (3PL outsourcing) end-to-end:
1. FleetProvider + FleetContract + Waybill domain
2. Provider abstraction (Kerry Express as first impl)
3. FleetDispatchStrategy: route by service area + create waybill
4. Provider webhook + reconciliation
5. Frontend waybill tracking

## Task Checklist

### Step 1: Create FleetProvider Domain

**`src/Modules/Transport.Fleet/.../Domain/Entities/`** (NEW):

**`FleetProvider.cs`**:
```csharp
public class FleetProvider
{
    public Guid Id { get; }
    public string Code { get; }                    // "kerry", "flash", "jt-express"
    public string Name { get; }
    public string ApiBaseUrl { get; }
    public string ContactPhone { get; }
    public string ContactEmail { get; }
    public IReadOnlyList<ServiceArea> ServiceAreas { get; }   // postal code ranges, lat/lng polygons
    public bool IsActive { get; }
    public DateTime ActivatedAt { get; }
}

public sealed record ServiceArea(string Name, Polygon Coverage);
```

**`FleetContract.cs`**:
```csharp
public class FleetContract
{
    public Guid Id { get; }
    public Guid ProviderId { get; }
    public string ContractNumber { get; }
    public DateTime StartDate { get; }
    public DateTime? EndDate { get; }
    public IReadOnlyList<RateCard> Rates { get; }
    public SlaCommitment Sla { get; }
    public bool IsActive { get; }
}

public sealed record RateCard(string ZoneName, decimal BasePrice, decimal PerKgPrice);
public sealed record SlaCommitment(TimeSpan PickupSla, TimeSpan DeliverySla);
```

**`Waybill.cs`**:
```csharp
public class Waybill
{
    public Guid Id { get; }
    public Guid TripId { get; }
    public Guid ProviderId { get; }
    public string WaybillNumber { get; }              // provider-issued
    public string ProviderRef { get; }                 // provider's internal ID
    public WaybillStatus Status { get; private set; }
    public string? TrackingUrl { get; }
    public DateTime? EstimatedPickupAt { get; }
    public DateTime? EstimatedDeliveryAt { get; }
    public DateTime CreatedAt { get; }
    public DateTime? LastSyncedAt { get; }
    public string? LastSnapshot { get; private set; }   // last provider status payload

    public void UpdateStatus(WaybillStatus status, string snapshot) { ... }
}

public enum WaybillStatus
{
    Created,
    AcceptedByProvider,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
    Cancelled
}
```

### Step 2: Create FleetTripExtension (per ADR-003)

**`src/Modules/Transport.Fleet/.../Domain/Entities/FleetTripExtension.cs`**:

```csharp
public class FleetTripExtension
{
    public Guid TripId { get; }
    public Guid WaybillId { get; }                   // FK to Waybill
    public Guid ProviderId { get; }
    public string WaybillNumber { get; }
    public string? TrackingUrl { get; }
    public DateTime? EstimatedArrivalAt { get; }
    public DateTime DispatchedAt { get; }
}
```

### Step 3: Provider Abstraction

**`src/Modules/Transport.Fleet/.../Application/Services/IFleetProviderClient.cs`**:

```csharp
public interface IFleetProviderClient
{
    string ProviderCode { get; }
    Task<CreateShipmentResult> CreateShipmentAsync(FleetDispatchPlan plan, CancellationToken ct);
    Task<CancelShipmentResult> CancelShipmentAsync(string providerRef, CancellationToken ct);
    Task<WaybillStatus> GetShipmentStatusAsync(string providerRef, CancellationToken ct);
}

public sealed record CreateShipmentResult(
    bool Success,
    string? WaybillNumber,
    string? ProviderRef,
    string? TrackingUrl,
    DateTime? EstimatedPickupAt,
    DateTime? EstimatedDeliveryAt,
    string? ErrorMessage);
```

**`IFleetProviderFactory.cs`**:
```csharp
public interface IFleetProviderFactory
{
    IFleetProviderClient Get(string providerCode);
    IFleetProviderClient Get(Guid providerId);
    IReadOnlyList<IFleetProviderClient> GetAll();
}
```

### Step 4: Kerry Implementation (Reference)

**`src/Modules/Transport.Fleet/.../Infrastructure/Providers/KerryFleetProviderClient.cs`**:

```csharp
public sealed class KerryFleetProviderClient : IFleetProviderClient
{
    private readonly HttpClient _http;
    private readonly IOptions<KerryProviderOptions> _options;
    private readonly ILogger<KerryFleetProviderClient> _log;

    public string ProviderCode => "kerry";

    public async Task<CreateShipmentResult> CreateShipmentAsync(FleetDispatchPlan plan, CancellationToken ct)
    {
        var request = new KerryCreateShipmentRequest {
            SenderName = plan.PickupContact.Name,
            SenderAddress = plan.PickupAddress.ToKerryFormat(),
            ReceiverName = plan.DropContact.Name,
            ReceiverAddress = plan.DropAddress.ToKerryFormat(),
            ItemDetails = plan.Items.Select(i => new {
                Weight = i.WeightKg,
                Dimensions = i.Dimensions,
                Description = i.Description
            }).ToList(),
            ServiceType = "STANDARD"
        };

        var response = await _http.PostAsJsonAsync("/api/v1/shipments", request, ct);
        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(ct);
            return new CreateShipmentResult(false, null, null, null, null, null, error);
        }

        var body = await response.Content.ReadFromJsonAsync<KerryCreateShipmentResponse>(ct);
        return new CreateShipmentResult(
            Success: true,
            WaybillNumber: body!.WaybillNumber,
            ProviderRef: body.ShipmentId,
            TrackingUrl: $"https://track.kerry.com/{body.WaybillNumber}",
            EstimatedPickupAt: body.EstimatedPickup,
            EstimatedDeliveryAt: body.EstimatedDelivery,
            ErrorMessage: null);
    }

    public async Task<CancelShipmentResult> CancelShipmentAsync(string providerRef, CancellationToken ct) { ... }
    public async Task<WaybillStatus> GetShipmentStatusAsync(string providerRef, CancellationToken ct) { ... }
}
```

### Step 5: FleetDispatchStrategy

**`src/Modules/Transport.Fleet/.../Application/Services/FleetDispatchStrategy.cs`**:

```csharp
public sealed class FleetDispatchStrategy : IDispatchStrategy
{
    private readonly IFleetProviderFactory _providerFactory;
    private readonly IFleetProviderSelector _selector;
    private readonly IWaybillRepository _waybills;
    private readonly IFleetTripExtensionRepository _ext;
    private readonly IWarehouseLookup _warehouses;
    private readonly IDeliveryOrderReader _orders;

    public TransportMode Mode => TransportMode.Fleet;

    public async Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct)
    {
        // 1. Load order details + destination warehouse for provider selection
        var order = await _orders.GetAsync(trip.OrderId, ct);
        var dropWarehouse = await _warehouses.GetAsync(trip.DropWarehouseId, ct);

        // 2. Select provider based on service area + contract availability
        var provider = await _selector.SelectAsync(dropWarehouse.Location, order, ct);
        if (provider is null)
            return new DispatchResult(false, null, "No provider covers destination");

        // 3. Build dispatch plan
        var plan = FleetDispatchPlan.Build(trip, order, dropWarehouse);

        // 4. Call provider API
        var client = _providerFactory.Get(provider.Id);
        var result = await client.CreateShipmentAsync(plan, ct);
        if (!result.Success)
            return new DispatchResult(false, null, result.ErrorMessage);

        // 5. Persist waybill + extension
        var waybill = Waybill.Create(
            tripId: trip.Id,
            providerId: provider.Id,
            waybillNumber: result.WaybillNumber!,
            providerRef: result.ProviderRef!,
            trackingUrl: result.TrackingUrl,
            estimatedPickupAt: result.EstimatedPickupAt,
            estimatedDeliveryAt: result.EstimatedDeliveryAt);

        await _waybills.AddAsync(waybill, ct);

        var ext = new FleetTripExtension(
            trip.Id, waybill.Id, provider.Id, waybill.WaybillNumber, waybill.TrackingUrl, waybill.EstimatedDeliveryAt);
        await _ext.AddAsync(ext, ct);

        return new DispatchResult(true, vendorOrderKey: waybill.ProviderRef, reason: null);
    }
}
```

**`IFleetProviderSelector`**:
```csharp
public sealed class ServiceAreaProviderSelector : IFleetProviderSelector
{
    public async Task<FleetProvider?> SelectAsync(LatLng destination, DeliveryOrder order, CancellationToken ct)
    {
        // 1. Find providers whose ServiceAreas contain destination
        // 2. Filter to active contracts
        // 3. Score by: rate (cheapest), SLA fit, reliability score
        // 4. Return best match (or null if none)
    }
}
```

### Step 6: FleetVendorOperationAdapter

```csharp
public sealed class FleetVendorOperationAdapter : IVendorEnvelopeOperationService
{
    private readonly IWaybillRepository _waybills;
    private readonly IFleetProviderFactory _providerFactory;

    public async Task<VendorOperationOutcome> CancelAsync(Guid tripId, CancellationToken ct)
    {
        var waybill = await _waybills.GetByTripIdAsync(tripId, ct);
        if (waybill is null) return VendorOperationOutcome.NoVendorRecord;

        var client = _providerFactory.Get(waybill.ProviderId);
        var result = await client.CancelShipmentAsync(waybill.ProviderRef, ct);

        return result.Success ? VendorOperationOutcome.Accepted : VendorOperationOutcome.Rejected;
    }

    public Task<VendorOperationOutcome> PauseAsync(Guid tripId, CancellationToken ct) =>
        Task.FromResult(VendorOperationOutcome.Rejected);  // most 3PL don't support pause

    public Task<VendorOperationOutcome> ResumeAsync(Guid tripId, CancellationToken ct) =>
        Task.FromResult(VendorOperationOutcome.Rejected);
}
```

### Step 7: Provider Webhook Handler

**`src/Modules/Transport.Fleet/.../Presentation/FleetWebhookEndpoints.cs`**:

```csharp
public static class FleetWebhookEndpoints
{
    public static void MapFleetWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/fleet/{providerCode}/notify", async (
            string providerCode,
            HttpRequest request,
            IFleetWebhookDispatcher dispatcher,
            CancellationToken ct) =>
        {
            // 1. Verify signature (per-provider HMAC)
            if (!await dispatcher.VerifySignatureAsync(providerCode, request, ct))
                return Results.Unauthorized();

            // 2. Parse + dispatch event
            var result = await dispatcher.HandleAsync(providerCode, request.Body, ct);
            return result.Success ? Results.Ok() : Results.BadRequest(result.Error);
        });
    }
}
```

**`KerryWebhookHandler`** (per-provider):
```csharp
public sealed class KerryWebhookHandler : IFleetWebhookHandler
{
    public string ProviderCode => "kerry";

    public async Task<WebhookResult> HandleAsync(Stream body, CancellationToken ct)
    {
        var payload = await JsonSerializer.DeserializeAsync<KerryWebhookPayload>(body, options: default, ct);

        // Map Kerry status → DTMS WaybillStatus
        var newStatus = MapStatus(payload!.Status);   // e.g. "PICKED_UP" → WaybillStatus.PickedUp

        var waybill = await _waybills.GetByProviderRefAsync(payload.ShipmentId, ct);
        if (waybill is null) return WebhookResult.NotFound;

        waybill.UpdateStatus(newStatus, JsonSerializer.Serialize(payload));
        await _waybills.UpdateAsync(waybill, ct);

        // Trigger Trip state transition if terminal
        if (newStatus is WaybillStatus.Delivered or WaybillStatus.Failed) {
            var trip = await _trips.GetByIdAsync(waybill.TripId, ct);
            if (newStatus == WaybillStatus.Delivered) trip.MarkCompleted(DateTime.UtcNow);
            else trip.MarkFailed("Provider reported failure");
            await _trips.UpdateAsync(trip, ct);
        }

        return WebhookResult.Ok;
    }
}
```

### Step 8: Reconciliation Background Service

**`FleetWaybillReconciliationService`** (fallback if webhook unreliable):

```csharp
public sealed class FleetWaybillReconciliationService : BackgroundService
{
    private readonly IWaybillRepository _waybills;
    private readonly IFleetProviderFactory _providers;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            var inFlight = await _waybills.GetInFlightAsync(ct);
            foreach (var waybill in inFlight) {
                try {
                    var client = _providers.Get(waybill.ProviderId);
                    var status = await client.GetShipmentStatusAsync(waybill.ProviderRef, ct);
                    if (status != waybill.Status) {
                        waybill.UpdateStatus(status, "polled");
                        await _waybills.UpdateAsync(waybill, ct);
                    }
                } catch (Exception ex) {
                    _log.LogWarning(ex, "Reconciliation failed for waybill {WaybillId}", waybill.Id);
                }
            }
            await Task.Delay(_interval, ct);
        }
    }
}
```

### Step 9: DI Registration

**`TransportFleetServiceCollectionExtensions.cs`**:

```csharp
public static class TransportFleetServiceCollectionExtensions
{
    public static IServiceCollection AddTransportFleet(this IServiceCollection services, IConfiguration config)
    {
        var fleetConfig = config.GetSection("TransportModes:Fleet");
        if (!fleetConfig.GetValue<bool>("Enabled")) return services;

        services.AddDbContext<FleetDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        // Domain
        services.AddScoped<IFleetProviderRepository, FleetProviderRepository>();
        services.AddScoped<IFleetContractRepository, FleetContractRepository>();
        services.AddScoped<IWaybillRepository, WaybillRepository>();
        services.AddScoped<IFleetTripExtensionRepository, FleetTripExtensionRepository>();

        // Strategies + adapters
        services.AddScoped<IDispatchStrategy, FleetDispatchStrategy>();
        services.AddScoped<FleetVendorOperationAdapter>();
        services.AddScoped<IFleetProviderSelector, ServiceAreaProviderSelector>();

        // Provider clients
        services.AddHttpClient<KerryFleetProviderClient>(c => {
            c.BaseAddress = new Uri(fleetConfig.GetSection("Providers:Kerry:BaseUrl").Value!);
            c.DefaultRequestHeaders.Add("Authorization", $"Bearer {fleetConfig["Providers:Kerry:ApiKey"]}");
        });
        services.AddScoped<IFleetProviderClient, KerryFleetProviderClient>();

        // Webhook handlers
        services.AddScoped<IFleetWebhookHandler, KerryWebhookHandler>();
        services.AddScoped<IFleetWebhookDispatcher, FleetWebhookDispatcher>();

        // Factory
        services.AddScoped<IFleetProviderFactory, FleetProviderFactory>();

        // Background services
        services.AddHostedService<FleetWaybillReconciliationService>();

        return services;
    }
}
```

### Step 10: Migrations (Additive)

```sql
-- 20260809000000_CreateTransportFleetSchema.cs
CREATE SCHEMA transport_fleet;

CREATE TABLE transport_fleet.providers (
    id UUID PRIMARY KEY,
    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    api_base_url VARCHAR(500) NOT NULL,
    contact_phone VARCHAR(50),
    contact_email VARCHAR(200),
    service_areas JSONB NOT NULL DEFAULT '[]',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    activated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE transport_fleet.contracts (
    id UUID PRIMARY KEY,
    provider_id UUID NOT NULL REFERENCES transport_fleet.providers(id),
    contract_number VARCHAR(100) NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE,
    rate_cards JSONB NOT NULL DEFAULT '[]',
    sla_pickup_seconds INTEGER NOT NULL,
    sla_delivery_seconds INTEGER NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (provider_id, contract_number)
);

CREATE TABLE transport_fleet.waybills (
    id UUID PRIMARY KEY,
    trip_id UUID NOT NULL UNIQUE,
    provider_id UUID NOT NULL REFERENCES transport_fleet.providers(id),
    waybill_number VARCHAR(100) NOT NULL UNIQUE,
    provider_ref VARCHAR(200) NOT NULL,
    status VARCHAR(50) NOT NULL,
    tracking_url VARCHAR(1000),
    estimated_pickup_at TIMESTAMPTZ,
    estimated_delivery_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL,
    last_synced_at TIMESTAMPTZ,
    last_snapshot JSONB
);

CREATE INDEX ix_waybills_provider_ref ON transport_fleet.waybills(provider_id, provider_ref);
CREATE INDEX ix_waybills_in_flight ON transport_fleet.waybills(status, last_synced_at)
    WHERE status NOT IN ('Delivered', 'Failed', 'Cancelled');

CREATE TABLE transport_fleet.fleet_trip_extensions (
    trip_id UUID PRIMARY KEY REFERENCES dispatch.trips(id) ON DELETE CASCADE,
    waybill_id UUID NOT NULL REFERENCES transport_fleet.waybills(id),
    provider_id UUID NOT NULL REFERENCES transport_fleet.providers(id),
    waybill_number VARCHAR(100) NOT NULL,
    tracking_url VARCHAR(1000),
    estimated_arrival_at TIMESTAMPTZ,
    dispatched_at TIMESTAMPTZ NOT NULL
);
```

### Step 11: Frontend — Waybill Tracker

**`frontend/components/transport/fleet/waybill-tracker.tsx`** (NEW):

```tsx
export function WaybillTracker({ tripId }: { tripId: string }) {
  const { data: waybill } = useSWR(`/api/transport-fleet/waybills?tripId=${tripId}`, fetcher);

  if (!waybill) return null;

  return (
    <Card>
      <CardHeader>
        <div>Waybill #{waybill.number}</div>
        <Badge variant={badgeVariant(waybill.status)}>{waybill.status}</Badge>
      </CardHeader>
      <CardBody>
        <Timeline events={waybill.statusHistory} />
        {waybill.trackingUrl && (
          <Link href={waybill.trackingUrl} target="_blank">View on {waybill.providerName} →</Link>
        )}
        <div>ETA: <DateTime value={waybill.estimatedDeliveryAt} /></div>
      </CardBody>
    </Card>
  );
}
```

**Update trip-action-bar.tsx** — Fleet-specific:
```tsx
{trip.mode === 'Fleet' && (
  <>
    <Button onClick={() => window.open(trip.trackingUrl, '_blank')}>View Tracking</Button>
    <Button onClick={onContactProvider}>Contact Provider</Button>
  </>
)}
```

### Step 12: Configuration

`appsettings.json`:
```json
{
  "TransportModes": {
    "Fleet": {
      "Enabled": true,
      "Providers": {
        "Kerry": {
          "BaseUrl": "https://api.kerryexpress.com",
          "ApiKey": "${KERRY_API_KEY}",
          "WebhookSecret": "${KERRY_WEBHOOK_SECRET}"
        }
      }
    }
  }
}
```

## Verification

### Test Gates

```bash
dotnet build --configuration Release
dotnet test --no-build --logger "console;verbosity=minimal"
cd frontend && npm run typecheck && npm run lint && npm run build
```

### NEW Tests

```csharp
// Unit tests
[Fact]
public async Task FleetDispatchStrategy_ReturnsFail_WhenNoProvider() { ... }

[Fact]
public async Task ServiceAreaProviderSelector_PicksByCoverage() { ... }

[Fact]
public async Task KerryFleetProviderClient_BuildsRequest_PerKerrySpec() { ... }

[Fact]
public async Task KerryWebhookHandler_Maps_PICKED_UP_To_PickedUp() { ... }

[Fact]
public async Task WaybillReconciliation_UpdatesStatus_FromPoll() { ... }

// Integration tests with mock Kerry server
[Fact]
public async Task FullDispatch_Kerry_Creates_Waybill_Receives_Webhook_Completes() {
    // Setup: WireMock for Kerry API
    // Action: Dispatch order → Kerry mock creates shipment → simulate webhook → trip completes
}

[Fact]
public async Task WebhookSignatureVerification_RejectsTamperedPayload() { ... }
```

### Regression Tests

```bash
# All previous modes still work
dotnet test --filter "FullyQualifiedName~Transport.Amr"
dotnet test --filter "FullyQualifiedName~Transport.Manual"
dotnet test --filter "FullyQualifiedName~Trip"
```

### Manual Smoke Test

```
Setup:
- POST /api/transport-fleet/providers (Kerry, ServiceAreas=[Bangkok, ChiangMai])
- POST /api/transport-fleet/contracts (Kerry, ContractNumber=K001, valid 2026)
- Configure Kerry mock server (or sandbox API)

Fleet dispatch flow:
1. POST /api/delivery-orders (TransportMode=Fleet, PickupWarehouseId=A, DropAddress=Customer Korat)
2. Wait for dispatch:
   - Verify FleetDispatchStrategy invoked
   - Verify ServiceAreaProviderSelector picked Kerry
   - Verify Kerry mock received CreateShipment with correct fields
   - Verify Waybill row + FleetTripExtension created
3. Verify Trip.Status = InProgress (or whatever fleet's "dispatched but not picked up" status is)

Webhook simulation:
4. POST /api/webhooks/fleet/kerry/notify
   { event: "PICKED_UP", shipmentId: "...", waybillNumber: "..." }
   → verify HMAC signature check
   → verify Waybill.Status = PickedUp
5. POST again with status="DELIVERED"
   → verify Trip.Status = Completed

Frontend:
6. Trip detail page shows waybill panel with tracking link
7. Status timeline reflects waybill events
```

## Before vs After

### Before
- Fleet mode = enum value only
- No 3PL integration
- No waybill tracking

### After
- FleetProvider + FleetContract + Waybill domain
- IFleetProviderClient abstraction (provider-agnostic)
- Kerry implementation as reference (extensible to Flash, J&T, etc.)
- Webhook + reconciliation safety net
- Frontend waybill tracker
- 3 transport modes operating in parallel on same DTMS deployment

## Outcome

- ✓ Fleet mode fully working end-to-end with Kerry
- ✓ Adding new provider = implement IFleetProviderClient + IFleetWebhookHandler (no Dispatch change)
- ✓ Multi-provider failover possible (FleetProviderSelector logic)
- ✓ Trip FSM reused across all 3 modes
- ✓ Dispatcher console + BI reporting cover all 3 modes
- ✓ Refactor complete — adding mode 4 (e.g., Drone) follows same pattern

## Final Acceptance Criteria

- [ ] All 3 transport modes (Amr, Manual, Fleet) ทำงานพร้อมกันใน DB เดียวกัน
- [ ] Trip FSM (Created → InProgress → Paused → Completed/Failed/Cancelled) ใช้ร่วมกัน
- [ ] BI report groupBy `TransportMode` ทำได้
- [ ] Dispatcher console operate ได้ทุก mode (mode-aware action bar)
- [ ] Test coverage ≥ 80% unit + integration happy path + failure path each mode
- [ ] ArchitectureTests enforce module boundaries
- [ ] All 5 phase gates green
- [ ] Documentation complete (this folder)
- [ ] No regression on existing AMR flows

## Risks & Mitigation

| Risk | Mitigation |
|---|---|
| Kerry API changes / outage | Retry with exponential backoff; circuit breaker; fallback provider |
| Webhook delivery unreliable | Reconciliation poller as safety net (every 10 min) |
| Provider sandbox unavailable | WireMock integration tests for CI |
| Multi-provider routing complexity | Start with single provider (Kerry); add second in follow-up sprint |
| Rate card / pricing changes | Versioned RateCard; effective date; audit log |

## Next Steps (Post-Phase 5)

- Onboard additional providers (Flash, J&T, etc.) — pattern established
- Cost optimization: provider selection by rate
- Real-time map view: GPS from all 3 modes overlaid
- Customer-facing tracking page
- BI dashboard cross-mode comparison
