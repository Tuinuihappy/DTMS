# Multi-Mode Transport — Documentation Index

DTMS refactor from AMR-only (RIOT3) to enterprise multi-mode (AMR + Manual + Fleet).

## Background

- DTMS วันนี้รองรับเฉพาะ AMR (RIOT3) แม้ enum `TransportMode { Amr, Manual, Fleet }` จะมีไว้แล้ว
- Business ต้องการ Manual mode (operator + mobile app) และ Fleet mode (3PL provider — Kerry, Flash) ใน enterprise-level deployment
- Refactor นี้สร้าง module structure แบบ symmetric peer และ abstraction layer เพื่อให้ทุก mode ใช้ Trip aggregate FSM เดียวกัน

## Document Structure

```
docs/multi-mode-transport/
├── README.md                          ← (you are here)
├── adr/                               ← Architecture Decision Records
│   ├── adr-001-multi-mode-transport-split.md       ← Why split modules
│   ├── adr-002-facility-station-hierarchy.md       ← Warehouse ↔ AmrStation
│   ├── adr-003-trip-extension-tables.md            ← Per-mode extension tables
│   ├── adr-004-testing-strategy.md                 ← 4-tier test gates
│   ├── adr-005-push-notification-gateway.md        ← FCM for operator app
│   ├── adr-006-transport-mode-feature-flag.md      ← Config-driven mode enable
│   ├── adr-007-mobile-api-authentication.md        ← JWT + device binding
│   ├── adr-008-migration-strategy.md               ← Manual EF migrations + conventions
│   ├── adr-009-pod-object-storage.md               ← S3-compatible (MinIO local, S3 prod)
│   └── adr-010-geofence-implementation.md          ← NetTopologySuite (no PostGIS)
├── phases/                            ← Per-phase implementation guides
│   ├── phase-1-foundation.md          ← Namespace + DI rewire
│   ├── phase-2-facility-vehicle-split.md
│   ├── phase-3-dispatch-abstraction.md
│   ├── phase-4-transport-manual.md
│   └── phase-5-transport-fleet.md
├── diagrams/
│   └── architecture.md                ← Mermaid sequence + module dependency
├── api/
│   └── manual-operator-api.md         ← Mobile API contract (OpenAPI-style)
└── templates/                            ← Reusable boilerplate (12 files)
    ├── README.md                          ← How to use templates
    ├── adr-template.md                    ← New ADR boilerplate
    ├── aggregate-template.cs              ← DDD aggregate root
    ├── command-handler-template.cs        ← Command + Handler pair
    ├── consumer-template.cs               ← MassTransit IConsumer
    ├── background-service-template.cs     ← BackgroundService (poll/watchdog/cleanup)
    ├── repository-template.cs             ← Repository (Domain interface + EF impl)
    ├── frontend-component-template.tsx    ← Next.js + shadcn (base-ui) component
    ├── unit-test-template.cs              ← xUnit + NSubstitute + FluentAssertions
    ├── integration-test-template.cs       ← DtmsWebApplicationFactory + Testcontainers
    ├── migration-template.cs              ← EF migration file
    └── migration-designer-template.cs     ← Migration snapshot companion
```

## Reading Order

1. **เริ่มจากที่นี่** เพื่อเข้าใจภาพรวม
2. **Core architecture** — อ่านตามลำดับ:
   - [ADR-001](adr/adr-001-multi-mode-transport-split.md) — "ทำไม split modules"
   - [ADR-002](adr/adr-002-facility-station-hierarchy.md) + [ADR-003](adr/adr-003-trip-extension-tables.md) — data model
   - [ADR-006](adr/adr-006-transport-mode-feature-flag.md) — deployment toggle
3. **Visual** — ดู [architecture diagrams](diagrams/architecture.md)
4. **Role-specific deep dives:**
   - **Mobile / push integration team**: [ADR-005](adr/adr-005-push-notification-gateway.md) + [ADR-007](adr/adr-007-mobile-api-authentication.md) + [ADR-009](adr/adr-009-pod-object-storage.md) + [Manual Operator API](api/manual-operator-api.md)
   - **QA / DevOps**: [ADR-004](adr/adr-004-testing-strategy.md) + [ADR-008](adr/adr-008-migration-strategy.md)
   - **Backend / data**: [ADR-008](adr/adr-008-migration-strategy.md) + [ADR-010](adr/adr-010-geofence-implementation.md)
5. **Implementation** — เริ่ม [Phase 1](phases/phase-1-foundation.md) → 5

## ADR Decision Map (by axis)

