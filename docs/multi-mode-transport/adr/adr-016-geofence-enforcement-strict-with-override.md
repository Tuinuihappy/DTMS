# ADR-016: Geofence Enforcement — Server-Strict with Dispatcher Override

- **Status**: Accepted
- **Date**: 2026-06-25
- **Deciders**: Solo dev (DTMS)
- **Supersedes**: N/A (extends [ADR-010 Geofence Implementation](adr-010-geofence-implementation.md))
- **Related**: [ADR-010 Geofence Implementation (NTS)](adr-010-geofence-implementation.md), [ADR-015 POD Upload](adr-015-pod-upload-presigned-urls.md), [Phase 4: Transport.Manual](../phases/phase-4-transport-manual.md)

## Context

ADR-010 (2026-06-22) decided **HOW** to compute geofence containment (NetTopologySuite in-memory, WKT polygon storage). This ADR decides **WHAT BEHAVIOR** the server enforces when operator-submitted GPS is outside the geofence.

Phase 4 operator captures POD at pickup + drop. GPS coordinate is submitted along with photo/signature. Server must decide:
- Accept always (audit only)?
- Reject always (strict)?
- Reject with override path (strict + escape hatch)?
- Threshold-based zones (in/edge/out)?

### Threat model
| Threat | Severity |
|---|---|
| Operator capturing POD from home (fraud) | 🔴 High — direct financial loss (invoice fraud) |
| Operator at parking lot (~30m outside warehouse) | 🟡 Medium — gray area, often legit |
| Operator inside warehouse but GPS no fix (cold storage, metal racks) | 🟡 Medium — legit blocker for workflow |
| Spoofed GPS (mock location app) | 🔴 High — sophisticated fraud, pure GPS check vulnerable |

### Operational reality
Warehouses are not GPS-friendly:
- Metal racks block satellite signal
- Cold storage rooms have no sky view
- Loading docks near roads sometimes get city signal that triangulates to wrong block
- Operator phone GPS warm-up time can take 30-60s

Pure "reject when outside" breaks legit workflow on the GPS-unfriendly half of the warehouse.

## Decision

Use **Server-strict enforcement with dispatcher-approved override path**:

```
Operator submits POD with GPS:
  ├── GPS inside geofence → ✅ accept POD immediately
  ├── GPS outside geofence + has overrideRequestId → ✅ accept POD + flag "outsideGeofence=true" + record override usage
  └── GPS outside geofence (no override) → ❌ reject 403 with reason + distance + warehouse coords
                                            → PWA shows "Request dispatcher override" dialog
                                            → Operator submits override request
                                            → Dispatcher console gets realtime alert
                                            → Dispatcher approves or denies
                                            → On approval, operator retries POD with overrideRequestId
```

### Override request lifecycle
```csharp
public class GeofenceOverrideRequest
{
    public Guid Id { get; }
    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public Guid WarehouseId { get; }
    public double SubmittedLat { get; }
    public double SubmittedLng { get; }
    public double DistanceFromGeofenceM { get; }   // computed at request time
    public string OperatorReason { get; }           // "GPS dead inside cold storage"
    public DateTime SubmittedAt { get; }
    public OverrideStatus Status { get; private set; }   // Pending / Approved / Denied
    public Guid? DecidedByDispatcherId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DispatcherNote { get; private set; }
    
    public void Approve(Guid dispatcherId, string? note) { /* ... */ }
    public void Deny(Guid dispatcherId, string reason) { /* ... */ }
}

public enum OverrideStatus { Pending, Approved, Denied, Expired }
```

### Endpoints
```csharp
// PWA — submit override request when POD rejected
group.MapPost("/trips/{id}/geofence-override-request", async (...) => { ... });

// PWA — check status of own request (poll or via Web Push notification)
group.MapGet("/trips/{id}/geofence-override-request/{requestId}", async (...) => { ... });

// Dispatcher console — list pending requests
group.MapGet("/dispatch/geofence-override-requests?status=Pending", async (...) => { ... });

// Dispatcher console — approve / deny
group.MapPost("/dispatch/geofence-override-requests/{requestId}/approve", async (...) => { ... });
group.MapPost("/dispatch/geofence-override-requests/{requestId}/deny", async (...) => { ... });
```

