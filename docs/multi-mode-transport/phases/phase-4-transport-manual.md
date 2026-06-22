# Phase 4 — Implement Transport.Manual

- **Sprint**: 7-8
- **Risk**: High (new domain + mobile UX critical)
- **Schema change**: Yes (additive — new tables only)
- **Frontend impact**: High (operator board + mobile app contract)
- **Depends on**: [Phase 1](phase-1-foundation.md), [Phase 2](phase-2-facility-vehicle-split.md), [Phase 3](phase-3-dispatch-abstraction.md)

## Goal

Implement Manual transport mode end-to-end:
1. Operator aggregate (shift, certification, device)
2. Manual dispatch strategy (assign operator + push notification)
3. Mobile-facing REST API (`/api/operator/*`)
4. POD capture flow (geofence-verified)
5. SLA watchdog + presence monitoring
6. Dispatcher console UI for Manual trips

> **Out of scope**: Mobile app implementation (React Native/Flutter — separate project)

## Task Checklist

### Step 1: Create Operator Domain

**`src/Modules/Transport.Manual/.../Domain/Entities/`** (NEW files):

**`Operator.cs`**:
```csharp
public class Operator
{
    public Guid Id { get; }
    public string EmployeeCode { get; }
    public string Name { get; }
    public string Phone { get; }
    public string Email { get; }
    public OperatorStatus Status { get; private set; }    // Active/Suspended/OffDuty
    public IReadOnlyList<Guid> CertificationIds { get; }
    public DateTime HiredAt { get; }
    public DateTime? TerminatedAt { get; }

    public void Suspend(string reason) { ... }
    public void Activate() { ... }
}

public enum OperatorStatus { Active, Suspended, OffDuty, Terminated }
```

**`OperatorShift.cs`**:
```csharp
public class OperatorShift
{
    public Guid Id { get; }
    public Guid OperatorId { get; }
    public DateTime ShiftStart { get; }
    public DateTime? ShiftEnd { get; private set; }
    public IReadOnlyList<Guid> WarehouseScope { get; }    // facilities operator can serve
    public Guid? CurrentTripId { get; private set; }

    public void ClockOut(DateTime at) { ... }
    public void AssignTrip(Guid tripId) {
        if (CurrentTripId is not null)
            throw new InvalidOperationException("Operator already has active trip");
        CurrentTripId = tripId;
    }
    public void ReleaseTrip() { CurrentTripId = null; }
}
```

**`OperatorCertification.cs`**:
```csharp
public class OperatorCertification
{
    public Guid Id { get; }
    public string Name { get; }       // "HAZMAT", "Cold-chain", "Heavy-load"
    public string Code { get; }
    public DateTime? ValidUntil { get; }
}
```

**`OperatorDevice.cs`**:
```csharp
public class OperatorDevice
{
    public Guid Id { get; }
    public Guid OperatorId { get; }
    public string DeviceFingerprint { get; }
    public string PushToken { get; private set; }
    public string AppVersion { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime RegisteredAt { get; }

    public void Heartbeat(DateTime at) { LastSeenAt = at; }
    public void UpdateToken(string token) { PushToken = token; }
}
```

### Step 2: Create ManualTripExtension (per ADR-003)

**`src/Modules/Transport.Manual/.../Domain/Entities/ManualTripExtension.cs`** (NEW):

```csharp
public class ManualTripExtension
{
    public Guid TripId { get; }                            // PK + FK
    public Guid AssignedOperatorId { get; private set; }
    public Guid OperatorShiftId { get; private set; }
    public DateTime AssignedAt { get; }
    public DateTime? AcknowledgedAt { get; private set; }
    public DateTime? PickupVerifiedAt { get; private set; }
    public DateTime? DropVerifiedAt { get; private set; }
    public LatLng? PickupGpsCoord { get; private set; }   // for audit
    public LatLng? DropGpsCoord { get; private set; }
    public string? PodPhotoUrl { get; private set; }
    public string? PodSignatureUrl { get; private set; }
    public string? PodNotes { get; private set; }

    // SLA deadlines
    public DateTime ExpectedAckBy { get; }
    public DateTime ExpectedPickupBy { get; }
    public DateTime ExpectedDropBy { get; }

    public void RecordAcknowledged(DateTime at) { ... }
    public void RecordPickup(DateTime at, LatLng gps, string? photoUrl) { ... }
    public void RecordDrop(DateTime at, LatLng gps, string? photoUrl, string? signatureUrl) { ... }
}
```

