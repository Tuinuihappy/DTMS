# Manual Operator API — Contract Spec

API contract สำหรับ Operator mobile app — used in Phase 4 ([phase-4-transport-manual.md](../phases/phase-4-transport-manual.md))

- **Base URL**: `https://{api-host}/api/operator`
- **Auth**: JWT Bearer token, audience=`operator-app`, claim=`operator_id`
- **Content-Type**: `application/json`
- **Date format**: ISO 8601 UTC (e.g., `2026-06-22T10:30:00Z`)
- **GPS format**: WGS84 decimal degrees

---

## Authentication

### POST /api/operator/auth/login

Login + device registration

**Request:**
```json
{
  "employeeCode": "EMP-001",
  "password": "...",
  "deviceFingerprint": "iPhone14-A1B2C3D4",
  "pushToken": "fcm-token-xxx",
  "appVersion": "1.0.3"
}
```

**Response 200:**
```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "eyJhbGc...",
  "expiresAt": "2026-06-22T18:00:00Z",
  "operator": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "John Doe",
    "employeeCode": "EMP-001",
    "certifications": ["STANDARD", "HAZMAT"]
  }
}
```

**Response 401:** Invalid credentials

---

## Trips

### POST /api/operator/trips/assigned

Get current assignments (polled by mobile app, offline-friendly)

**Request:** (empty body)

**Response 200:**
```json
{
  "trips": [
    {
      "tripId": "650e8400-...",
      "orderId": "750e8400-...",
      "status": "Created",
      "assignedAt": "2026-06-22T10:00:00Z",
      "pickup": {
        "warehouseId": "...",
        "warehouseCode": "WH-BKK-01",
        "warehouseName": "Bangkok DC",
        "address": "123 Sukhumvit Rd, Bangkok",
        "location": { "lat": 13.7, "lng": 100.5 },
        "geofenceRadiusM": 100,
        "contact": { "name": "Warehouse Manager", "phone": "+66..." }
      },
      "drop": {
        "warehouseId": "...",
        "warehouseCode": "WH-CNX-01",
        "warehouseName": "Chiang Mai DC",
        "address": "...",
        "location": { "lat": 18.8, "lng": 98.9 },
        "geofenceRadiusM": 100,
        "contact": { "name": "...", "phone": "..." }
      },
      "items": [
        {
          "itemId": "...",
          "description": "Standard pallet x 5",
          "weightKg": 500,
          "dimensions": { "lengthMm": 1200, "widthMm": 800, "heightMm": 1500 }
        }
      ],
      "sla": {
        "expectedAckBy": "2026-06-22T10:15:00Z",
        "expectedPickupBy": "2026-06-22T11:30:00Z",
        "expectedDropBy": "2026-06-22T14:30:00Z"
      }
    }
  ]
}
```

---

### POST /api/operator/trips/{tripId}/acknowledge

Operator accepts the trip

**Request:**
```json
{
  "acknowledgedAt": "2026-06-22T10:05:00Z"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "status": "InProgress",
  "acknowledgedAt": "2026-06-22T10:05:00Z"
}
```

**Response 409:** Trip not in Created status or already acknowledged

---

### POST /api/operator/trips/{tripId}/pickup

Record pickup at warehouse (geofence-verified)

**Request:**
```json
{
  "occurredAt": "2026-06-22T11:00:00Z",
  "gpsCoord": { "lat": 13.701, "lng": 100.501 },
  "photoUrl": "https://storage.dtms.com/pod/photos/abc123.jpg",
  "notes": "All 5 pallets loaded"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "pickupVerifiedAt": "2026-06-22T11:00:00Z"
}
```

**Response 400:**
```json
{
  "error": "GeofenceViolation",
  "message": "GPS coordinate is 250m from warehouse (allowed: 100m)",
  "warehouseLocation": { "lat": 13.7, "lng": 100.5 },
  "providedLocation": { "lat": 13.702, "lng": 100.504 },
  "distanceM": 250,
  "allowedM": 100
}
```

**Response 409:** Trip not in correct state for pickup

---

### POST /api/operator/trips/{tripId}/drop

Record drop at destination (geofence-verified + POD required)

