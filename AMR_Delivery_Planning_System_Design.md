# AMR Delivery Planning System — System Design

**Version**: 1.0 | **Scope**: End-to-end AMR delivery orchestration supporting all logistics patterns (Point-to-Point, Multi-Stop, Consolidation, Multi-Pick Multi-Drop, Milk Run, Cross Dock) with a vendor-agnostic Adapter Layer (ACL) integrating RIOT3.0 and other AMR control systems.

---

## 1. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         External Integrations                            │
│   WMS / ERP / MES / TMS / OMS / Operator UI / Mobile / BI              │
└───────────────────────────┬─────────────────────────────────────────────┘
                            │ REST / GraphQL / Webhook / Event
┌───────────────────────────▼─────────────────────────────────────────────┐
│                         API Gateway (AuthN / AuthZ / Rate Limit)        │
└──────┬─────────────┬─────────────┬─────────────┬─────────────┬──────────┘
       │             │             │             │             │
┌──────▼─────┐ ┌─────▼──────┐ ┌───▼─────────┐ ┌─▼──────────┐ ┌▼──────────┐
│ 1. Delivery│ │ 2. Planning│ │ 3. Dispatch │ │ 4. Fleet & │ │5. Facility│
│    Order   │ │     &      │ │     &       │ │   Asset    │ │     &     │
│   Mgmt     │ │Optimization│ │  Execution  │ │   Mgmt     │ │ Topology  │
└──────┬─────┘ └─────┬──────┘ └──────┬──────┘ └─────┬──────┘ └─────┬─────┘
       │             │               │              │              │
       └─────────────┴───────┬───────┴──────────────┴──────────────┘
                             │ Event Bus (Kafka / NATS)
                    ┌────────▼──────────┐
                    │  6. Vendor        │
                    │  Adapter (ACL)    │
                    └────────┬──────────┘
                             │
             ┌───────────────┼────────────────┐
             ▼               ▼                ▼
        ┌─────────┐   ┌──────────┐      ┌──────────┐
        │ RIOT3.0 │   │ SEER     │      │ Other    │
        │ (HTTP)  │   │ RoboShop │      │ Vendors  │
        └─────────┘   └──────────┘      └──────────┘
```

**Cross-cutting concerns** (shared across all contexts): Identity, Tenant, Audit, Observability (OpenTelemetry), Configuration, Secret management, Feature flags, Event bus, Workflow engine (Temporal/Camunda), Rule engine.

---

## 2. Domain Model — Ubiquitous Language

Before diving into contexts, the core glossary to anchor naming:

- **DeliveryOrder** — the business-level delivery request from an upstream system (WMS/ERP). It expresses intent, not execution.
- **Job** — the operational unit produced by Planning. One DeliveryOrder becomes one or more Jobs depending on pattern.
- **Leg** — a segment of a Job (e.g., pickup-to-dropoff). One Job has one or more Legs.
- **Stop** — a physical visit at a Station (pick, drop, park, charge, wait).
- **Task** — the lowest-level, vendor-specific directive sent to an AMR (MOVE / ACT). Maps to RIOT `mission`.
- **Trip** — an AMR's actual execution of a Job on the floor (with real timestamps, routes, events).
- **Load Unit (LU)** — the thing being moved: carton, pallet, tote, rack, shelf.
- **Carrier** — physical AMR (Lift-up, Feeder, Forklift, Tugger).
- **Zone / Area** — logical region of the facility with rules (speed, priority, exclusive access).
- **Resource** — any constrained entity: lift, conveyor, door, charger, traffic area, stopper.

**Pattern mapping** (how patterns express themselves in this model):

| Pattern | DeliveryOrder | Jobs | Legs per Job |
|---|---|---|---|
| Point-to-Point | 1 | 1 | 1 pick + 1 drop |
| Multi-Stop (one vehicle, many drops) | 1 | 1 | 1 pick + N drops |
| Multi-Pick Multi-Drop | 1 | 1 | N picks + M drops (interleaved) |
| Consolidation | N | 1 merged | N picks + 1 drop |
| Milk Run | 1 (scheduled) | 1 cyclic | N stops in loop |
| Cross Dock | 2 (inbound + outbound) | 1 linked | pick-drop-pick-drop bridge |

---

## 3. Bounded Contexts

### Context 1 — Delivery Order Management

**Purpose**: Own the lifecycle of business-level delivery intent. Validate, enrich, persist, and emit events for downstream planning. This context does NOT know about AMRs.

**Responsibilities**
- Accept orders from upstream (WMS/ERP/TMS/MES) via REST, bulk upload, EDI, scheduled imports, or manual UI.
- Validate business rules (SLA, cutoff, service window, load compatibility, origin/destination existence).
- Enrich with master data (customer, SLA tier, load characteristics, handling instructions).
- Manage order state machine and emit domain events.
- Expose query APIs: by upperKey, by state, by SLA risk, by date range, by tag.
- Support order amendments (split, merge, reschedule, cancel, hold) with audit trail.
- Support order templates & recurring orders (for Milk Run schedules).

**Core entities**
- `DeliveryOrder` (aggregate root): upperKey, orderName, orderType, priority, serviceWindow{earliest, latest}, slaTier, tags, structureType (sequence|tree|parallel), state, createdBy, tenantId.
- `OrderLine`: sku, loadUnitType, quantity, weight, dims, hazmatClass, temperatureRange, handlingInstructions.
- `PickupInstruction`: originFacilityId, originZoneId, stationHint, earliestPick, latestPick.
- `DropInstruction`: destFacilityId, destZoneId, stationHint, earliestDrop, latestDrop, deliveryProof (signature, scan, photo).
- `OrderTemplate`: for recurring orders (cron, calendar, load pattern).
- `OrderAmendment`: type, reason, originalSnapshot, newSnapshot, by, at.

**State machine**
```
DRAFT → SUBMITTED → VALIDATED → READY_TO_PLAN → PLANNING → PLANNED
     → DISPATCHED → IN_PROGRESS → COMPLETED