| Concern | ADR(s) | Key choice |
|---|---|---|
| Module structure | [001](adr/adr-001-multi-mode-transport-split.md) | Per-mode peer modules + Abstractions |
| Domain modeling | [002](adr/adr-002-facility-station-hierarchy.md), [003](adr/adr-003-trip-extension-tables.md) | Warehouse promoted; Trip extension tables |
| Quality | [004](adr/adr-004-testing-strategy.md), [008](adr/adr-008-migration-strategy.md) | 4-tier gates; manual EF migrations |
| Operational toggle | [006](adr/adr-006-transport-mode-feature-flag.md) | Config-driven per mode |
| Mobile platform | [005](adr/adr-005-push-notification-gateway.md), [007](adr/adr-007-mobile-api-authentication.md), [009](adr/adr-009-pod-object-storage.md), [010](adr/adr-010-geofence-implementation.md) | FCM + JWT + S3 + NTS |

## ADRs Summary

| # | Decision | Reading time |
|---|---|---|
| [001](adr/adr-001-multi-mode-transport-split.md) | Split into `Transport.{Amr,Manual,Fleet}` peer modules + `Transport.Abstractions` | 8 min |
| [002](adr/adr-002-facility-station-hierarchy.md) | Promote Warehouse to first-class; AmrStation moves to Transport.Amr | 10 min |
| [003](adr/adr-003-trip-extension-tables.md) | Per-mode 1:0..1 extension tables instead of nullable columns on Trip | 8 min |
| [004](adr/adr-004-testing-strategy.md) | 4-tier strict gates: unit + integration + architecture + manual smoke | 6 min |
| [005](adr/adr-005-push-notification-gateway.md) | FCM via `IPushNotificationGateway` abstraction | 5 min |
| [006](adr/adr-006-transport-mode-feature-flag.md) | Config-driven enable per mode; strongly-typed options + startup validation | 5 min |
| [007](adr/adr-007-mobile-api-authentication.md) | JWT with audience separation + device-bound refresh tokens | 6 min |
| [008](adr/adr-008-migration-strategy.md) | Manual EF migrations + MigrationId conventions + per-DbContext apply | 7 min |
| [009](adr/adr-009-pod-object-storage.md) | S3-compatible storage (MinIO local, AWS S3 production); server-mediated upload | 6 min |
| [010](adr/adr-010-geofence-implementation.md) | NetTopologySuite in-memory (no PostGIS); WKT polygon format; defer PostGIS | 6 min |

## Target Module Structure (After Refactor)

```
src/Modules/
├── Shared kernel
│   ├── DeliveryOrder/      (unchanged)
│   ├── Planning/           (refactored)
│   ├── Facility/           (refactored — Warehouse first-class)
│   ├── Dispatch/           (refactored — transport-agnostic Trip)
│   ├── OmsAdapter/         (unchanged)
│   └── Vehicle/            (renamed from Fleet)
│
└── Transport modes (peer modules)
    ├── Transport.Abstractions/   (shared contracts)
    ├── Transport.Amr/            (RIOT3)
    ├── Transport.Manual/         (operator + mobile)
    └── Transport.Fleet/          (3PL providers)
```

## Key Principles

1. **Operator-as-Vendor analogy** — Manual operator implements same `IVendorEnvelopeOperationService` contract as RIOT3
2. **Trip aggregate unchanged** — same FSM (Created/InProgress/Paused/Completed/Failed/Cancelled) across all modes
3. **Strategy + Router** — `IDispatchStrategy` per mode, `IVendorOperationsRouter` routes by `trip.TransportMode`
4. **Per-mode extension tables** — Trip core stays clean; AMR/Manual/Fleet specific fields go to extension tables (1:0..1)
5. **Conditional registration** — `services.AddTransportXxx()` extensions toggled by config flag

## Phase Summary

| Phase | Title | Sprints | Risk | Schema Change |
|---|---|---|---|---|
| 1 | Foundation — Namespace + DI Rewire | 1-2 | Low | None |
| 2 | Facility / Vehicle Split | 3-4 | High | Yes (breaking) |
| 3 | Dispatch Plan Abstraction + Trip Extensions | 5-6 | High | Yes |
| 4 | Implement Transport.Manual | 7-8 | High | Yes (additive) |
| 5 | Implement Transport.Fleet | 9-10 | Medium-High | Yes (additive) |

## Status

- Plan approved: 2026-06-22
- Pre-launch (schema breaking acceptable)
- Test rigor: Strict (build + unit + integration + manual smoke per phase)

## Related Documents

- Top-level plan: `~/.claude/plans/implement-plan-tingly-haven.md`
- Existing architecture overview: [docs/AMR_Delivery_Planning_System_Design.md](../AMR_Delivery_Planning_System_Design.md)
- Recent phase plans: [docs/plans/](../plans/)