**Request:**
```json
{
  "occurredAt": "2026-06-22T14:00:00Z",
  "gpsCoord": { "lat": 18.801, "lng": 98.901 },
  "photoUrl": "https://storage.dtms.com/pod/photos/def456.jpg",
  "signatureUrl": "https://storage.dtms.com/pod/signatures/def456.png",
  "receiverName": "Jane Smith",
  "notes": "Delivered to receiving bay 3"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "dropVerifiedAt": "2026-06-22T14:00:00Z"
}
```

**Response 400:** Geofence violation OR missing signature (required)

---

### POST /api/operator/trips/{tripId}/complete

Mark trip as completed (after pickup + drop both verified)

**Request:**
```json
{
  "completedAt": "2026-06-22T14:05:00Z"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "status": "Completed",
  "completedAt": "2026-06-22T14:05:00Z"
}
```

**Response 409:** Pickup or drop not yet recorded

---

### POST /api/operator/trips/{tripId}/exception

Raise an issue (truck breakdown, address wrong, recipient unavailable, etc.)

**Request:**
```json
{
  "occurredAt": "2026-06-22T11:30:00Z",
  "category": "AddressNotFound",
  "severity": "Blocker",
  "description": "Address in order doesn't match physical location",
  "gpsCoord": { "lat": 18.8, "lng": 98.9 },
  "photoUrls": ["https://storage.dtms.com/exceptions/xxx.jpg"]
}
```

**Categories**: `VehicleBreakdown`, `AddressNotFound`, `RecipientUnavailable`, `CargoDamaged`, `Refused`, `SafetyIssue`, `Other`
**Severities**: `Info`, `Warning`, `Blocker`

**Response 200:**
```json
{
  "exceptionId": "...",
  "tripStatus": "Paused"
}
```

---

### POST /api/operator/trips/{tripId}/pause

Operator-initiated pause (e.g., break, traffic delay)

**Request:**
```json
{
  "pausedAt": "2026-06-22T12:00:00Z",
  "reason": "Lunch break"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "status": "Paused"
}
```

---

### POST /api/operator/trips/{tripId}/resume

Resume after pause

**Request:**
```json
{
  "resumedAt": "2026-06-22T12:30:00Z"
}
```

**Response 200:**
```json
{
  "tripId": "...",
  "status": "InProgress"
}
```

---

## Shift Management

### POST /api/operator/shift/clock-in

Start a shift

**Request:**
```json
{
  "clockedInAt": "2026-06-22T08:00:00Z",
  "warehouseScope": ["550e8400-..."],
  "vehicleId": "650e8400-..."
}
```

**Response 200:**
```json
{
  "shiftId": "...",
  "operatorId": "...",
  "shiftStart": "2026-06-22T08:00:00Z",
  "warehouseScope": [...]
}
```

**Response 409:** Already on active shift

---

### POST /api/operator/shift/clock-out

End shift

**Request:**
```json
{
  "clockedOutAt": "2026-06-22T17:00:00Z"
}
```

**Response 200:**
```json
{
  "shiftId": "...",
  "shiftEnd": "2026-06-22T17:00:00Z",
  "tripsCompleted": 3
}
```

**Response 409:** Active trip in progress (must complete or release before clock-out)

---

## Presence & Telemetry

### POST /api/operator/presence/heartbeat

Periodic heartbeat (every 30 seconds when on shift)

**Request:**
```json
{
  "observedAt": "2026-06-22T12:15:00Z",
  "gpsCoord": { "lat": 14.5, "lng": 100.3 },
  "speedKmh": 45,
  "batteryPercent": 75,
  "networkType": "4G"
}
```

**Response 200:** `{ "ok": true }`

---

## Offline Sync

### POST /api/operator/sync/batch

Replay queued events from offline period (app stores locally, syncs when online)

**Request:**
```json
{
  "events": [
    {
      "clientEventId": "uuid-1",
      "type": "trip.acknowledge",
      "tripId": "...",
      "occurredAt": "2026-06-22T10:05:00Z",
      "payload": { /* same shape as direct endpoint */ }
    },
    {
      "clientEventId": "uuid-2",
      "type": "trip.pickup",
      "tripId": "...",
      "occurredAt": "2026-06-22T11:00:00Z",
      "payload": { ... }
    }
  ]
}
```