### Step 3: Implement ManualDispatchStrategy

**`src/Modules/Transport.Manual/.../Application/Services/ManualDispatchStrategy.cs`** (NEW):

```csharp
public sealed class ManualDispatchStrategy : IDispatchStrategy
{
    private readonly IOperatorAssignmentPolicy _policy;
    private readonly IManualTripExtensionRepository _ext;
    private readonly IPushNotificationGateway _push;
    private readonly IOperatorShiftRepository _shifts;
    private readonly IOptions<ManualModeOptions> _options;

    public TransportMode Mode => TransportMode.Manual;

    public async Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct)
    {
        // 1. Find eligible operator
        var operatorId = await _policy.SelectAsync(trip, ct);
        if (operatorId is null)
            return new DispatchResult(false, null, "No eligible operator available");

        // 2. Assign to operator's shift (enforces 1-active-trip invariant)
        var shift = await _shifts.GetActiveByOperatorIdAsync(operatorId.Value, ct);
        shift.AssignTrip(trip.Id);
        await _shifts.UpdateAsync(shift, ct);

        // 3. Create extension with SLA deadlines
        var now = DateTime.UtcNow;
        var ext = new ManualTripExtension(
            trip.Id, operatorId.Value, shift.Id, now,
            expectedAckBy: now + _options.Value.AckTimeout,
            expectedPickupBy: now + _options.Value.PickupTimeout,
            expectedDropBy: now + _options.Value.DropTimeout);
        await _ext.AddAsync(ext, ct);

        // 4. Push notification
        await _push.SendAsync(operatorId.Value, new TripAssignedNotification(trip.Id), ct);

        return new DispatchResult(true, vendorOrderKey: null, reason: null);
    }
}
```

**`IOperatorAssignmentPolicy`** — auto-assign logic:
```csharp
public interface IOperatorAssignmentPolicy
{
    Task<Guid?> SelectAsync(Trip trip, CancellationToken ct);
}

public sealed class WarehouseScopeAssignmentPolicy : IOperatorAssignmentPolicy
{
    // Find on-shift operators with:
    // 1. trip.PickupWarehouseId IN shift.WarehouseScope
    // 2. CurrentTripId IS NULL (not busy)
    // 3. Has required certifications for cargo
    // Returns least-loaded eligible operator
}
```

### Step 4: Implement ManualVendorOperationAdapter

`Trip.Pause/Resume/Cancel` flows through router — Manual adapter ทำ vendor ops:

**`src/Modules/Transport.Manual/.../Application/Services/ManualVendorOperationAdapter.cs`**:

```csharp
public sealed class ManualVendorOperationAdapter : IVendorEnvelopeOperationService
{
    private readonly IManualTripExtensionRepository _ext;
    private readonly IPushNotificationGateway _push;

    public async Task<VendorOperationOutcome> PauseAsync(Guid tripId, CancellationToken ct)
    {
        var ext = await _ext.GetByTripIdAsync(tripId, ct);
        if (ext is null) return VendorOperationOutcome.NoVendorRecord;
        await _push.SendAsync(ext.AssignedOperatorId, new TripPausedNotification(tripId), ct);
        return VendorOperationOutcome.Accepted;
    }

    public async Task<VendorOperationOutcome> ResumeAsync(Guid tripId, CancellationToken ct) { ... }
    public async Task<VendorOperationOutcome> CancelAsync(Guid tripId, CancellationToken ct) { ... }
}
```

### Step 5: Mobile API Endpoints

ดูรายละเอียดที่ [Manual Operator API Spec](../api/manual-operator-api.md)

