# DTMS — SignalR Hub Catalog

**Status:** P0 foundation — every realtime push in DTMS goes through one of
the five hubs documented here.

This catalog is the **operator-facing reference** for what each hub does,
which client methods exist, which groups subscribers join, and which
projectors push to each. See also [event-projection-implementation-plan.md](event-projection-implementation-plan.md)
for the strategic plan and [projection-conventions.md](projection-conventions.md)
for the projector-side rules.

---

## 1. Overview

| Aspect | Choice | Rationale |
|---|---|---|
| Library | **SignalR (ASP.NET Core)** | First-class .NET integration, auto-fallback transports, free RPC abstraction |
| Wire protocol | **MessagePack + LZ4** | 30-60% smaller payload, 3-5× faster parse than JSON |
| Preferred transport | **WebSocket (forced)** | `skipNegotiation: true` — saves ~50ms first-byte |
| Fallback transports | SSE → LongPolling | Auto-handled by SignalR for proxies that block WS |
| Backplane (multi-instance) | **Redis (env-gated)** | DTMS already runs Redis; flip `SignalR__UseRedisBackplane=true` |
| Auth | JWT in `?access_token=` query | WebSocket upgrades can't carry Authorization header |
| CORS | Allowlist via `Cors__HubsAllowedOrigins` | Frontend on `:3000` ↔ API on `:5219` |

**Endpoint base:** `http://localhost:5219/hubs/*` (dev) /
`https://api.dtms.example.com/hubs/*` (prod)

---

## 2. Hub directory

| Hub | Endpoint | Group convention | Push frequency |
|---|---|---|---|
| OrderHub | `/hubs/orders` | `order:{id:N}` | Event-driven (status change) |
| JobHub | `/hubs/jobs` | `job-queue` + `job:{id:N}` | Event-driven |
| TripHub | `/hubs/trips` | `trip:{id:N}` | Event-driven |
| DashboardHub | `/hubs/dashboard` | `dashboard:{boardKey}` | Batched (250 ms window) |
| FleetHub | `/hubs/fleet` | `floor:{facilityId:N}` | Throttled (1 s window, latest-wins) |

---

## 3. OrderHub — `/hubs/orders`

**Source:** [src/DTMS.Api/Realtime/Hubs/OrderHub.cs](../src/DTMS.Api/Realtime/Hubs/OrderHub.cs)

### Client → server methods

| Method | Args | Effect |
|---|---|---|
| `Subscribe` | `Guid orderId` | Join `order:{id:N}` group |
| `Unsubscribe` | `Guid orderId` | Leave group |

### Server → client callbacks (`IOrderClient`)

| Callback | Payload | Pushed by | When |
|---|---|---|---|
| `TimelineUpdated` | `StatusTimelineEntryDto` (P1) | `OrderStatusHistoryProjector` | New row in `order_status_history` |
| `StatusChanged` | `{ orderId, fromStatus, toStatus, occurredAt }` | Same projector | After successful row insert |
| `ActivityUpdated` | `OrderActivityEntryDto` (P2, shape mirrors `FullAuditEntryDto`) | `OrderActivityProjector` | New unified-timeline row (status + amendment + trip + POD); `Id = EventId` (deterministic for dedup with REST refresh) |

### Frontend usage

```typescript
import { useOrderHubSubscription } from "@/lib/realtime/hubs/order-hub";

const { connected } = useOrderHubSubscription(orderId, {
  TimelineUpdated: (entry) => setEntries((prev) => [entry, ...prev]),
  StatusChanged: (change) => setStatus(change.toStatus),
});
```

---

## 4. JobHub — `/hubs/jobs`

**Source:** [JobHub.cs](../src/DTMS.Api/Realtime/Hubs/JobHub.cs)

Two subscription flavours so the **Jobs queue page** (broad) and a
**single Job drawer** (focused) can coexist without paying for each
other's traffic.

### Client → server methods