**Response 200:**
```json
{
  "results": [
    { "clientEventId": "uuid-1", "status": "applied" },
    { "clientEventId": "uuid-2", "status": "rejected", "reason": "GeofenceViolation" }
  ]
}
```

**Conflict resolution rules:**
- If trip is in terminal state (Completed/Failed/Cancelled) at server → reject events
- If event timestamp older than current state → reject (server-wins)
- Idempotency: re-sending same `clientEventId` returns previous result (no duplicate apply)

---

## Push Notification Payloads

These are pushed FROM server TO operator app (via FCM). Documented for mobile app handler implementation.

### TripAssigned

```json
{
  "type": "TripAssigned",
  "tripId": "...",
  "pickupWarehouse": "WH-BKK-01",
  "expectedAckBy": "2026-06-22T10:15:00Z"
}
```

### TripPausedByDispatcher

```json
{
  "type": "TripPaused",
  "tripId": "...",
  "reason": "Dispatcher requested pause"
}
```

### TripCancelled

```json
{
  "type": "TripCancelled",
  "tripId": "...",
  "reason": "Order cancelled by customer"
}
```

### SlaReminder

```json
{
  "type": "SlaReminder",
  "tripId": "...",
  "stage": "Pickup",
  "expectedBy": "2026-06-22T11:30:00Z",
  "minutesRemaining": 15
}
```

---

## Error Response Format (Standard)

All non-2xx responses follow:

```json
{
  "error": "ErrorCode",
  "message": "Human-readable description",
  "details": { /* optional additional context */ },
  "traceId": "00-abc123-def456-01"
}
```

**Common error codes:**
- `Unauthorized` (401)
- `Forbidden` (403) — wrong operator for this trip
- `NotFound` (404)
- `Conflict` (409) — state machine violation
- `GeofenceViolation` (400)
- `RateLimited` (429)
- `InternalError` (500)

---

## Rate Limits

| Endpoint | Limit |
|---|---|
| `/auth/login` | 5 / minute / device |
| `/trips/assigned` | 60 / minute |
| `/presence/heartbeat` | 4 / minute (every 15s minimum) |
| All others | 30 / minute / operator |

Rate-limited responses include `Retry-After` header

---

## Versioning

API path versioning: `/api/v1/operator/*` (v1 = initial)

Breaking changes will require new version path. Deprecated endpoints serve for 90 days minimum after deprecation notice.

---

## Storage Notes (Server-side)

- `photoUrl` and `signatureUrl` in requests are **already-uploaded** URLs — app uploads to `POST /api/operator/uploads/photo` first (returns URL), then references in POD calls
- Photos compressed to 1024px max width, < 500KB
- Signatures: PNG, 800x300, transparent background
- Storage: S3-compatible (MinIO local, S3 production) under `pod/{tripId}/{type}/{guid}.{ext}`

---

## Testing

**Sandbox base URL**: `https://sandbox.dtms.com/api/operator`

**Test credentials**:
- EmployeeCode: `TEST-001`, Password: see secure note

**Mock geofences**: All sandbox warehouses have 1000m geofence radius for easier testing

**Test endpoint**: `POST /api/operator/test/reset-trip/{tripId}` — resets trip back to Created state (sandbox only)

---

## Mobile App Implementation Hints

1. **JWT refresh** — refresh token 5 min before expiry; never wait for 401
2. **Offline queue** — store events in SQLite/Realm; flush on connectivity restored
3. **GPS polling** — only during active trip + on shift; pause when app backgrounded > 30 min
4. **Photo capture** — compress before upload; show progress; retry on failure
5. **Push notification handling** — wake app for TripAssigned; show in-app toast for others
6. **Battery awareness** — reduce heartbeat frequency below 20% battery (4/min → 1/min)
7. **Maps integration** — open native maps app for navigation (don't reimplement)

---

## Related Documents

- [Phase 4 Implementation Plan](../phases/phase-4-transport-manual.md)
- [Architecture Diagrams — Manual Flow](../diagrams/architecture.md#3-manual-dispatch-flow-new-phase-4)
- [ADR-003: Trip Extension Tables](../adr/adr-003-trip-extension-tables.md)