**`src/Modules/Transport.Manual/.../Presentation/OperatorEndpoints.cs`** (NEW):

```csharp
public static class OperatorEndpoints
{
    public static void MapOperatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator")
            .RequireAuthorization("OperatorAppPolicy")  // JWT audience=operator-app
            .WithTags("Operator");

        group.MapPost("/trips/assigned", GetAssignedTrips);
        group.MapPost("/trips/{tripId}/acknowledge", AcknowledgeTrip);
        group.MapPost("/trips/{tripId}/pickup", CapturePickup);
        group.MapPost("/trips/{tripId}/drop", CaptureDrop);
        group.MapPost("/trips/{tripId}/complete", CompleteTrip);
        group.MapPost("/trips/{tripId}/exception", RaiseException);
        group.MapPost("/trips/{tripId}/pause", OperatorPauseTrip);
        group.MapPost("/shift/clock-in", ClockIn);
        group.MapPost("/shift/clock-out", ClockOut);
        group.MapPost("/presence/heartbeat", Heartbeat);
        group.MapPost("/sync/batch", SyncBatch);
    }
}
```

**Key endpoint — Pickup capture with geofence**:
```csharp
private static async Task<IResult> CapturePickup(
    Guid tripId,
    [FromBody] CapturePickupRequest req,
    IMediator mediator,
    ICurrentOperator currentOp,
    CancellationToken ct)
{
    var cmd = new CaptureManualPickupCommand(
        TripId: tripId,
        OperatorId: currentOp.OperatorId,
        GpsCoord: req.GpsCoord,
        PhotoUrl: req.PhotoUrl);

    var result = await mediator.Send(cmd, ct);
    return result.IsValid
        ? Results.Ok(result)
        : Results.BadRequest(new { error = result.Reason });  // e.g. geofence violation
}
```

**Geofence check** — `CaptureManualPickupCommandHandler`:
```csharp
public async Task<CaptureResult> Handle(CaptureManualPickupCommand cmd, CancellationToken ct)
{
    var trip = await _trips.GetByIdAsync(cmd.TripId, ct);
    var warehouse = await _warehouses.GetAsync(trip.PickupWarehouseId, ct);

    if (warehouse.GeofenceRadiusM is { } radius &&
        DistanceMeters(cmd.GpsCoord, warehouse.Location) > radius) {
        return CaptureResult.Invalid($"GPS {cmd.GpsCoord} outside {radius}m of warehouse");
    }

    var ext = await _ext.GetByTripIdAsync(cmd.TripId, ct);
    ext.RecordPickup(DateTime.UtcNow, cmd.GpsCoord, cmd.PhotoUrl);
    trip.MarkPickedUp();  // or similar lifecycle method (Phase 3)

    await _ext.UpdateAsync(ext, ct);
    await _trips.UpdateAsync(trip, ct);
    return CaptureResult.Ok();
}
```

### Step 6: SLA Watchdog Background Service

**`src/Modules/Transport.Manual/.../Application/BackgroundServices/ManualTripSlaWatchdog.cs`**:

```csharp
public sealed class ManualTripSlaWatchdog : BackgroundService
{
    private readonly IManualTripExtensionRepository _ext;
    private readonly IPublishEndpoint _bus;
    private readonly TimeSpan _scanInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            var now = DateTime.UtcNow;
            var stalled = await _ext.FindStalledAsync(now, ct);  // EF query joining Trip

            foreach (var ext in stalled) {
                await _bus.Publish(new ManualTripStalledIntegrationEvent(
                    ext.TripId, ext.AssignedOperatorId, ext.StalledStage), ct);
            }

            await Task.Delay(_scanInterval, ct);
        }
    }
}
```

### Step 7: Push Notification Gateway

**`src/Modules/Transport.Manual/.../Infrastructure/Services/FcmPushNotificationGateway.cs`** (Firebase impl):