(at any live state): HELD, CANCELLED, FAILED, AMENDED
```

**Primary APIs** (opinionated; aligned loosely to REST)
- `POST /api/v1/orders` — create (supports idempotency-key header)
- `POST /api/v1/orders:bulk` — up to N orders in one transaction
- `PATCH /api/v1/orders/{id}` — amendment (partial update)
- `POST /api/v1/orders/{id}:hold` / `:release` / `:cancel` / `:split` / `:merge`
- `GET /api/v1/orders?state=&sla=&from=&to=&page=&size=`
- `GET /api/v1/orders/{id}`
- `POST /api/v1/order-templates` (recurring)
- `GET /api/v1/orders/{id}/timeline` — full audit trail

**Emitted events**
- `delivery-order.submitted.v1`
- `delivery-order.validated.v1`
- `delivery-order.amended.v1`
- `delivery-order.cancelled.v1`
- `delivery-order.recurring-instance-generated.v1`

**Key design decisions**
- **Idempotency via upperKey + idempotency-key header**: prevents duplicate orders when upstream retries.
- **Append-only amendment history**: no in-place mutation of submitted orders; every change is an amendment with reason code.
- **Validation is tiered**: syntactic (schema) → semantic (facility/station exists, SLA feasible) → business (credit, hazmat compatibility). Only syntactic failures reject at ingestion; semantic issues can put the order into `VALIDATION_HOLD` with a reviewer task.
- **SLA clock starts at SUBMITTED**, not at dispatch — so Planning can compute SLA risk during optimization.

---

### Context 2 — Planning & Optimization

**Purpose**: Transform business DeliveryOrders into executable Jobs, producing feasible, cost-optimal assignments across AMRs, routes, time windows, and resources. This context is the **brain** and is where all six logistics patterns become concrete plans.

**Responsibilities**
- Pattern classifier: infer or apply the pattern (Point-to-Point, Multi-Stop, Consolidation, Milk Run, Multi-Pick Multi-Drop, Cross Dock) from order attributes and configuration.
- Consolidation engine: group compatible orders (same destination window, compatible loads, same carrier type).
- Routing/assignment: vehicle-to-job assignment + route sequencing. Solver: rolling-horizon heuristic (default) + OR-tools CVRPTW for batch/milk-run.
- Time-window feasibility (service windows, cutoffs, SLA).
- Resource reservation: lifts, chargers, traffic areas, cross-dock docks.
- Replanning: triggered by disruptions (AMR offline, path blocked, order amendment, priority escalation).
- What-if simulation API (for planners to evaluate scenarios before committing).
- Cost model: travel distance, travel time, idle time, battery burn, SLA penalty, human labour, lift/resource contention.

**Core entities**
- `Job`: jobKey, derivedFromOrders[], pattern, carrierTypeRequired, plannedStartTs, plannedEndTs, legs[], assignedCarrier (nullable until dispatch), state.
- `Leg`: legKey, sequence, fromStop, toStop, actions[], loadManifest[], plannedDurationMs, plannedDistanceMm.
- `Stop`: stationId, stopType (PICK/DROP/CHARGE/PARK/WAIT), plannedArrival, plannedDeparture, dwellMs, requiredActions[].
- `PlanningConstraint`: vehicleCompatibility, zoneAccess, timeWindow, loadCapacity, concurrency limit on resource.
- `PlanSnapshot`: immutable plan version with cost breakdown, KPIs, feasibility flags.
- `Reservation`: resourceId, windowStart, windowEnd, heldForJobKey, state.

**Pattern implementation notes**

| Pattern | Engine behavior |
|---|---|
| **Point-to-Point** | Trivial: 1 job = pick→drop. Assignment by nearest available compatible AMR; cost = travel + dwell. |
| **Multi-Stop** | Given 1 pick, N drops (or vice versa): solve TSP/CVRP on the drops under capacity & time window. |
| **Consolidation** | Group orders by (destZone, deliveryWindow overlap, compatible load). Solver looks back over rolling window (e.g., 15 min) and commits on either fill-threshold or latest-pickup-deadline. |
| **Multi-Pick Multi-Drop** | CVRPPD (pickup-delivery pairs) with precedence (pick(i) before drop(i)) and capacity constraints. |
| **Milk Run** | Scheduled template. Plan = fixed route + time windows; engine only assigns vehicle and monitors drift. Missed stops promote to exception handling. |
| **Cross Dock** | Two linked legs: inbound order `IN-001` drops at dock `D-12`; outbound order `OUT-002` picks from `D-12`. Planner synchronizes: outbound pick window must start after inbound drop + handling dwell. Expressed as a Job-dependency graph. |

**Primary APIs**
- `POST /api/v1/plans:generate` — generate plan for a set of order keys (optionally with constraints override).
- `POST /api/v1/plans:simulate` — what-if; returns KPI delta without committing.
- `POST /api/v1/plans/{id}:commit` — lock plan; triggers dispatch eligibility.
- `POST /api/v1/plans/{id}:replan` — with reason code (DISRUPTION, PRIORITY_CHANGE, AMENDMENT).
- `GET /api/v1/plans/{id}/kpis` — makespan, avg wait, SLA-at-risk count, vehicle utilization, battery burn.
- `POST /api/v1/milk-run-schedules` — register recurring plan template.

**Emitted events**
- `plan.generated.v1` / `plan.committed.v1` / `plan.rejected.v1`
- `job.created.v1` / `job.assigned.v1` / `job.replanned.v1`
- `reservation.created.v1` / `reservation.released.v1`

**Key design decisions**
- **Two-layer solver**: fast heuristic (greedy insertion) for sub-second response on single-order dispatch; full MILP/CVRP solver for batch and milk-run on a rolling horizon. Same API, flag `solverMode: realtime|batch`.
- **Soft vs hard constraints**: time windows and capacity are hard; minimizing idle time and balancing load are soft (weighted objective).
- **Plan is immutable once committed**; disruptions produce a new PlanSnapshot linked to the prior version.
- **Replanning trigger policy**: configurable per tenant (e.g., replan if SLA risk > X%, or if AMR offline > Y seconds).
- **Explainability**: every assignment carries a `planningTrace` (why this AMR, which constraints bound the solution). Critical for operator trust.

---

### Context 3 — Dispatch & Execution

**Purpose**: Take committed Jobs from Planning, translate them into vendor-specific Tasks, execute, monitor, and close the loop. Owns the live operational state.

**Responsibilities**
- Job→Task translation using the carrier's action catalog (see Vendor Adapter).
- Issue orders to the AMR fleet via the vendor adapter (RIOT3.0 `POST /api/v4/orders` for SEER AMRs).
- Execution monitor: subscribes to vendor callbacks (e.g., RIOT3.0 `/api/v4/notify` → taskEventType / vehicleEventType) and normalizes them.
- Exception handling: blocked path, emergency stop, battery critical, action failure, traffic contention.
- Live job operations: pause, resume, skip, cancel, retarget, takeover (manual).
- Resource coordination: requests traffic area / door / lift access in the right order (TCM bridges to `/api/v4/traffic/outside/operation`, `/api/v4/device/{deviceType}/command`).
- Proof-of-delivery capture (scan, photo, signature) and dwell verification.
- Publish execution telemetry to the event bus.

**Core entities**
- `Trip`: tripKey, jobKey, vehicleKey, state, actualStartTs, currentLegIndex, currentStopIndex, eventLog[].
- `Task`: taskKey, tripKey, sequence, vendorCommand (e.g., `{type:"MOVE", mapId, stationId}` or `{type:"ACT", actionType:"4", parameters:[{key:"1", value:"0"}]}`), blockingType, state, startedTime, finishedTime, resultCode.
- `ExecutionEvent`: tripKey, eventType, source (vendor|internal|operator), payload, receivedAt.
- `Exception`: tripKey, code, severity, detail, resolution, resolvedBy, resolvedAt.
- `ProofOfDelivery`: tripKey, stopKey, artifacts[] (photo URL, scanned IDs, signature blob), capturedAt.

**Job→Task translation pattern** (for a Lift-up-type AMR pick-drop job using RIOT3.0):
```
Leg 1: move to pickup station
  Task 1 → MOVE (category=agv, mapId, stationId=pickupStation)

