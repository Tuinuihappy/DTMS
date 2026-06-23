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

### Step 10: Frontend — Operator Board & Mode-Aware UI (Detail)

> Visual reference: [UI Mockups — Operator Board](../diagrams/ui-mockups.md#2-operator-board-phase-4)
> Conventions: [ADR-011 Frontend Architecture](../adr/adr-011-frontend-architecture.md)
> This phase is the **largest frontend addition** in the entire refactor (~10-15 days)

#### 10.1 New Hooks (foundation for all Manual UI)

**`frontend/lib/hooks/use-capabilities.ts`** (NEW — also used by Phase 5):

```tsx
"use client";

import useSWR from "swr";

export type SystemCapabilities = {
  enabledModes: ('Amr' | 'Manual' | 'Fleet')[];
  manual?: {
    geofenceEnforcement: boolean;
    photoRequired: boolean;
    signatureRequired: boolean;
    offlineSyncEnabled: boolean;
  };
  fleet?: {
    enabledProviders: string[];
  };
};

export function useCapabilities() {
  return useSWR<SystemCapabilities>(
    '/api/system/capabilities',
    fetcher,
    {
      revalidateOnFocus: false,
      dedupingInterval: 60_000,    // cache 1 min
    },
  );
}
```

**`frontend/lib/hooks/use-operator-presence.ts`** (NEW):

```tsx
export function useOperatorPresence(operatorId: string) {
  const { data, mutate } = useSWR<OperatorPresenceDto>(
    `/api/transport-manual/operators/${operatorId}/presence`,
    fetcher,
    { refreshInterval: 30_000 },
  );

  useEffect(() => {
    // SignalR invalidation signal — no payload, just trigger refetch
    const sub = signalR.subscribe(`operator-presence:${operatorId}`, () => mutate());
    return sub.unsubscribe;
  }, [operatorId, mutate]);

  return { presence: data };
}
```

**`frontend/lib/hooks/use-stalled-trips.ts`** (NEW):

```tsx
export function useStalledTrips() {
  return useSWR<StalledTripDto[]>(
    '/api/transport-manual/trips/stalled',
    fetcher,
    { refreshInterval: 60_000 },
  );
}
```

#### 10.2 API Layer

**`frontend/lib/api/transport-manual.ts`** (NEW):

```typescript
const API_BASE = "/api/transport-manual";

export type OperatorDto = {
  id: string;
  employeeCode: string;
  name: string;
  phone: string;
  email: string;
  status: 'Active' | 'Suspended' | 'OffDuty' | 'Terminated';
  certifications: string[];                   // ['STANDARD', 'HAZMAT']
};

export type OperatorBoardItemDto = {
  operator: OperatorDto;
  shift: {
    id: string;
    shiftStart: string;
    warehouseScope: string[];                 // warehouse IDs
  };
  currentTripId: string | null;
  currentTripSummary: {                       // null if idle
    id: string;
    pickupWarehouseCode: string;
    dropWarehouseCode: string;
    expectedDropBy: string;
    status: TripStatus;
  } | null;
  device: {
    id: string;
    appVersion: string;
    lastSeenAt: string;
    batteryPercent: number | null;
    networkType: string | null;
  };
  gpsCoord: { lat: number; lng: number; accuracyM: number } | null;
  presence: 'Active' | 'Warning' | 'Critical' | 'Unknown';
  tripsToday: { completed: number; inProgress: number };
};

export type ManualTripExtensionDto = {
  tripId: string;
  assignedOperatorId: string;
  assignedOperatorName: string;
  operatorShiftId: string;
  assignedAt: string;
  acknowledgedAt: string | null;
  pickupVerifiedAt: string | null;
  dropVerifiedAt: string | null;
  pickupGpsCoord: { lat: number; lng: number } | null;
  dropGpsCoord: { lat: number; lng: number } | null;
  podPhotoUrl: string | null;
  podSignatureUrl: string | null;
  podNotes: string | null;
  expectedAckBy: string;
  expectedPickupBy: string;
  expectedDropBy: string;
};

export async function getOperatorBoard(opts?: {
  warehouseId?: string;
  status?: 'on-shift' | 'in-trip' | 'idle' | 'alert';
  search?: string;
}): Promise<OperatorBoardItemDto[]> { ... }

export async function getOperator(id: string): Promise<OperatorDto> { ... }

export async function getManualTripExtension(tripId: string): Promise<ManualTripExtensionDto | null> { ... }

export async function assignOperatorToTrip(tripId: string, operatorId: string): Promise<void> { ... }

export async function reassignOperator(tripId: string, newOperatorId: string, reason: string): Promise<void> { ... }

export async function contactOperator(operatorId: string, message: string): Promise<void> { ... }
```

**`frontend/lib/api/system.ts`** (NEW):
```typescript
export async function getCapabilities(): Promise<SystemCapabilities> { ... }
```

**`frontend/lib/transport/manual/sla-formatters.ts`** (NEW):

```typescript
export function formatSlaCountdown(expectedBy: string): string {
  // "in 15m", "2h overdue", etc.
}

export function getSlaSeverity(expectedBy: string): 'ok' | 'warning' | 'breach' {
  const now = Date.now();
  const target = Date.parse(expectedBy);
  const diffMin = (target - now) / 60_000;
  if (diffMin < 0) return 'breach';
  if (diffMin < 15) return 'warning';
  return 'ok';
}
```

#### 10.3 Operator Board Components

**`frontend/components/transport/manual/operator-board.tsx`** (NEW — full implementation matching mockup):

```tsx
"use client";

import { useState } from "react";
import useSWR from "swr";
import { getOperatorBoard } from "@/lib/api/transport-manual";
import { OperatorCard } from "./operator-card";
import { OperatorBoardStats } from "./operator-board-stats";
import { OperatorBoardFilters } from "./operator-board-filters";
import { StalledTripsAlert } from "./stalled-trips-alert";
import { EmptyState } from "@/components/primitives/empty-state";
import { Users } from "lucide-react";
import { useRouter } from "next/navigation";

export function OperatorBoard() {
  const [warehouseFilter, setWarehouseFilter] = useState<string | undefined>();
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [search, setSearch] = useState("");

  const { data: operators, isLoading } = useSWR(
    ['operator-board', warehouseFilter, statusFilter, search],
    () => getOperatorBoard({ warehouseId: warehouseFilter, status: statusFilter as any, search }),
    { refreshInterval: 5_000 },
  );

  const router = useRouter();

  if (isLoading) return <OperatorBoardSkeleton />;

  if (!operators?.length) {
    return (
      <EmptyState
        icon={<Users className="h-12 w-12" />}
        title="No operators currently on shift"
        description="Operators will appear here when they clock in"
        action={{ label: "Manage operators", onClick: () => router.push('/admin/operators') }}
      />
    );
  }

  // Sort: critical first
  const sorted = [...operators].sort((a, b) => severityRank(b.presence) - severityRank(a.presence));

  return (
    <div className="space-y-4">
      <OperatorBoardFilters
        warehouseFilter={warehouseFilter}
        onWarehouseChange={setWarehouseFilter}
        statusFilter={statusFilter}
        onStatusChange={setStatusFilter}
        search={search}
        onSearchChange={setSearch}
      />

      <OperatorBoardStats operators={operators} />

      <StalledTripsAlert />

      <div className="space-y-3">
        {sorted.map(item => (
          <OperatorCard key={item.operator.id} item={item} />
        ))}
      </div>

      <p className="text-sm text-muted-foreground text-right">
        Showing {sorted.length} of {operators.length}
      </p>
    </div>
  );
}

function severityRank(presence: string): number {
  return { Critical: 4, Warning: 3, Active: 2, Unknown: 1 }[presence] ?? 0;
}
```

**`frontend/components/transport/manual/operator-card.tsx`** (NEW):

Per mockup "Expanded card details" — collapsible with sub-sections (Shift, Current Trip, Device)

**`frontend/components/transport/manual/operator-presence-badge.tsx`** (NEW):

```tsx
export function OperatorPresenceBadge({ presence }: { presence: PresenceState }) {
  const config = {
    Active:   { color: 'text-success',          label: 'Active',   ariaLabel: 'Operator is active' },
    Warning:  { color: 'text-warning',          label: 'Warning',  ariaLabel: 'Operator may be unreachable' },
    Critical: { color: 'text-destructive',      label: 'Critical', ariaLabel: 'Operator critical alert' },
    Unknown:  { color: 'text-muted-foreground', label: 'Unknown',  ariaLabel: 'Operator status unknown' },
  }[presence];

  return (
    <span aria-label={config.ariaLabel} className={cn("inline-flex items-center gap-1", config.color)}>
      <Circle className="h-2 w-2 fill-current" />
      <span className="text-xs">{config.label}</span>
    </span>
  );
}
```

**`frontend/components/transport/manual/sla-breach-alert.tsx`** (NEW):

```tsx
export function StalledTripsAlert() {
  const { data: stalled } = useStalledTrips();

  if (!stalled?.length) return null;

  return (
    <Alert variant="warning" role="alert">
      <AlertTriangle className="h-4 w-4" />
      <AlertTitle>Action Required</AlertTitle>
      <AlertDescription>
        {stalled.length} {stalled.length === 1 ? 'trip has' : 'trips have'} breached SLA in the last hour
      </AlertDescription>
      <Link href="/operator-board/stalled" className="mt-2 inline-block text-sm font-medium underline">
        Review all →
      </Link>
    </Alert>
  );
}
```

#### 10.4 Operator Assignment Dialog

**`frontend/components/transport/manual/operator-picker.tsx`** (NEW):

Modal for dispatcher to assign Manual trip to specific operator:

```tsx
export function OperatorPicker({
  trip,
  onAssigned,
  onCancel,
}: {
  trip: TripDto;
  onAssigned: () => void;
  onCancel: () => void;
}) {
  // Lists operators eligible for trip:
  // - On shift in warehouse scope
  // - Certified for cargo (HAZMAT etc.)
  // - Not currently in trip
  // Shows trip count today (load balancing hint)
}
```

**`frontend/components/transport/manual/reassign-operator-dialog.tsx`** (NEW):

```tsx
export function ReassignOperatorDialog({ trip, currentOperator, onReassigned }: Props) {
  // Show current operator + reason
  // Pick new operator (filtered same as OperatorPicker)
  // Confirm cascade: release current, assign new, send push to both
}
```

#### 10.5 Mode-Aware Trip Action Bar Updates

**Update [trip-action-bar.tsx](../../../frontend/components/dispatch/trip-action-bar.tsx)** — add Manual section:

```tsx
"use client";

import { useCapabilities } from "@/lib/hooks/use-capabilities";

export function TripActionBar({ trip, onAction }: Props) {
  const { data: caps } = useCapabilities();

  return (
    <div className="flex flex-wrap gap-2">
      {/* Common actions (Cancel/Pause/Resume) — visible for ALL modes */}
      {trip.status === 'InProgress' && (
        <Button variant="secondary" onClick={() => handlePause()}>Pause</Button>
      )}
      {/* ... cancel, resume buttons ... */}

      {/* Mode-specific action panels */}
      {trip.mode === 'Amr' && <AmrActions trip={trip} onAction={onAction} />}
      {trip.mode === 'Manual' && <ManualActions trip={trip} onAction={onAction} />}
      {/* Fleet section added in Phase 5 */}
    </div>
  );
}

// frontend/components/transport/manual/manual-actions.tsx
function ManualActions({ trip, onAction }: { trip: TripDto; onAction: (action: string) => void }) {
  const [reassignOpen, setReassignOpen] = useState(false);
  const [contactOpen, setContactOpen] = useState(false);

  return (
    <>
      {trip.status === 'InProgress' && (
        <>
          <Button variant="outline" onClick={() => setReassignOpen(true)}>
            <UserPlus className="mr-2 h-4 w-4" />
            Reassign Operator
          </Button>
          <Button variant="outline" onClick={() => setContactOpen(true)}>
            <MessageCircle className="mr-2 h-4 w-4" />
            Contact
          </Button>
        </>
      )}
      <Button variant="outline" onClick={() => onAction('override')}>
        <Settings className="mr-2 h-4 w-4" />
        Override Status
      </Button>
      <ReassignOperatorDialog ... open={reassignOpen} ... />
      <ContactOperatorDialog ... open={contactOpen} ... />
    </>
  );
}
```

#### 10.6 Manual Trip Extension Panel

**`frontend/components/transport/manual/manual-trip-extension-panel.tsx`** (NEW):

Similar to AmrTripExtensionPanel but for Manual:
- Operator name + presence badge
- SLA countdown for each stage (Ack / Pickup / Drop)
- POD photo + signature thumbnails (clickable to enlarge)
- Geofence verification status

```tsx
export function ManualTripExtensionPanel({ tripId }: { tripId: string }) {
  const { data: ext } = useSWR(...);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Manual Delivery Details</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <OperatorSection ext={ext} />
        <SlaCountdownSection ext={ext} />
        <PodSection ext={ext} />
      </CardContent>
    </Card>
  );
}
```

#### 10.7 Geofence Editor

**`frontend/components/transport/manual/geofence-editor.tsx`** (NEW):

Used in `app/admin/facility/[id]/page.tsx` for warehouse admin:

```tsx
import { MapContainer, FeatureGroup, Circle, Polygon } from 'react-leaflet';
import { EditControl } from 'react-leaflet-draw';

export function GeofenceEditor({ facility, onSave }: Props) {
  const [geofence, setGeofence] = useState<Geofence>(facility.geofence);

  return (
    <div>
      <RadioGroup value={geofence.type} onValueChange={...}>
        <RadioGroupItem value="circle">Circle (radius)</RadioGroupItem>
        <RadioGroupItem value="polygon">Polygon (custom shape)</RadioGroupItem>
      </RadioGroup>

      <MapContainer center={facility.location} zoom={17} style={{ height: 500 }}>
        <FeatureGroup>
          <EditControl
            position="topright"
            draw={{ rectangle: false, circle: geofence.type === 'circle', polygon: geofence.type === 'polygon', marker: false }}
            onCreated={handleCreated}
            onEdited={handleEdited}
          />
          {/* Render existing geofence */}
        </FeatureGroup>
      </MapContainer>

      <Button onClick={() => onSave(geofence)}>Save Geofence</Button>
    </div>
  );
}
```

#### 10.8 Map Integration — Add Operator Layer

**Update `frontend/components/facility/facility-map.tsx`** (existing):

Add operator markers alongside robot positions (different icon):

```tsx
export function FacilityMap({ facilityId }: { facilityId: string }) {
  const { data: caps } = useCapabilities();

  return (
    <MapContainer center={...} zoom={...}>
      <TileLayer .../>

      {caps?.enabledModes.includes('Amr') && (
        <RobotLayer facilityId={facilityId} />
      )}

      {caps?.enabledModes.includes('Manual') && (
        <OperatorLayer facilityId={facilityId} />
      )}

      <GeofenceLayer facilityId={facilityId} />
    </MapContainer>
  );
}
```

**`frontend/components/transport/manual/operator-layer.tsx`** (NEW):

```tsx
export function OperatorLayer({ facilityId }: { facilityId: string }) {
  const { data: operators } = useSWR(
    `/api/transport-manual/operators/active-in-facility?facilityId=${facilityId}`,
    fetcher,
    { refreshInterval: 5_000 },
  );

  return (
    <>
      {operators?.map(op => (
        <Marker key={op.id} position={[op.gpsCoord.lat, op.gpsCoord.lng]} icon={operatorIcon}>
          <Popup>
            <strong>{op.name}</strong><br />
            Trip: {op.currentTripId ?? 'idle'}<br />
            Last seen: <DateTime value={op.device.lastSeenAt} relative />
          </Popup>
        </Marker>
      ))}
    </>
  );
}
```

#### 10.9 Navigation Updates

**Update `frontend/components/layout/main-nav.tsx`** (existing):

Conditional nav items based on capabilities:

```tsx
const { data: caps } = useCapabilities();

return (
  <nav>
    <NavLink href="/trips">Trips</NavLink>
    <NavLink href="/orders">Orders</NavLink>

    {caps?.enabledModes.includes('Manual') && (
      <>
        <NavLink href="/operator-board">Operator Board</NavLink>
        <NavLink href="/admin/operators">Operators</NavLink>
      </>
    )}

    {caps?.enabledModes.includes('Amr') && (
      <NavLink href="/admin/facility">Facility Maps</NavLink>
    )}
  </nav>
);
```

#### 10.10 New Pages (Next.js App Router)

| Route | Page | Description |
|---|---|---|
| `/operator-board` | `app/(console)/operator-board/page.tsx` | Live dispatcher view |
| `/operator-board/stalled` | `app/(console)/operator-board/stalled/page.tsx` | SLA-breached trips list |
| `/admin/operators` | `app/(admin)/operators/page.tsx` | Operator CRUD |
| `/admin/operators/[id]` | `app/(admin)/operators/[id]/page.tsx` | Operator detail (shifts, certs, audit) |
| `/admin/operators/new` | `app/(admin)/operators/new/page.tsx` | Onboard new operator |
| `/admin/facility/[id]/geofence` | `app/(admin)/facility/[id]/geofence/page.tsx` | Geofence editor |

#### 10.11 Dashboard KPI Addition

**`frontend/components/dashboard/manual-operations-kpis.tsx`** (NEW):

```tsx
export function ManualOperationsKpis() {
  const { data: caps } = useCapabilities();
  if (!caps?.enabledModes.includes('Manual')) return null;

  const { data: kpis } = useSWR('/api/transport-manual/kpis', fetcher);

  return (
    <Card>
      <CardHeader><CardTitle>Manual Operations</CardTitle></CardHeader>
      <CardContent className="grid grid-cols-2 gap-4">
        <KpiTile label="Active operators" value={kpis?.activeOperators} />
        <KpiTile label="Avg pickup time" value={kpis?.avgPickupMin} unit="min" />
        <KpiTile label="SLA hit rate" value={kpis?.slaHitRate} unit="%" />
        <KpiTile label="POD compliance" value={kpis?.podCompliance} unit="%" />
      </CardContent>
    </Card>
  );
}
```

### Phase 4 Frontend Manual Smoke Checklist

```
□ /operator-board page loads — shows live operator list
□ Operator card expands on click — shows shift + trip + device details
□ Presence badge updates via SignalR when operator goes offline
□ Filters work: warehouse, status, search
□ Empty state shown when no operators on shift
□ Stalled trips alert appears when SLA breach happens (test by short-cycle SLA)
□ Sort: critical-first when operators in mixed states
□ Operator Layer on facility map shows live GPS markers
□ Click operator marker → popup with current trip + last seen
□ Geofence editor: draw circle on map — save → persist
□ Geofence editor: draw polygon on map — save → persist
□ Manual Trip detail drawer shows ManualTripExtensionPanel
□ SLA countdown updates in real-time (using setInterval or relative DateTime)
□ POD photo thumbnail in panel → click to enlarge
□ POD signature thumbnail renders correctly
□ Trip action bar shows Manual buttons (Reassign / Contact / Override)
□ Reassign dialog: shows eligible operators, prevents double-assign
□ Contact dialog: sends message → success toast
□ Override Status dialog: requires reason text
□ Admin /operators page: CRUD operations work
□ Operator detail page: shift history paginated
□ Geofence violation on test pickup (simulate GPS off-warehouse) → error in dispatcher console
□ Navigation: Operator Board link only shows when Manual mode enabled
□ Navigation: hidden when capabilities API returns Manual=disabled
□ Dashboard: Manual Operations KPI card renders
□ Dark mode: all new components readable
□ Mobile breakpoint: operator board collapses to single-column
□ Accessibility: presence badges have ARIA labels
□ Accessibility: live regions for SignalR updates announce changes
```

### Phase 4 Frontend Effort Breakdown

| Task | Effort |
|---|---|
| Hooks (`useCapabilities`, `useOperatorPresence`, `useStalledTrips`) | 1 day |
| API client (`transport-manual.ts`, `system.ts`) | 1 day |
| Operator Board page + components (Card, Stats, Filters, Alert) | 3 days |
| ManualTripExtensionPanel | 1 day |
| OperatorPicker + ReassignOperatorDialog + ContactDialog | 2 days |
| Geofence editor (Leaflet + react-leaflet-draw) | 2 days |
| Operator layer on facility map | 1 day |
| Admin pages (Operators CRUD + detail) | 2 days |
| Navigation + Dashboard KPI updates | 0.5 day |
| Trip action bar mode-aware refactor | 1 day |
| Manual smoke + bug fixes + a11y audit | 1.5 days |
| **Total** | **~15 days (3 sprint weeks)** |

### Phase 4 Frontend Risks

| Risk | Mitigation |
|---|---|
| Leaflet + react-leaflet-draw compatibility with Next 16 | Spike day 1; fallback to Mapbox if blocker |
| SignalR connection lifecycle (memory leaks on route change) | Wrap in custom hook with cleanup in useEffect return |
| POD image loading slow (S3 cold cache) | Thumbnail proxy endpoint server-side; lazy load |
| Operator board re-render storm on busy day | Memoize OperatorCard; SWR refresh throttle |
| Mobile breakpoint untested | Add to manual smoke checklist explicitly |

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