```csharp
public sealed class FcmPushNotificationGateway : IPushNotificationGateway
{
    private readonly HttpClient _http;
    private readonly IOptions<FcmOptions> _options;
    private readonly IOperatorDeviceRepository _devices;

    public async Task SendAsync(Guid operatorId, INotification notification, CancellationToken ct)
    {
        var devices = await _devices.GetByOperatorIdAsync(operatorId, ct);
        foreach (var device in devices) {
            await _http.PostAsJsonAsync(
                $"https://fcm.googleapis.com/v1/projects/{_options.Value.ProjectId}/messages:send",
                new {
                    message = new {
                        token = device.PushToken,
                        notification = new { title = notification.Title, body = notification.Body },
                        data = notification.Data
                    }
                },
                ct);
        }
    }
}
```

(Provide also `InMemoryPushNotificationGateway` for tests + dev)

### Step 8: DI Registration

**`src/Modules/Transport.Manual/.../TransportManualServiceCollectionExtensions.cs`** (full impl):

```csharp
public static class TransportManualServiceCollectionExtensions
{
    public static IServiceCollection AddTransportManual(this IServiceCollection services, IConfiguration config)
    {
        var manualConfig = config.GetSection("TransportModes:Manual");
        if (!manualConfig.GetValue<bool>("Enabled")) return services;

        services.Configure<ManualModeOptions>(manualConfig);

        // EF
        services.AddDbContext<ManualDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        // Domain repositories
        services.AddScoped<IOperatorRepository, OperatorRepository>();
        services.AddScoped<IOperatorShiftRepository, OperatorShiftRepository>();
        services.AddScoped<IOperatorDeviceRepository, OperatorDeviceRepository>();
        services.AddScoped<IManualTripExtensionRepository, ManualTripExtensionRepository>();

        // Services
        services.AddScoped<IDispatchStrategy, ManualDispatchStrategy>();
        services.AddScoped<IOperatorAssignmentPolicy, WarehouseScopeAssignmentPolicy>();
        services.AddScoped<IPushNotificationGateway, FcmPushNotificationGateway>();

        // Vendor ops adapter (for Pause/Resume routing)
        services.AddScoped<ManualVendorOperationAdapter>();

        // Background services
        services.AddHostedService<ManualTripSlaWatchdog>();
        services.AddHostedService<OperatorPresenceCleanupService>();

        // HTTP client
        services.AddHttpClient<FcmPushNotificationGateway>();

        return services;
    }
}
```

**Update VendorOperationsRouter** (in Transport.Amr — or move to Abstractions):
```csharp
public IVendorEnvelopeOperationService For(Trip trip) => trip.TransportMode switch
{
    TransportMode.Amr => _serviceProvider.GetRequiredService<Riot3VendorEnvelopeOperationAdapter>(),
    TransportMode.Manual => _serviceProvider.GetRequiredService<ManualVendorOperationAdapter>(),
    _ => throw new NotSupportedException($"Mode {trip.TransportMode} not supported")
};
```

### Step 9: Migrations (Additive)