Leg 1: pick (lift shelf via QR alignment)
  Task 2 → ACT (actionType=4, param0=1, param1=0)   // Align and Lift Shelf, max height
           [from Image 2: Lift-up type Action command table]

Leg 2: move to drop station
  Task 3 → MOVE (category=agv, mapId, stationId=dropStation)

Leg 2: drop (lower shelf)
  Task 4 → ACT (actionType=4, param0=2, param1=0)   // Lower Shelf and Return to Zero
```

**Job→Task translation pattern** (Feeder-type AMR side loading/unloading using the legacy program table in Image 1):
```
Leg 1: move to pickup station
  Task 1 → MOVE to pickup station

Leg 1: Left Side Loading
  Task 2 → ACT (program1=192, program2=1, program3=3)

Leg 2: move to drop station
  Task 3 → MOVE to drop station

Leg 2: Left Side Unloading
  Task 4 → ACT (program1=192, program2=1, program3=4)
```

**Primary APIs**
- `POST /api/v1/trips:start` — begin execution of a committed job
- `POST /api/v1/trips/{id}:pause` / `:resume` / `:cancel` / `:skip-current` / `:retry-current`
- `POST /api/v1/trips/{id}:reassign` (operator takeover, force new vehicle)
- `GET /api/v1/trips/{id}` — live state
- `GET /api/v1/trips/{id}/events?since=` — tailable event log
- `POST /api/v1/trips/{id}/pod` — attach proof of delivery
- `POST /api/v1/exceptions/{id}:resolve` — close exception with resolution code

**Emitted events**
- `trip.started.v1` / `trip.leg-completed.v1` / `trip.completed.v1` / `trip.failed.v1`
- `task.dispatched.v1` / `task.completed.v1` / `task.failed.v1`
- `exception.raised.v1` / `exception.resolved.v1`
- `pod.captured.v1`

**Key design decisions**
- **State authority**: Dispatch is the source of truth for live state. Vendor AMR state is a reported signal, not authoritative — the normalized Trip state is derived and persisted here.
- **Idempotent vendor calls**: every dispatch carries a client-generated `upperKey` (RIOT3.0 `upperKey`) so re-sends are safe.
- **Command vs query separation**: command endpoints (pause/resume) are asynchronous and return a commandKey; status polled or event-pushed. Avoids blocking UI on slow vendor responses.
- **Graceful disruption handling**: a blocked path doesn't cancel the job — it first tries replan-in-place (ask Planning); only if no alternative is found does it fail up.
- **Blocking types honored**: map Planning's semantics to RIOT3.0 `blockingType` (NONE/SOFT/HARD) correctly so intra-job sequencing matches intent.

---

### Context 4 — Fleet & Asset Management

**Purpose**: Manage the AMR fleet as strategic assets — their identity, capability, health, utilization, charging, maintenance, and commissioning/decommissioning.

**Responsibilities**
- Fleet registry: each AMR's identity, type, capabilities, software version, serial number, warranty.
- Capability catalog: what each carrier type can do (actions, max payload, dims, speed, floor rating, attachments).
- Real-time state: position, battery, health, current trip, online/offline, emergency stop state.
- Charging strategy: opportunistic vs scheduled; low-battery auto-park to charger; charge-to-threshold.
- Parking strategy: assign parking stations when idle; load-balance across zones.
- Group management: vehicle groups (for pool-based dispatch, e.g., "zone-A-liftups").
- Maintenance: scheduled (by hours/km) and corrective (from alarm); maintenance windows block dispatch.
- Utilization & health analytics: MTBF, availability %, energy per job, wear on critical components.
- Firmware / software upgrade orchestration.

**Core entities**
- `Vehicle`: deviceKey, deviceName, typeKey, manufacturer, serialNumber, version, groupIds[], commissionedAt, state (ACTIVE/MAINTENANCE/RETIRED).
- `VehicleType`: typeKey, category (agv|seer_agv|…), capabilities[] (MOVE, LIFT, ROTATE, SIDE_LOAD, FRONT_PROBE…), maxPayloadKg, lengthMm, widthMm, heightMm, supportedActions[] (ref to action catalog).
- `VehicleState`: position (mapId, x, y, theta), batteryPct, systemState (IDLE/BUSY/ERROR), safetyState, connectionState, loadStatus, currentTripKey.
- `ChargingPolicy`: tenantId, vehicleTypeKey, lowThresholdPct, targetThresholdPct, mode (OPPORTUNISTIC|SCHEDULED|RESERVED).
- `MaintenanceRecord`: vehicleKey, type (SCHEDULED|CORRECTIVE|UPGRADE), scheduledAt, performedAt, completedAt, parts[], technician, outcome.
- `VehicleGroup`: groupId, name, membership criteria (static list or tag-based), dispatch policy.

**Primary APIs**
- `GET /api/v1/vehicles?state=&group=&zone=`
- `GET /api/v1/vehicles/{id}` — full state including current trip
- `POST /api/v1/vehicles:operation` — bulk op (enable/disable scheduling, emergency stop, park, charge, play light)
- `POST /api/v1/vehicles/{id}/maintenance` — create maintenance record; blocks dispatch
- `GET /api/v1/vehicle-types/{id}/capabilities` — capability catalog
- `POST /api/v1/charging-policies`
- `GET /api/v1/fleet/kpi?from=&to=` — availability, utilization, energy, MTBF

**Emitted events**
- `vehicle.state-changed.v1` (throttled/debounced)
- `vehicle.battery-low.v1` / `vehicle.emergency-triggered.v1`
- `vehicle.maintenance-entered.v1` / `vehicle.maintenance-exited.v1`
- `vehicle.commissioned.v1` / `vehicle.retired.v1`

**Key design decisions**
- **State is eventually consistent**, pulled from vendor adapter with a configured polling/stream strategy. A 1–2s staleness is acceptable for dashboards; Planning uses best-effort with a staleness budget.
- **Capability-based assignment**: Planning matches Job's `carrierTypeRequired` against `VehicleType.capabilities[]` rather than hard-coding vendor names. Adding a new AMR model means registering its capabilities, not changing Planning code.
- **Charging is a first-class Job type**, not a side effect — it competes for the vehicle on the same planner queue so the planner can anticipate it (similar to RIOT3.0 `CHARGE` orderType).
- **Group membership is dynamic**: tag-based groups recalculated on vehicle state change, so "ready-to-dispatch-in-zone-A" reflects reality.

---

### Context 5 — Facility & Topology

**Purpose**: Model the physical world the AMR operates in — maps, stations, zones, routes, points of interest, and integration with facility infrastructure (doors, lifts, conveyors, chargers).

**Responsibilities**
- Map registry: multi-map, multi-floor, multi-facility.
- Station master data: pick points, drop points, charging stations, parking stations, checkpoint stations, cross-dock docks.
- Zone modeling: traffic zones, exclusive-access zones, slow zones, restricted zones, capacity-limited zones.
- Route graph: edges between stations with cost, allowed carrier types, direction.
- Facility integrations: doors, air-shower doors, elevators, chargers, conveyors, stoppers (matching RIOT3.0 `deviceType` enum: AGV, DOOR, AIR_SHOWER_DOOR, ELEVATOR, CHARGER, COMMON).
- Route cost queries on demand (proxy to `/api/v4/route/costs/{mapId}/{stationId}`).
- Map synchronization: pull or push map files from/to AMR vendors.
- Editable topology overlays (operational ones, like temporary blockage) without mutating the base map.

**Core entities**
- `Facility`: facilityId, name, timezone, address.
- `Map`: mapId, facilityId, floor, version, checksum, fileUri, state (activated/inactivated), syncState (synced/partition/asynced).
- `Station`: stationId, mapId, stationType (PICK/DROP/CHARGE/PARK/DOCK/CHECKPOINT), pose{x,y,yaw}, entryPose, exitPose, qrCode, compatibleVehicleTypes[], zoneId.
- `Zone`: zoneId, mapId, polygon, zoneType (TRAFFIC/EXCLUSIVE/SLOW/RESTRICTED), rules.
- `RouteEdge`: fromStationId, toStationId, distanceMm, estTimeMs, allowedVehicleTypes[], bidirectional.
- `FacilityResource`: resourceKey, resourceType (DOOR/AIR_SHOWER/ELEVATOR/CHARGER/STOPPER/TRAFFIC_AREA), vendorRef, capacity, controlEndpoint.
- `TopologyOverlay`: overlayId, mapId, type (BLOCKAGE/TEMP_STATION/RESTRICTION), validFrom, validUntil, reason.

**Primary APIs**
- `GET /api/v1/facilities` / `/api/v1/maps` / `/api/v1/maps/{mapId}/stations`
- `GET /api/v1/route/costs?mapId=&stationId=&vehicleKeys=` (proxy + caching)
- `GET /api/v1/stations?type=&zone=&compatibleWith=`
- `POST /api/v1/zones` / `/api/v1/route-edges`
- `POST /api/v1/resources/{id}:command` (door open, air shower apply, elevator call)
- `POST /api/v1/topology-overlays` (e.g., operator marks aisle blocked for next 2 hours)

**Emitted events**
- `map.synced.v1` / `map.sync-failed.v1`
- `topology-overlay.activated.v1` / `topology-overlay.expired.v1`
- `resource.state-changed.v1` (door opened/closed, lift arrived)

**Key design decisions**
- **Map ownership is asymmetric**: AMR vendors own the navigable map (it's their format). Our Facility context owns the business overlay — zones, cross-dock docks, SLA-regulated areas — in a vendor-neutral model.
- **Station compatibility matrix**: `Station.compatibleVehicleTypes` is critical for heterogeneous fleets (a narrow liftup fits a station a wide forklift can't). Planning honors this hard.
- **Resource commands are idempotent and tokenized**: a DOOR.APPLY request holds a reservation token; released on timeout or explicit release, so we don't leave doors stuck open if a trip dies mid-way.
- **Route cost is cached with short TTL (5–15s)** per (vehicleKey, mapId, stationId) tuple — cheaper than re-querying the AMR every time Planning evaluates a candidate.

---

### Context 6 — Vendor Adapter (Anti-Corruption Layer, ACL)

**Purpose**: Isolate the rest of the system from vendor-specific APIs, terminology, and behaviors. Translate between the canonical domain model and each vendor's wire protocol. Absorb vendor quirks, retry logic, authentication, and version drift.

**Responsibilities**
- Outbound: translate canonical Tasks into vendor API calls.
- Inbound: normalize vendor callbacks/events into canonical domain events.
- Manage vendor auth (token refresh, certificate rotation).
- Rate limiting & backpressure per vendor.
- Retry policy with idempotency (client keys).
- Capability probing: discover what each connected vendor instance can do and expose as capability metadata for Planning & Fleet contexts.
- Action catalog mapping per vendor type.
- Fallback & degradation: if vendor API is down, queue commands and surface health to upstream.

**Subcomponents** (one adapter per vendor; same contract)
- `AdapterInterface` (canonical contract, implemented per vendor)
- `RIOT3Adapter` (SEER/STANDARD AMRs)
- `CustomAdapter` (e.g., for feeder-type AMRs using the program-number protocol from Image 1)
- `SimulatorAdapter` (for test environments)

**Canonical adapter contract** (sketch)
```
submitOrder(canonicalJob) → vendorOrderKey
cancelOrder(vendorOrderKey, reason)
pauseOrder / resumeOrder / skipCurrentTask(vendorOrderKey)
queryOrderState(vendorOrderKey) → canonical state
listVehicles() → Vehicle[]
getVehicle(deviceKey) → canonical Vehicle state
operateVehicle(deviceKeys[], operation, params) → per-device result
getRouteCosts(mapId, stationId, deviceKeys) → cost map
getMaps() / getStations(mapId)
commandResource(resourceType, resourceRef, command) → result
registerCallbackUrl(url) → registration
onVendorCallback(rawPayload) → emitted canonical event
```

**RIOT3.0 mapping** (from the interface doc)

| Canonical op | RIOT3.0 endpoint |
|---|---|
| submitOrder | `POST /api/v4/orders` with structureType, missions, priority, appointVehicleKey/Group |
| cancel/priority/hold/resume | `PUT /api/v4/orders/{orderkey}/operation` with `orderCommandType` |
| queryOrderState | `GET /api/v4/orders/{key}` (use `isUpper=true` when using upperKey) |
| list orders | `GET /api/v4/orders?…` |
| getVehicle | `GET /api/v4/robots/{deviceKey}` |
| listVehicles | `GET /api/v4/robots?…` |
| operateVehicle (bulk) | `POST /api/v4/robots/operation` (operation = SCHEDULE_ENABLE / TRIGGER_EMERGENCY / PAUSE_TASK / CREATE_CHARGE_TASK …) |
| commandResource (door, lift) | `POST /api/v4/device/{deviceType}/command` (deviceType = DOOR / AIR_SHOWER_DOOR / ELEVATOR / CHARGER / COMMON) |
| traffic reservation | `POST /api/v4/traffic/outside/operation` (APPLY/RELEASE) |
| getMaps | `GET /api/v4/maps` |
| getStations | `GET /api/v4/map/file/{mapId}/stations` |
| route costs | `GET /api/v4/route/costs/{mapId}/{stationId}?deviceKey=…` |
| alarms | `GET /api/v4/message/alarms?…` |
| receive events | RIOT3.0 calls our `POST /api/v4/notify` (taskEventType, vehicleEventType) |
| action callback in a task | RIOT3.0 calls our `url` mid-action (with retryCount, backoffDelay, readTimeOutMillis) |

**Action catalog mapping examples**

Lift-up type (Image 2) canonicalized:
| Canonical action | Vendor actionType (RIOT3.0 `actionType`) | Parameters |
|---|---|---|
| LIFT_SHELF_ALIGNED (max height) | 4 | param0=1, param1=0 |
| LIFT_SHELF_ALIGNED (specified mm) | 4 | param0=1, param1=X |
| LOWER_SHELF_ZERO | 4 | param0=2, param1=0 |
| LIFT_PLATFORM (with overload detect) | 4 | param0=9, param1=X |
| ROTATE_PLATFORM | 4 | param0=12, param1=X (0.1°) |
| PLATFORM_INIT | 4 | param0=15, param1=0 |
| MANUAL_SHELF_CORRECTION | 4 | param0=21, param1=0 |

Feeder type (Image 1) canonicalized:
| Canonical action | program1 | program2 | program3 | Notes |
|---|---|---|---|---|
| INIT | 192 | 100 | 100 | initialization |
| LEFT_SIDE_LOAD | 192 | 1 | 3 | |
| LEFT_SIDE_UNLOAD | 192 | 1 | 4 | |
| RIGHT_SIDE_LOAD | 192 | 2 | 3 | |
| RIGHT_SIDE_UNLOAD | 192 | 2 | 4 | |
| FRONT_PROBE | 192 | 22 | W | -50 < H < 50 mm |
| LEFT_STOPPER_BLOCK | 192 | 201 | 2 | |
| RIGHT_STOPPER_BLOCK | 192 | 201 | 21 | |

**Event normalization**
RIOT3.0 `/api/v4/notify` payload → canonical events:
- `type=task` + `taskEventType=started/progress/finished/failed` → `trip.started / trip.leg-completed / trip.completed / trip.failed`
- `type=subTask` → `task.dispatched / task.completed / task.failed` (with failResult→Exception)
- `type=vehicle` + `vehicleEventType` → `vehicle.state-changed / vehicle.emergency-triggered / vehicle.battery-low`

**Key design decisions**
- **ACL is the *only* place that speaks vendor dialect**. Every other context sees canonical entities. This is non-negotiable for multi-vendor ops.
- **Symmetric translation tables**: action catalog mapping is data, not code — stored per `(tenant, vehicleType)` so a new AMR model is a config change, not a deploy.
- **Callback registration at bootstrap**: adapter registers its inbound URL with RIOT3.0's action-callback mechanism using the parameter shape from the interface doc (`url`, `retryCount`, `backoffDelay`, `readTimeOutMillis`).
- **Health check & circuit breaker**: per-vendor health probe every 30s; trip the circuit breaker on sustained failure and surface to Fleet Mgmt as `vendor.connection-degraded.v1`.
- **Shadow mode for new vendors**: new adapters can be deployed read-only first (listen to events, don't dispatch) for validation against live data before cutting over.

---

## 4. Cross-Context Workflows — End-to-End Examples

### Example A: Point-to-Point order with a Lift-up AMR

1. WMS → `POST /api/v1/orders` (DeliveryOrder Management): 1 line, station S10 → S42.
2. Validator passes → `delivery-order.validated.v1` → Planning picks it up.
3. Planning classifies as POINT_TO_POINT, solves assignment → picks lift-up AMR `AMR-07`, generates Job with 4 Tasks: MOVE→ACT(lift)→MOVE→ACT(drop). Reserves no shared resource. Commits plan.
4. `plan.committed.v1` → Dispatch issues Trip.
5. Dispatch → Vendor Adapter → RIOT3.0 `POST /api/v4/orders` with 4 missions, `appointVehicleKey=AMR-07`.
6. RIOT3.0 callbacks `/api/v4/notify` as tasks progress → Dispatch updates Trip → emits `trip.leg-completed.v1` events.
7. On final task finish → `trip.completed.v1` → Order transitions to COMPLETED.

### Example B: Cross-Dock between two orders

1. Inbound DeliveryOrder `IN-001` submitted for pallet arriving at inbound dock D1 at 10:00.
2. Outbound DeliveryOrder `OUT-002` submitted for same pallet to leave from outbound dock D5 by 11:30.
3. Planning detects cross-dock linkage (same `loadUnitId`, overlapping windows, compatible stations) — creates 1 cross-dock Job with 2 linked legs: `IN-001.drop@D1 → dwell (sort) → OUT-002.pick@D1 → drop@D5`.
4. Reservation made on staging slot in the sort area for 10:00–10:45.
5. Dispatch executes; the inbound AMR completes drop; Dispatch emits event; the outbound Job becomes eligible (dependency satisfied); planner assigns a (possibly different) AMR for outbound leg.
6. All state changes surface on a single "cross-dock Job" view for operator visibility.

### Example C: Milk Run (recurring)

1. Operator creates OrderTemplate: every weekday 08:00 and 14:00, visit stations [S1, S5, S9, S12] in order, drop consumables, pick empties.
2. Scheduler generates DeliveryOrder instances at cutoff time.
3. Planning uses the template's pinned sequence — no re-sequencing (milk runs are typically fixed route for SOP reasons).
4. Same dispatch/execution as other patterns; missed stops raise exceptions linked to the template for SLA reporting.

### Example D: Disruption — blocked path mid-trip

1. RIOT3.0 callback arrives: subtask failed with errorCode=PATH_BLOCKED on AMR-07 at node X.
2. Dispatch raises Exception, pauses trip via `PUT /api/v4/orders/{key}/operation` (CMD_ORDER_HELD), emits `exception.raised.v1`.
3. Planning's replan trigger fires → queries route costs for remaining stops from AMR-07's current position → finds feasible alternative route → emits updated plan (`plan.replanned.v1`).
4. Dispatch cancels held order (CMD_ORDER_CANCEL), issues new order from current position, resumes.
5. If no alternative: operator notified, manual takeover option presented on console.

---

## 5. Non-Functional Requirements & Technical Decisions

**Scalability**
- Stateless services, horizontally scaled behind gateway.
- Planning solver workers pulled from a queue; parallelism per tenant. Long-running optimizations run async with a plan-ready event.
- Event bus (Kafka) partitioned by `tenantId` + `facilityId` to preserve ordering per facility.

**Availability**
- Multi-AZ deployment; RPO ≤ 1 min, RTO ≤ 5 min for Dispatch & Execution (the live-critical path).
- Vendor adapter has fallback read-through cache for recent vehicle/map data to keep dashboards alive if vendor is down.
- Planning can degrade to "heuristic only" mode if solver service is unreachable.

**Data model / persistence**
- Transactional store (Postgres) for Orders, Jobs, Trips, Vehicles.
- Time-series store (TimescaleDB or ClickHouse) for telemetry and events.
- Object store (S3) for POD artifacts, map files.
- Event store for full domain event history (append-only, replayable).

**Observability**
- Every message carries `correlationId = orderId` to trace a delivery end-to-end across all contexts.
- OpenTelemetry traces on all inter-service calls including outbound vendor calls.
- Per-tenant dashboards: order throughput, SLA compliance, fleet utilization, exception rate, mean time to resolve.

**Security**
- Multi-tenant isolation at row level for all tables; each query filtered by `tenantId`.
- Bearer-token auth on all external APIs; mTLS between internal services.
- Vendor adapter holds vendor credentials in secret manager (Vault/KMS); rotated on schedule.
- Audit log of every state-changing action with actor, timestamp, before/after snapshot.

**Extensibility**
- Adding a new logistics pattern: implement new pattern classifier + solver strategy in Planning; no change to other contexts.
- Adding a new AMR vendor: implement `AdapterInterface`; register in vendor registry; capability discovery populates Fleet.
- Adding a new action type: register in action catalog per vehicle type; Planning auto-picks it if it matches capability demand.
- Adding a new business rule: rule engine (configurable, e.g., JSON Logic or Drools) consulted at Validation and at Planning; tenant-specific rule packs.

---

## 6. What to Build First — Recommended Delivery Roadmap

**Phase 1 — MVP (Point-to-Point, single vendor)**
Contexts 1, 3, 4 (basic), 5 (minimal), 6 (RIOT3Adapter). Planning is trivial (1 order = 1 job = 1 assignment). Proves the end-to-end wire.

**Phase 2 — Multi-Stop + Consolidation**
Upgrade Planning with consolidation window, TSP on drops, basic replanning. Introduce capability matrix in Fleet.

**Phase 3 — Full pattern coverage**
Cross-dock linkage, milk-run templates, multi-pick-multi-drop CVRPPD, what-if API.

**Phase 4 — Multi-vendor & heterogeneous fleet**
Second adapter (feeder-type AMR), capability-based assignment live, action catalog per vehicle type.

**Phase 5 — Advanced optimization & autonomy**
Predictive replanning, battery-aware dispatch, cost-model tuning per tenant, planner explainability UI, operator-in-the-loop escalation flow.

---

## 7. Open Questions / Decisions to Confirm

- Expected peak throughput (orders/hour/facility)? Drives solver mode choice.
- Preferred UI framework & whether the operator console is part of this scope or separate.
- Multi-tenant vs single-tenant deployment target? Drives isolation strategy.
- Mix of vendors expected at go-live and over 2 years? Drives how much to invest in ACL abstractions upfront.
- Is there an existing identity provider (SSO)? Drives auth integration.
- Real-time streaming of telemetry to BI/monitoring required, or periodic export acceptable?
- Regulatory requirements (e.g., hazmat audit, medical device traceability, food cold-chain)? May require specialized order attributes and evidence capture.