### POD endpoint with override
```csharp
// PWA
public sealed record FinalizePodRequest(
    PodKind Kind,
    string ObjectKey,
    double Lat,
    double Lng,
    DateTime CaptureTime,
    string? Signature,
    Guid? OverrideRequestId = null);   // optional — set after dispatcher approval

// Server
public async Task<Result<ProofOfDelivery>> FinalizeAsync(...)
{
    var inside = await _geofence.IsInside(req.Lat, req.Lng, trip.PickupWarehouseId);
    
    if (!inside && req.OverrideRequestId is null)
    {
        var distance = await _geofence.DistanceFromBoundary(req.Lat, req.Lng, trip.PickupWarehouseId);
        return Result.Failure(new {
            error = "outside_geofence",
            distanceM = distance,
            instructions = "Request dispatcher override via POST /api/operator/trips/{id}/geofence-override-request"
        });
    }
    
    if (!inside && req.OverrideRequestId.HasValue)
    {
        var or = await _overrideRepo.GetAsync(req.OverrideRequestId.Value);
        if (or?.Status != OverrideStatus.Approved)
            return Result.Failure("Override not approved");
        // Continue — record usage
    }
    
    var pod = ProofOfDelivery.Create(
        tripId: tripId, kind: req.Kind, /* ... */,
        outsideGeofence: !inside,
        overrideRequestId: req.OverrideRequestId);
    // ...
}
```

## Reasoning — Why "strict + override" over alternatives

### Alternatives ruled out
| Option | Why ruled out |
|---|---|
| **Server-strict, no override** | Operators stuck in GPS-dead zones (cold storage, metal racks). Breaks workflow, forces dispatcher to manually adjust warehouse geofence each time. |
| **Server-warn (always accept, log)** | Doesn't deter fraud at point of capture. POD invoiced before audit catches the issue. Logistics fraud has $ cost — need block-at-source. |
| **Adaptive zones (50m strict, 200m warn)** | Arbitrary thresholds. Adversarial-friendly (fraud can stand at 199m). No human-in-loop for ambiguous cases. |
| **Client-only enforcement** | Trivially bypassable (modify PWA code). Anti-pattern for fraud-sensitive. |

### Why strict + override wins
1. **Strong default** — fraud blocked at point of capture
2. **Escape hatch** — legit GPS issues unblocked via human-in-loop
3. **Audit trail** — every override visible to dispatcher = fraud pattern visible
4. **Operator UX** — clear "you're 50m outside, request override?" beats silent rejection
5. **Dispatcher latency** — override approval ~1-2 min in practice (Web Push to dispatcher → click approve)

### Operational characteristics
| Scenario | Outcome |
|---|---|
| GPS good, inside | ✅ accept instantly |
| GPS good, 40m outside (parking lot near loading dock) | ❌ reject → operator requests override → dispatcher sees "40m, GPS good" → approves quickly |
| GPS dead inside cold storage | ❌ reject → operator requests override with reason "cold storage GPS dead" → dispatcher knows pattern → approves |
| Fraud from home (5km) | ❌ reject → operator requests override → dispatcher sees "5km away" → denies, flags for investigation |
| GPS spoofed near warehouse | ✅ accept (any pure GPS check vulnerable — Phase 5 adds WiFi BSSID, photo EXIF defense in depth) |

### Anti-spoof defense (deferred to Phase 5)
GPS spoofing is the one threat strict+override doesn't block. Future enhancements (out of Phase 4 scope):
1. **WiFi BSSID fingerprint** — operator's connected WiFi AP must match warehouse's known APs
2. **Photo EXIF GPS** — extract GPS from photo metadata, compare to claimed GPS
3. **Time velocity check** — operator can't be at warehouse A → warehouse B 100km away 5 min later
4. **Device attestation** — Play Integrity / App Attest (native only; not applicable to PWA)