| Method | Args | Group |
|---|---|---|
| `SubscribeQueue` | – | `job-queue` |
| `UnsubscribeQueue` | – | – |
| `SubscribeJob` | `Guid jobId` | `job:{id:N}` |
| `UnsubscribeJob` | `Guid jobId` | – |

### Server → client callbacks (`IJobClient`)

| Callback | Payload | Group pushed to | When |
|---|---|---|---|
| `TimelineUpdated` | `JobTimelineEntryDto` (P1) | `job:{id:N}` | New job_status_history row |
| `JobUpdated` | `JobDto` | `job-queue` | Job's status changed |
| `JobAdded` | `JobDto` | `job-queue` | New retriable Failed job |
| `JobRemoved` | `Guid jobId` | `job-queue` | Job left the queue |

---

## 5. TripHub — `/hubs/trips`

**Source:** [TripHub.cs](../src/DTMS.Api/Realtime/Hubs/TripHub.cs)

| Method | Args | Group |
|---|---|---|
| `Subscribe` | `Guid tripId` | `trip:{id:N}` |
| `Unsubscribe` | `Guid tripId` | – |

| Callback | Payload | When |
|---|---|---|
| `TimelineUpdated` | `TripTimelineEntryDto` (P1) | New trip_status_history row |
| `StatusChanged` | `{ tripId, fromStatus, toStatus }` | Same projector |
| `MissionUpdated` | `MissionEventDto` | Mission progress within InProgress trip |

---

## 6. DashboardHub — `/hubs/dashboard`

**Source:** [DashboardHub.cs](../src/DTMS.Api/Realtime/Hubs/DashboardHub.cs)

Updates flow through [DashboardCounterBatcher](../src/DTMS.Api/Realtime/Pipeline/DashboardCounterBatcher.cs):
the projector calls `.Enqueue(boardKey, delta)` and the batcher drains
every **250 ms**, fanning out one `CountersUpdated` call per board.

### Client → server methods

| Method | Args | Notes |
|---|---|---|
| `Subscribe` | `string boardKey` | One of `"orders"`, `"fleet"`, `"funnel"`, `"sla"` |
| `Unsubscribe` | `string boardKey` | – |

### Server → client callbacks (`IDashboardClient`)

| Callback | Payload | When |
|---|---|---|
| `CountersUpdated` | `IReadOnlyList<object>` | 250 ms drain tick. Phase P3 — `OrderFunnelProjector` enqueues `{ kind: "order-funnel.bucket-touched", bucketHourUtc }` hints via `IDashboardRealtimePublisher`. Frontend treats payloads as refetch triggers (debounced 500 ms), not deltas to merge — avoids chart-vs-projection drift. |
| `KpiSnapshotUpdated` | `KpiSnapshotDto` | Initial load + reconnect |

### Why batching is mandatory here

During a stress window (100+ status transitions per second) a naïve
push would re-render charts dozens of times per frame. The 250 ms drain
bounds re-render rate to **~4 Hz**, well within human perception, while
still feeling realtime.

---

## 7. FleetHub — `/hubs/fleet`

**Source:** [FleetHub.cs](../src/DTMS.Api/Realtime/Hubs/FleetHub.cs)

Updates flow through [FleetPositionThrottler](../src/DTMS.Api/Realtime/Pipeline/FleetPositionThrottler.cs):
latest-wins per `(floor, robot)` pair within the **1-second** window so a
robot reporting at 10 Hz doesn't translate into 10 hub pushes.

### Client → server methods

| Method | Args | Group |
|---|---|---|
| `SubscribeFloor` | `Guid facilityId` | `floor:{facilityId:N}` |
| `UnsubscribeFloor` | `Guid facilityId` | – |

### Server → client callbacks (`IFleetClient`)

| Callback | Payload | When |
|---|---|---|
| `RobotPositionsUpdated` | `IReadOnlyList<RobotPosition>` | 1 s flush, latest position per robot |
| `RobotStateChanged` | `(Guid robotId, string state)` | Individual lifecycle change, not batched |

---

## 8. Hub pipeline (every method invocation goes through)