```sql
-- 20260726000000_CreateTransportManualSchema.cs
CREATE SCHEMA transport_manual;

CREATE TABLE transport_manual.operators (
    id UUID PRIMARY KEY,
    employee_code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    phone VARCHAR(50) NOT NULL,
    email VARCHAR(200) NOT NULL,
    status VARCHAR(50) NOT NULL,
    hired_at TIMESTAMPTZ NOT NULL,
    terminated_at TIMESTAMPTZ
);

CREATE TABLE transport_manual.operator_certifications (
    id UUID PRIMARY KEY,
    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    valid_until TIMESTAMPTZ
);

CREATE TABLE transport_manual.operator_certs_join (
    operator_id UUID NOT NULL REFERENCES transport_manual.operators(id),
    certification_id UUID NOT NULL REFERENCES transport_manual.operator_certifications(id),
    PRIMARY KEY (operator_id, certification_id)
);

CREATE TABLE transport_manual.operator_shifts (
    id UUID PRIMARY KEY,
    operator_id UUID NOT NULL REFERENCES transport_manual.operators(id),
    shift_start TIMESTAMPTZ NOT NULL,
    shift_end TIMESTAMPTZ,
    warehouse_scope JSONB NOT NULL DEFAULT '[]',
    current_trip_id UUID
);

CREATE TABLE transport_manual.operator_devices (
    id UUID PRIMARY KEY,
    operator_id UUID NOT NULL REFERENCES transport_manual.operators(id),
    device_fingerprint VARCHAR(200) NOT NULL,
    push_token VARCHAR(500) NOT NULL,
    app_version VARCHAR(50),
    last_seen_at TIMESTAMPTZ NOT NULL,
    registered_at TIMESTAMPTZ NOT NULL,
    UNIQUE (operator_id, device_fingerprint)
);

CREATE TABLE transport_manual.manual_trip_extensions (
    trip_id UUID PRIMARY KEY REFERENCES dispatch.trips(id) ON DELETE CASCADE,
    assigned_operator_id UUID NOT NULL REFERENCES transport_manual.operators(id),
    operator_shift_id UUID NOT NULL REFERENCES transport_manual.operator_shifts(id),
    assigned_at TIMESTAMPTZ NOT NULL,
    acknowledged_at TIMESTAMPTZ,
    pickup_verified_at TIMESTAMPTZ,
    drop_verified_at TIMESTAMPTZ,
    pickup_gps_lat DOUBLE PRECISION, pickup_gps_lng DOUBLE PRECISION,
    drop_gps_lat DOUBLE PRECISION, drop_gps_lng DOUBLE PRECISION,
    pod_photo_url VARCHAR(1000),
    pod_signature_url VARCHAR(1000),
    pod_notes TEXT,
    expected_ack_by TIMESTAMPTZ NOT NULL,
    expected_pickup_by TIMESTAMPTZ NOT NULL,
    expected_drop_by TIMESTAMPTZ NOT NULL
);

CREATE INDEX ix_manual_trip_extensions_operator ON transport_manual.manual_trip_extensions(assigned_operator_id);
CREATE INDEX ix_manual_trip_extensions_sla ON transport_manual.manual_trip_extensions(expected_pickup_by, expected_drop_by)
    WHERE pickup_verified_at IS NULL OR drop_verified_at IS NULL;
```

### Step 10: Frontend — Operator Board

**`frontend/components/transport/manual/operator-board.tsx`** (NEW):

```tsx
export function OperatorBoard() {
  const { data: operators } = useSWR('/api/transport-manual/operators/active', fetcher);

  return (
    <Card>
      <CardHeader>Active Operators</CardHeader>
      <DataTable
        columns={[
          { key: 'name', label: 'Name' },
          { key: 'status', label: 'Status' },
          { key: 'currentTrip', label: 'Current Trip' },
          { key: 'lastSeen', label: 'Last Seen' },
          { key: 'gps', label: 'Location' }
        ]}
        data={operators}
      />
    </Card>
  );
}
```

**Update trip-action-bar.tsx** — mode-aware:
```tsx
{trip.mode === 'Manual' && (
  <>
    <Button onClick={onReassign}>Reassign Operator</Button>
    <Button onClick={onContact}>Contact</Button>
    <Button onClick={onOverride}>Override Status</Button>
  </>
)}
```

### Step 11: Mobile Audience JWT Setup

**`Program.cs`** — เพิ่ม JWT policy:
```csharp
builder.Services.AddAuthorization(opts => {
    opts.AddPolicy("OperatorAppPolicy", policy =>
        policy.RequireClaim("aud", "operator-app"));
});
```

JWT issuance — separate endpoint `/api/operator/auth/login` ที่ generate token ที่มี `aud=operator-app` + `operator_id` claim

## Verification

### Test Gates

```bash
dotnet build --configuration Release
dotnet test --no-build --logger "console;verbosity=minimal"
cd frontend && npm run typecheck && npm run lint && npm run build
```

### NEW Tests