These add as additional checks layered on top of geofence. Strict+override remains the baseline.

## Implementation Sketch (Phase 4.2-4.3)

### Domain entity (new in Transport.Manual.Domain — Phase 4.1)
```csharp
public class GeofenceOverrideRequest
{
    // fields as above
    
    public static GeofenceOverrideRequest Create(
        Guid tripId, Guid operatorId, Guid warehouseId,
        double lat, double lng, double distance, string reason)
    {
        // validation: distance > 0, reason not empty, etc.
        return new GeofenceOverrideRequest { /* ... */ };
    }
}
```

### Service interface
```csharp
public interface IGeofenceService
{
    Task<bool> IsInsideAsync(double lat, double lng, Guid warehouseId, CancellationToken ct);
    Task<double> DistanceFromBoundaryAsync(double lat, double lng, Guid warehouseId, CancellationToken ct);
}

public class NtsGeofenceService : IGeofenceService
{
    // Uses NetTopologySuite per ADR-010
    // Loads warehouse polygon WKT, evaluates Contains() / Distance()
}
```

### Realtime notification to dispatcher
Override requests need dispatcher attention quickly. Use existing SignalR hub:

```csharp
public class GeofenceOverrideRequestHandler : INotificationHandler<GeofenceOverrideRequestSubmittedDomainEvent>
{
    private readonly IHubContext<DispatchHub> _hub;
    
    public async Task Handle(GeofenceOverrideRequestSubmittedDomainEvent notification, CancellationToken ct)
    {
        await _hub.Clients.Group("dispatchers").SendAsync(
            "geofence-override-pending",
            new {
                requestId = notification.RequestId,
                tripId = notification.TripId,
                operatorName = notification.OperatorDisplayName,
                distance = notification.DistanceM,
                reason = notification.Reason,
                submittedAt = notification.SubmittedAt,
            }, ct);
    }
}
```

Dispatcher console shows a toast / banner; clicking opens override decision modal.

### Audit + reporting (Phase 4.5+)
- `GeofenceOverrideRequest` records all decisions → queryable for ops reporting
- Dashboard: "Override approval rate per operator" (red flag if operator has unusual approval pattern)
- Dashboard: "Override approval rate per warehouse" (high rate = geofence boundary too tight → adjust)

## Open questions

1. **Override approval auto-expire** — Should approval expire if operator doesn't retry within N minutes? (Yes — 15 min default)
2. **Same operator, same trip, multiple override requests** — Allow or deny? (Allow — different reasons for pickup vs drop)
3. **Dispatcher notification rate limit** — If operator hammers requests, throttle? (Yes — 1 request per 60s per operator)
4. **Override approval delegation** — Can a senior operator approve another operator's override, or admin-only? (Phase 5 — admin only for now)

## Consequences

### Positive
- ✅ Fraud blocked at point of capture (no POD created if outside without override)
- ✅ Audit trail of every override (queryable for fraud investigation)
- ✅ Operator UX clear (knows why rejected + how to proceed)
- ✅ Dispatcher human-in-loop = handles ambiguous cases better than rule-based zones

### Negative
- ❌ Override approval requires dispatcher availability (latency ~1-2 min)
- ❌ +4 hours implementation effort vs simple strict (override entity + endpoints + UI)
- ❌ Doesn't block GPS spoofing alone (mitigation: Phase 5 add WiFi BSSID, EXIF GPS)

### Neutral
- 🟡 Override request UI requires dispatcher console real-time updates (already have SignalR — minimal new infra)
- 🟡 PWA needs Push to notify operator when override approved (per ADR-013 — already in scope)

## Why this extends (not supersedes) ADR-010

ADR-010 chose the **mechanism** (NetTopologySuite in-memory, WKT polygon storage). This ADR chooses the **behavior** (strict + override). They are complementary — `NtsGeofenceService` implements ADR-010 mechanics; the strict+override behavior is enforced in `PodFinalizationService` (per ADR-015) and `GeofenceOverrideRequest` aggregate (this ADR).

No conflict with ADR-010 — only extends with behavioral policy.