```
Browser hub.invoke()
    │
    ▼ TracingHubFilter  ← OpenTelemetry span + duration histogram + connect counter
    │
    ▼ RateLimitedHubFilter  ← TokenBucket per ConnectionId
    │                          (100 burst / 20 sustained per second)
    │                          throws HubException on exhaustion → close UI's invoke()
    ▼
Hub.MethodBody  (Subscribe/Unsubscribe only — no DB calls)
```

| Filter | Source | Purpose |
|---|---|---|
| `TracingHubFilter` | [TracingHubFilter.cs](../src/DTMS.Api/Realtime/Filters/TracingHubFilter.cs) | Activity span, `dtms.signalr.hub.*` metrics |
| `RateLimitedHubFilter` | [RateLimitedHubFilter.cs](../src/DTMS.Api/Realtime/Filters/RateLimitedHubFilter.cs) | Per-connection token bucket; cleanup on disconnect |

---

## 9. Metrics (OpenTelemetry, meter `DTMS.SignalR`)

| Metric | Type | Tags |
|---|---|---|
| `dtms.signalr.hub.method.invocations_total` | Counter | hub, method |
| `dtms.signalr.hub.method.duration_ms` | Histogram | hub, method |
| `dtms.signalr.hub.connections_total` | Counter | hub |
| `dtms.signalr.hub.rate_limited_total` | Counter | hub, method |

Wired in [Program.cs](../src/DTMS.Api/Program.cs) via
`.AddMeter("DTMS.SignalR")` on the OTel `WithMetrics` builder.

---

## 10. Frontend connection model

**Source:** [signalr-client.ts](../frontend/lib/realtime/signalr-client.ts)

- One `HubConnection` per hub path, shared across components (`Map<string, HubConnectionEntry>`).
- Lazy: connection opens on first subscriber, stays alive until page unloads.
- Forced `WebSockets` + `skipNegotiation` + MessagePack protocol.
- Auto-reconnect: exponential backoff 1s → 2s → 4s → … capped at 30s, ±500 ms jitter.
- Cookie credentials via `withCredentials: true` (CORS allows it).

### `useHubSubscription` lifecycle

```
mount
  ├─ ensureStarted(hubPath)
  ├─ register event handlers via connection.on(name, fn)
  ├─ invoke(subscribeMethod, ...args)
  └─ wire onreconnected / onreconnecting / onclose
on reconnect
  └─ re-invoke(subscribeMethod, ...args)  ← server-side group membership is per-connection
unmount
  ├─ if Connected: best-effort invoke(unsubscribeMethod, ...args)
  └─ connection.off(name, fn) for each registered handler
```

---

## 11. Scaling stages

| Stage | Setup | Capacity (est.) |
|---|---|---|
| **Stage 1 — current** | Single API instance, in-memory backplane | ~1 000 concurrent connections |
| **Stage 2 — light HA** | 2-3 instances + Redis backplane (`SignalR__UseRedisBackplane=true`) | ~10 000 concurrent |
| **Stage 3 — heavy** | Tune backplane channel prefix, watch Redis CPU | ~50 000 concurrent |
| **Stage 4 — massive** | Azure SignalR Service (managed) | 1 M+ concurrent |

Redis backplane gate is one env var; no code change needed to flip Stage 1 → Stage 2.

---

## 12. Adding a new hub callback (cheat sheet)

1. Add method to the typed client interface (e.g. `IOrderClient`)
2. Add a typed wrapper in `frontend/lib/realtime/hubs/{name}-hub.ts`
   under the corresponding `*HubEvents` type
3. Inject `IHubContext<TheHub, IItsClient>` into the projector
4. Call `_hub.Clients.Group(TheHub.GroupKey(id)).YourCallback(payload)`
   after the read-model write succeeds (fire-and-forget OK; the
   projection is durable regardless of push success)
5. Subscribe from the React component via the hub wrapper

**Do not:**
- Push directly from a controller / command handler — projectors own that responsibility
- Touch the DB inside a hub method — keep them subscription-only
- Use `Clients.All` outside of an admin broadcast use case — always Group