```csharp
// tests/Modules/Transport.Manual.UnitTests/

[Fact]
public void OperatorShift_AssignTrip_Throws_WhenAlreadyHasTrip() { ... }

[Fact]
public async Task ManualDispatchStrategy_AssignsToEligibleOperator() { ... }

[Fact]
public async Task ManualDispatchStrategy_Returns_NoEligible_WhenAllBusy() { ... }

[Fact]
public async Task CaptureManualPickup_Rejects_OutsideGeofence() { ... }

[Fact]
public async Task SlaWatchdog_EmitsEvent_ForStaleTrip() { ... }

// tests/Modules/Transport.Manual.IntegrationTests/

[Fact]
public async Task OperatorMobileApi_HappyPath_Ack_Pickup_Drop_Complete() { ... }

[Fact]
public async Task OperatorMobileApi_SyncBatch_ReplaysOfflineEvents() { ... }
```

### Regression Tests (must stay green)

```bash
# AMR flow must not break
dotnet test --filter "FullyQualifiedName~Transport.Amr"
dotnet test tests/Integration/AMR.DeliveryPlanning.IntegrationTests/Riot3WebhookTests.cs
```

### Architecture Tests

```csharp
[Fact]
public void TransportManual_ShouldNotReferenceTransportAmr() { ... }

[Fact]
public void Dispatch_ShouldNotReferenceTransportManual() { ... }
```

### Manual Smoke Test

```
Setup:
- POST /api/transport-manual/operators (seed 2 operators with Standard cert)
- POST /api/transport-manual/operators/{id}/shift/clock-in (warehouseScope=[Warehouse A])

Manual dispatch flow:
1. POST /api/delivery-orders (TransportMode=Manual, PickupWarehouseId=A, DropWarehouseId=B)
2. Wait for dispatch → verify ManualTripExtension row created
3. Verify push notification sent (check log or test FCM gateway)
4. (Simulate mobile app) POST /api/operator/trips/{id}/acknowledge
   → verify ManualTripExtension.AcknowledgedAt set
   → verify Trip.Status = InProgress
5. POST /api/operator/trips/{id}/pickup (with GPS in geofence + photo URL)
   → verify ManualTripExtension.PickupVerifiedAt set
6. POST /api/operator/trips/{id}/pickup (with GPS OUT of geofence)
   → expect 400 with geofence error
7. POST /api/operator/trips/{id}/drop (with GPS + signature)
8. POST /api/operator/trips/{id}/complete
   → verify Trip.Status = Completed
9. Verify TripFactsRow projection has TransportMode=Manual + Duration metrics

SLA test:
1. Create Manual trip, don't acknowledge for 20 minutes
2. Wait for SlaWatchdog → verify ManualTripStalledIntegrationEvent emitted
3. Verify dispatcher console shows alert
```

## Before vs After

### Before
- Manual mode = enum value only
- No operator concept
- No mobile API
- Trip dispatch path = AMR only

### After
- Operator aggregate with shift + cert + device
- ManualDispatchStrategy assigns operator + push
- 11 mobile API endpoints
- SLA watchdog catches stalled trips
- POD with geofence verification + photo/signature
- Dispatcher console rendering both AMR + Manual

## Outcome

- ✓ Manual mode fully working end-to-end
- ✓ Operator domain ready for shift management + certification tracking
- ✓ Mobile API contract published (see [Manual Operator API Spec](../api/manual-operator-api.md))
- ✓ Trip FSM reused — same projections, BI reports
- ✓ SLA monitoring prevents stuck trips
- ✓ Cross-mode dispatcher console operational

## Risks & Mitigation

| Risk | Mitigation |
|---|---|
| Mobile app delays Phase 4 completion | API contract published early; web simulator for testing without app |
| Push notification delivery unreliable | Acknowledge timeout + poll fallback in API; manual reassign as last resort |
| GPS spoofing | Photo + signature required; audit log of GPS coords; spot-check by dispatcher |
| Operator app offline | `/sync/batch` endpoint with conflict resolution (server-wins for terminal states) |
| Operator goes off-network mid-trip | Presence watchdog escalates; dispatcher can override or reassign |

## Next Phase

→ [Phase 5: Implement Transport.Fleet](phase-5-transport-fleet.md)
