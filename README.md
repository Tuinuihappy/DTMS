# AMR Delivery Planning System

> Vendor-agnostic delivery orchestration platform for Autonomous Mobile Robots (AMRs), supporting all major logistics patterns with a pluggable adapter layer for multiple AMR vendors.

![Status](https://img.shields.io/badge/status-design-blue)
![Version](https://img.shields.io/badge/version-1.0-green)
![Patterns](https://img.shields.io/badge/patterns-6-orange)
![Vendors](https://img.shields.io/badge/vendors-RIOT3.0%20%7C%20Feeder%20%7C%20Pluggable-purple)

---

## Table of Contents

- [Overview](#overview)
- [Supported Logistics Patterns](#supported-logistics-patterns)
- [Architecture](#architecture)
- [Bounded Contexts](#bounded-contexts)
- [Domain Model](#domain-model)
- [Supported AMR Vendors](#supported-amr-vendors)
- [Getting Started](#getting-started)
- [API Reference](#api-reference)
- [Event Catalog](#event-catalog)
- [Roadmap](#roadmap)
- [Documentation](#documentation)
- [Open Questions](#open-questions)

---

## Overview

The AMR Delivery Planning System transforms business-level delivery intent from upstream systems (WMS, ERP, MES, TMS) into optimized, executable plans for heterogeneous AMR fleets. It separates **business logic** (what needs to be delivered) from **physical logic** (how AMRs execute), enabling:

- **Pattern-agnostic orchestration** — one system handles Point-to-Point pickups and complex Cross-Dock workflows alike
- **Multi-vendor fleets** — mix lift-up, feeder, forklift, and tugger AMRs from different manufacturers
- **Business-aware optimization** — respect SLA tiers, service windows, load compatibility, and facility rules
- **Live disruption handling** — automatic replanning when paths block, AMRs fail, or priorities escalate

### Key Capabilities

| Capability | Description |
|---|---|
| Multi-pattern planning | 6 delivery patterns supported out of the box |
| Heterogeneous fleet | Capability-based assignment, not vendor-locked |
| Real-time dispatch | Sub-second assignment for single orders; batch optimization for milk runs |
| Exception handling | Blocked paths, battery critical, emergency stops → auto-replan |
| Resource coordination | Doors, lifts, chargers, traffic zones managed as first-class resources |
| Proof of delivery | Scan, photo, signature capture |
| Multi-tenant | Row-level isolation across facilities and organizations |

---

## Supported Logistics Patterns

The system supports six core delivery patterns, each mapped to a consistent Job/Leg/Stop model:

| Pattern | Use Case | Planning Strategy |
|---|---|---|
| **Point-to-Point** | Single pick → single drop | Nearest compatible AMR, greedy assignment |
| **Multi-Stop** | 1 pick → N drops (or reverse) | TSP with time windows and capacity |
| **Consolidation** | N orders → 1 merged delivery | Rolling window grouping by destination + compatible load |
| **Multi-Pick Multi-Drop** | N picks + M drops interleaved | CVRPPD with pickup-delivery precedence |
| **Milk Run** | Scheduled cyclic route | Fixed template, vehicle assignment only |
| **Cross Dock** | Inbound drop → dwell → outbound pick | Linked jobs with dependency graph |

See [Pattern Details](./docs/patterns.md) for solver implementation notes.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         External Integrations                           │
│         WMS │ ERP │ MES │ TMS │ Operator UI │ Mobile │ BI               │
└───────────────────────────┬─────────────────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────────────────┐
│                   API Gateway (AuthN / AuthZ / Rate Limit)              │
└──┬──────────────┬──────────────┬──────────────┬──────────────┬──────────┘
   │              │              │              │              │
┌──▼──────┐ ┌─────▼──────┐ ┌────▼──────┐ ┌────▼─────┐ ┌──────▼─────┐
│ 1.Order │ │ 2.Planning │ │3.Dispatch │ │ 4.Fleet  │ │5.Facility  │
│   Mgmt  │ │ & Optimize │ │ & Execute │ │ & Asset  │ │ & Topology │
└────┬────┘ └──────┬─────┘ └─────┬─────┘ └────┬─────┘ └─────┬──────┘
     │            │              │             │             │
     └────────────┴──────┬───────┴─────────────┴─────────────┘
                         │ Event Bus (Kafka / NATS)
                  ┌──────▼──────┐
                  │ 6. Vendor   │
                  │ Adapter     │
                  │ (ACL)       │
                  └──────┬──────┘
                         │
        ┌────────────────┼────────────────┐
        ▼                ▼                ▼
   ┌─────────┐    ┌──────────┐      ┌──────────┐
   │ RIOT3.0 │    │ Feeder   │      │ Other    │
   │  SEER   │    │ AMR      │      │ Vendors  │
   └─────────┘    └──────────┘      └──────────┘
```

**Design principles:**
- **Bounded contexts** — each with its own ubiquitous language and persistence
- **Event-driven** — cross-context communication via domain events on a partitioned bus
- **ACL isolation** — only the Vendor Adapter speaks vendor-specific protocols
- **Capability-based assignment** — Planning matches job requirements to vehicle capabilities, never vendor names

---

## Bounded Contexts

### 1. Delivery Order Management

Owns the lifecycle of business-level delivery intent from upstream systems.

- **Responsibilities**: order ingestion, validation, enrichment, amendments, recurring templates, state machine
- **Does NOT know about**: AMRs, maps, vendor protocols
- **Key state flow**: `DRAFT → SUBMITTED → VALIDATED → READY_TO_PLAN → PLANNING → PLANNED → DISPATCHED → IN_PROGRESS → COMPLETED`

### 2. Planning & Optimization

The brain of the system. Transforms DeliveryOrders into executable Jobs.

- **Two-layer solver**: greedy heuristic (realtime) + MILP/CVRP (batch)
- **Pattern classifier**: auto-detects or applies the 6 patterns
- **What-if simulation**: evaluate scenarios before committing
- **Replanning**: triggered by disruption, SLA risk, priority change, amendment
- **Explainability**: every assignment carries a planning trace

### 3. Dispatch & Execution

Takes committed Jobs and runs them on the floor. Source of truth for live state.

- **Job→Task translation** using vendor-specific action catalogs
- **Execution monitor**: normalizes vendor callbacks into canonical events
- **Exception handling**: path blocked, emergency stop, action failure, battery critical
- **Live operations**: pause, resume, skip, cancel, retarget, operator takeover
- **Proof of delivery**: scan, photo, signature capture

### 4. Fleet & Asset Management

AMRs as strategic assets.

- **Registry**: identity, type, capabilities, firmware, serial
- **Real-time state**: position, battery, health, current trip
- **Capability catalog**: what each vehicle type can do (MOVE, LIFT, ROTATE, SIDE_LOAD, etc.)
- **Charging strategy**: opportunistic / scheduled / reserved
- **Maintenance**: scheduled and corrective, blocks dispatch
- **Group management**: dynamic tag-based vehicle pools

### 5. Facility & Topology

The physical world the AMR operates in.

- **Maps**: multi-map, multi-floor, multi-facility with version management
- **Stations**: pick, drop, charge, park, checkpoint, cross-dock dock
- **Zones**: traffic, exclusive, slow, restricted with rule enforcement
- **Route graph**: edges with cost, carrier compatibility, direction
- **Facility resources**: doors, air showers, elevators, chargers, stoppers
- **Topology overlays**: temporary blockages without mutating base maps

### 6. Vendor Adapter (ACL)

Isolates the system from vendor-specific APIs and behaviors.

- **Canonical contract**: same interface, one implementation per vendor
- **Action catalog mapping**: data-driven, per `(tenant, vehicleType)`
- **Callback normalization**: vendor events → canonical domain events
- **Health probes**: circuit breakers, vendor health visibility
- **Capability discovery**: probe connected vendors, populate Fleet capability registry

---

## Domain Model

Core glossary — the ubiquitous language that anchors all communication:

| Term | Meaning |
|---|---|
| **DeliveryOrder** | Business-level delivery request from upstream. Intent, not execution. |
| **Job** | Operational unit produced by Planning. One order → one or more jobs depending on pattern. |
| **Leg** | A segment of a Job (e.g., pickup-to-dropoff). One Job has one or more Legs. |
| **Stop** | A physical visit at a Station (pick, drop, park, charge, wait). |
| **Task** | Lowest-level, vendor-specific directive (MOVE or ACT). Maps to RIOT3.0 `mission`. |
| **Trip** | An AMR's actual execution of a Job with real timestamps and events. |
| **Load Unit** | The thing being moved: carton, pallet, tote, rack, shelf. |
| **Carrier** | Physical AMR (lift-up, feeder, forklift, tugger). |
| **Zone** | Logical region with rules (speed, priority, exclusive access). |
| **Resource** | Any constrained entity: lift, conveyor, door, charger, traffic area, stopper. |

**Pattern-to-model mapping:**

| Pattern | DeliveryOrders | Jobs | Legs per Job |
|---|---|---|---|
| Point-to-Point | 1 | 1 | 1 pick + 1 drop |
| Multi-Stop | 1 | 1 | 1 pick + N drops |
| Multi-Pick Multi-Drop | 1 | 1 | N picks + M drops (interleaved) |
| Consolidation | N | 1 merged | N picks + 1 drop |
| Milk Run | 1 (scheduled) | 1 cyclic | N stops in loop |
| Cross Dock | 2 (inbound + outbound) | 2 linked | pick-drop-pick-drop bridge |

---

## Supported AMR Vendors

### RIOT3.0 (SEER AMR) — Lift-up type

Full-featured integration with the RIOT standard interface:

- **Base URL**: `/api/v4`
- **Auth**: Bearer Token (via User Application Management Interface)
- **Port**: 12000 (default)
- **Action catalog**: `actionId 4` with parameter pairs

Example action mappings:

| Canonical action | actionType | param0 | param1 |
|---|---|---|---|
| LIFT_SHELF_ALIGNED (max) | 4 | 1 | 0 |
| LIFT_SHELF_ALIGNED (specified mm) | 4 | 1 | X |
| LOWER_SHELF_ZERO | 4 | 2 | 0 |
| LIFT_PLATFORM (with overload) | 4 | 9 | X |
| ROTATE_PLATFORM | 4 | 12 | X (0.1°) |
| PLATFORM_INIT | 4 | 15 | 0 |

### Feeder-type AMR — Program triplet protocol

Side loading and unloading operations via `program1/program2/program3`:

| Canonical action | program1 | program2 | program3 |
|---|---|---|---|
| INIT | 192 | 100 | 100 |
| LEFT_SIDE_LOAD | 192 | 1 | 3 |
| LEFT_SIDE_UNLOAD | 192 | 1 | 4 |
| RIGHT_SIDE_LOAD | 192 | 2 | 3 |
| RIGHT_SIDE_UNLOAD | 192 | 2 | 4 |
| FRONT_PROBE | 192 | 22 | W (−50<H<50mm) |

### Adding a New Vendor

1. Implement `AdapterInterface` (canonical contract)
2. Register in vendor registry
3. Define action catalog mapping (data, not code)
4. Deploy in shadow mode for validation against live data
5. Cut over to active dispatch

No changes required in Planning, Dispatch, Fleet, or Facility contexts.

---

## Getting Started

### Prerequisites

- Access to at least one AMR vendor system (RIOT3.0 recommended for first deployment)
- PostgreSQL 14+ for transactional store
- Kafka or NATS for event bus
- Redis for caching
- S3-compatible object store for POD artifacts

### Quick Start (conceptual)

```bash
# 1. Configure vendor credentials
export RIOT3_BASE_URL=https://your-riot-instance:12000/api/v4
export RIOT3_TOKEN=<your-bearer-token>

# 2. Register callback endpoint with vendor
# Point RIOT3.0 /api/v4/notify callback to your Dispatch context's ingress

# 3. Sync master data
curl -X POST https://<your-gateway>/api/v1/facilities/sync

# 4. Submit first order
curl -X POST https://<your-gateway>/api/v1/orders \
  -H "Authorization: Bearer <token>" \
  -H "Idempotency-Key: <uuid>" \
  -H "Content-Type: application/json" \
  -d '{
    "upperKey": "WMS-ORDER-001",
    "orderType": "WORK",
    "priority": 10,
    "pickup": {"facilityId": "F1", "stationHint": "S10"},
    "drop":   {"facilityId": "F1", "stationHint": "S87"}
  }'
```

### Configuration

See [Configuration Reference](./docs/configuration.md) for:
- Tenant setup
- Vendor adapter registration
- Action catalog configuration
- Charging policies
- Replanning triggers
- SLA tiers

---

## API Reference

### Delivery Order Management

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/orders` | Create order (idempotent via header) |
| `POST` | `/api/v1/orders:bulk` | Create multiple orders transactionally |
| `PATCH` | `/api/v1/orders/{id}` | Amendment (partial update) |
| `POST` | `/api/v1/orders/{id}:hold` | Hold |
| `POST` | `/api/v1/orders/{id}:release` | Release from hold |
| `POST` | `/api/v1/orders/{id}:cancel` | Cancel |
| `POST` | `/api/v1/orders/{id}:split` | Split into multiple |
| `POST` | `/api/v1/orders/{id}:merge` | Merge with another |
| `GET` | `/api/v1/orders` | List (filter by state, SLA, date, tag) |
| `GET` | `/api/v1/orders/{id}` | Get single order |
| `GET` | `/api/v1/orders/{id}/timeline` | Full audit trail |
| `POST` | `/api/v1/order-templates` | Create recurring template (milk run) |

### Planning & Optimization

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/plans:generate` | Generate plan for orders |
| `POST` | `/api/v1/plans:simulate` | What-if simulation |
| `POST` | `/api/v1/plans/{id}:commit` | Commit plan for dispatch |
| `POST` | `/api/v1/plans/{id}:replan` | Replan with reason code |
| `GET` | `/api/v1/plans/{id}/kpis` | KPIs: makespan, SLA, utilization |

### Dispatch & Execution

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/trips:start` | Start executing committed job |
| `POST` | `/api/v1/trips/{id}:pause` | Pause trip |
| `POST` | `/api/v1/trips/{id}:resume` | Resume trip |
| `POST` | `/api/v1/trips/{id}:cancel` | Cancel trip |
| `POST` | `/api/v1/trips/{id}:skip-current` | Skip current task |
| `POST` | `/api/v1/trips/{id}:retry-current` | Retry current task |
| `POST` | `/api/v1/trips/{id}:reassign` | Operator takeover |
| `GET` | `/api/v1/trips/{id}` | Get live trip state |
| `GET` | `/api/v1/trips/{id}/events` | Tailable event log |
| `POST` | `/api/v1/trips/{id}/pod` | Attach proof of delivery |

### Fleet & Asset Management

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/vehicles` | List (filter by state, group, zone) |
| `GET` | `/api/v1/vehicles/{id}` | Get full vehicle state |
| `POST` | `/api/v1/vehicles:operation` | Bulk operation |
| `POST` | `/api/v1/vehicles/{id}/maintenance` | Create maintenance record |
| `GET` | `/api/v1/vehicle-types/{id}/capabilities` | Capability catalog |
| `GET` | `/api/v1/fleet/kpi` | Availability, utilization, MTBF |

### Facility & Topology

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/facilities` | List facilities |
| `GET` | `/api/v1/maps` | List maps |
| `GET` | `/api/v1/maps/{mapId}/stations` | Stations on map |
| `GET` | `/api/v1/route/costs` | Route cost query (with caching) |
| `GET` | `/api/v1/stations` | List stations (by type, zone, compatibility) |
| `POST` | `/api/v1/zones` | Create zone |
| `POST` | `/api/v1/resources/{id}:command` | Door/lift/charger commands |
| `POST` | `/api/v1/topology-overlays` | Create temporary overlay |

For the complete OpenAPI specification, see [./docs/openapi.yaml](./docs/openapi.yaml).

---

## Event Catalog

All events follow the pattern `<aggregate>.<event>.v<version>` and carry `correlationId` (typically the originating order ID) for end-to-end tracing.

### Delivery Order events
- `delivery-order.submitted.v1`
- `delivery-order.validated.v1`
- `delivery-order.amended.v1`
- `delivery-order.cancelled.v1`
- `delivery-order.recurring-instance-generated.v1`

### Planning events
- `plan.generated.v1`
- `plan.committed.v1`
- `plan.rejected.v1`
- `plan.replanned.v1`
- `job.created.v1`
- `job.assigned.v1`
- `reservation.created.v1`
- `reservation.released.v1`

### Dispatch events
- `trip.started.v1`
- `trip.leg-completed.v1`
- `trip.completed.v1`
- `trip.failed.v1`
- `task.dispatched.v1`
- `task.completed.v1`
- `task.failed.v1`
- `exception.raised.v1`
- `exception.resolved.v1`
- `pod.captured.v1`

### Fleet events
- `vehicle.state-changed.v1` (debounced)
- `vehicle.battery-low.v1`
- `vehicle.emergency-triggered.v1`
- `vehicle.maintenance-entered.v1`
- `vehicle.maintenance-exited.v1`
- `vehicle.commissioned.v1`
- `vehicle.retired.v1`

### Facility events
- `map.synced.v1`
- `map.sync-failed.v1`
- `topology-overlay.activated.v1`
- `topology-overlay.expired.v1`
- `resource.state-changed.v1`

---

## Roadmap

### Phase 1 — MVP (Point-to-Point, single vendor)
Contexts 1, 3, 4 (basic), 5 (minimal), 6 (RIOT3Adapter). Planning is trivial (1 order = 1 job = 1 assignment). Proves the end-to-end wire.

### Phase 2 — Multi-Stop + Consolidation
Upgrade Planning with consolidation window, TSP on drops, basic replanning. Introduce capability matrix in Fleet.

### Phase 3 — Full pattern coverage
Cross-dock linkage, milk-run templates, multi-pick-multi-drop CVRPPD, what-if API.

### Phase 4 — Multi-vendor & heterogeneous fleet
Second adapter (feeder-type AMR), capability-based assignment live, action catalog per vehicle type.

### Phase 5 — Advanced optimization & autonomy
Predictive replanning, battery-aware dispatch, cost-model tuning per tenant, planner explainability UI, operator-in-the-loop escalation.

---

## Non-Functional Requirements

| Requirement | Target |
|---|---|
| Order ingestion throughput | Configurable per tenant; typical 500/min/facility |
| Realtime dispatch latency | < 1 second for Point-to-Point |
| Batch planning latency | < 30 seconds for 100-order milk run |
| RPO (live ops) | ≤ 1 minute |
| RTO (live ops) | ≤ 5 minutes |
| Vehicle state staleness | ≤ 2 seconds for dashboards |
| Availability | 99.9% on critical path (Dispatch) |
| Multi-tenant isolation | Row-level with per-query tenant filter |

---

## Documentation

Full design documentation lives in the `docs/` folder:

- **[System Design (full)](./docs/AMR_Delivery_Planning_System_Design.md)** — complete architectural reference
- **[Pattern Details](./docs/patterns.md)** — solver strategies per pattern
- **[Vendor Integration Guide](./docs/vendor-integration.md)** — adding a new AMR vendor
- **[Configuration Reference](./docs/configuration.md)** — all tunable parameters
- **[OpenAPI Spec](./docs/openapi.yaml)** — machine-readable API reference
- **[Event Schemas](./docs/events/)** — JSON schemas for all domain events
- **[Deployment Guide](./docs/deployment.md)** — topology, scaling, observability

---

## Open Questions

These decisions need confirmation before Phase 1 begins:

- **Expected peak throughput** (orders/hour/facility)? Drives solver mode choice.
- **Vendor mix at go-live and over 2 years**? Drives ACL abstraction investment level.
- **Multi-tenant vs single-tenant deployment**? Drives data isolation strategy.
- **Operator console scope**: part of this system, or separate?
- **Identity provider (SSO)**: integrated, or self-managed auth?
- **Real-time telemetry to BI**: streaming required, or periodic export acceptable?
- **Regulatory requirements**: hazmat audit, medical device traceability, food cold-chain?

---

## Glossary

See [Domain Model](#domain-model) above for the full ubiquitous language.

---

## License

Proprietary. Contact the project team for usage terms.

---

## Contact

Project team — AMR Delivery Planning Working Group

*Last updated: April 2026*


RIOT
Url: http://10.204.212.28:12000
Auth: API Key
Key: Authorization
Value: ***REMOVED_RIOT3_TOKEN***
Add to: Header
