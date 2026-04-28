# Production Implementation Plan

> Based on `PRODUCTION_READINESS.md` last updated 2026-04-28.
> Current state: staging-ready prototype, not production-ready.
> Goal: close the remaining production blockers and produce deployable evidence.

## Executive Summary

Most critical runtime defects are already fixed: C1-C5, H1-H2, M1-M2, M4-M5.
The remaining work is production hardening, not another feature sprint.

Primary blockers:

1. H3 Multi-tenancy is not implemented.
2. EF migrations infrastructure exists, but initial migrations still need to be scaffolded and verified.
3. Integration tests are incomplete for readiness scenarios.
4. Load/stress testing has not been defined or executed against the 500 orders/minute target.
5. Release-gate evidence is missing: secrets, readiness checks, observability, backup/restore, rollback, RIOT3 contract verification.
6. `PRODUCTION_READINESS.md` has document drift in the final estimate row: it still lists H2, M1, and M5 as remaining even though those sections are marked fixed.

## Production Definition Of Done

The system is production-ready only when all of these are true:

- Production starts with EF migrations, not `EnsureCreated`.
- Tenant-owned data is isolated by `TenantId` at API, repository, EF query filter, event, and background-consumer boundaries.
- Cross-tenant reads and writes are covered by automated tests.
- Readiness integration scenarios run in CI without manual database setup.
- A staging-like load test proves the target throughput or documents the revised target.
- No production secrets are committed or defaulted.
- `/health` and `/health/ready` reflect process and dependency health.
- Deployment, rollback, backup, restore, and vendor-contract checks have owner/date/evidence.

## Phase 0 - Baseline Verification

**Objective:** confirm the current code and readiness document match reality before changing schema or tenant behavior.

**Tasks**

- Run the full solution test suite:
  - `dotnet test AMR.DeliveryPlanning.slnx`
- Run the central integration project directly:
  - `dotnet test tests/Integration/AMR.DeliveryPlanning.IntegrationTests/AMR.DeliveryPlanning.IntegrationTests.csproj`
- Start local dependencies:
  - `docker compose up -d`
- Start the API with user-secrets or environment variables configured.
- Verify endpoints:
  - `GET /health`
  - `GET /health/ready`
  - `/swagger`
- Update `PRODUCTION_READINESS.md` document drift:
  - Header fixed list should include H2, M1, M5 if they are verified.
  - Final estimate row should not list H2, M1, or M5 as remaining.
  - Test count should reflect the actual current result, not the old `44/44` value if it changed.

**Acceptance Criteria**

- Current build/test status is known.
- Local infrastructure boots cleanly.
- Health endpoints return expected results.
- Readiness document has no contradiction between fixed sections and remaining-work summary.

**Estimate:** 0.5 day

## Phase 1 - EF Migrations For Production

**Objective:** remove production dependence on `EnsureCreated` fallback.

**Implementation Order**

1. Scaffold initial migrations for all contexts:
   - Facility
   - Fleet
   - DeliveryOrder
   - Planning
   - Dispatch
   - VendorAdapter
   - AuthDbContext
   - OutboxDbContext

2. Review generated migrations:
   - Schema names are correct.
   - Indexes match the DbContext model.
   - FK cascade behavior is intentional.
   - PostgreSQL-specific mappings are correct, especially `xmin`.
   - Unique indexes that may become tenant-local are identified before Phase 2.

3. Verify migrations:
   - Run against a clean PostgreSQL database.
   - Run API startup and confirm `MigrateAsync()` path is used.
   - Run existing tests after migrations are present.

4. Add production guard:
   - Production must fail fast if no migrations exist and the app would fall back to `EnsureCreated`.
   - Development may keep fallback behavior if explicitly limited to non-production environments.

**Key Files**

- `src/AMR.DeliveryPlanning.Api/Program.cs`
- `src/**/Infrastructure/Data/*DbContext.cs`
- `src/**/Infrastructure/Data/*DbContextFactory.cs`
- `src/AMR.DeliveryPlanning.Api/Auth/AuthDbContext.cs`
- `src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxDbContext.cs`

**Acceptance Criteria**

- A clean database can be created only from migrations.
- API startup applies migrations automatically when migrations exist.
- Production cannot silently use `EnsureCreated`.
- Migration commands are documented for macOS/Linux and Windows.

**Estimate:** 1-2 days

## Phase 2 - Multi-Tenancy

**Objective:** enforce tenant isolation across all tenant-owned business data.

### 2.1 Tenant Context

**Tasks**

- Add shared tenant abstractions:
  - `ITenantContext`
  - `TenantContext`
  - optional `ITenantContextAccessor` if background consumers need scoped mutation.
- Resolve tenant from JWT claims in API requests.
- Define required claim name, for example `tenant_id`.
- Define system/background behavior:
  - Consumers must establish tenant context from integration-event metadata.
  - Startup seeders must run with explicit system/global tenant behavior.
  - Global reference data must be documented as tenant-shared or tenant-owned.

**Acceptance Criteria**

- Tenant is available as a scoped service during API requests.
- Tenant-protected endpoints reject missing tenant claims.
- System code cannot accidentally run with an empty tenant unless explicitly allowed.

### 2.2 Domain And Persistence Model

**Tenant-owned tables, minimum scope**

- DeliveryOrder:
  - `DeliveryOrders`
  - `OrderLines`
  - amendments/history/timeline records if persisted separately
- Planning:
  - `Jobs`
  - `Legs`
  - `Stops`
  - `JobDependencies`
  - cost model configs if tenant-specific
- Dispatch:
  - `Trips`
  - `RobotTasks`
  - execution events
  - exceptions
  - proof of delivery
- Fleet:
  - `Vehicles`
  - `VehicleGroups`
  - `VehicleGroupMembers`
  - charging policies
  - maintenance records
- Facility:
  - Decide whether `Maps`, `Stations`, `RouteEdges`, resources, and topology overlays are tenant-owned or shared.
- VendorAdapter:
  - Decide whether action catalog is global or tenant-scoped.

**Tasks**

- Add `TenantId` to aggregate roots and tenant-owned records.
- Pass tenant into constructors/factories instead of assigning it ad hoc after creation.
- Include `TenantId` in tenant-local unique indexes.
- Include `TenantId` in join tables where both sides are tenant-owned.
- Add migrations for all tenant columns and indexes.

**Acceptance Criteria**

- Tenant-owned tables contain non-null `TenantId`.
- Tenant-local uniqueness works independently per tenant.
- Join tables cannot link records across tenants.

### 2.3 EF Query Filters And Repositories

**Tasks**

- Inject tenant context into each tenant-owned DbContext.
- Add global query filters:
  - `entity.TenantId == tenantContext.TenantId`
- Ensure repositories do not use `IgnoreQueryFilters()` except in explicitly reviewed admin/system paths.
- Audit raw SQL and cross-module lookups.
- Ensure background handlers set tenant context before DbContext is first used.

**Acceptance Criteria**

- Normal repositories automatically filter by tenant.
- Cross-tenant read by ID returns the designed safe response, usually 404.
- Cross-tenant mutation is rejected.

### 2.4 Events, Outbox, And Consumers

**Tasks**

- Add `TenantId` to tenant-owned integration events.
- Ensure domain events converted to outbox messages carry tenant metadata.
- Ensure MassTransit consumers establish tenant context before DB access.
- Ensure RIOT3 webhook handling can resolve tenant safely:
  - Prefer task/trip lookup through a tenant-aware vendor reference mapping.
  - If webhook lacks tenant, define a deterministic lookup path and reject ambiguous matches.

**Acceptance Criteria**

- Events that create or mutate tenant-owned state carry tenant identity.
- Consumers cannot write tenant-owned data without tenant context.
- RIOT3 callbacks cannot update another tenant's task/trip.

### 2.5 API And Auth

**Tasks**

- Add tenant claim to issued tokens or test auth helper.
- Update auth/test token helpers.
- Make tenant behavior explicit for admin/system endpoints.
- Add API tests for:
  - Missing tenant claim.
  - Tenant A cannot read Tenant B order/trip/vehicle.
  - Tenant A cannot mutate Tenant B order/trip/vehicle.

**Acceptance Criteria**

- Tenant-protected endpoints require tenant identity.
- Tests prove cross-tenant isolation.
- Existing tests use a default tenant fixture.

**Estimate:** 4-7 days

## Phase 3 - Integration Test Completion

**Objective:** automate the readiness scenarios that currently remain unchecked.

**Use Existing Harness**

- `tests/Integration/AMR.DeliveryPlanning.IntegrationTests/DtmsWebApplicationFactory.cs`
- PostgreSQL Testcontainers
- In-memory distributed cache replacement for Redis
- Auth helper for token-backed clients

**Priority Test Cases**

1. End-to-end delivery flow:
   - Submit order.
   - Validate station IDs.
   - Plan job.
   - Assign vehicle.
   - Commit plan.
   - Dispatch trip.
   - Complete RIOT3 task event.

2. RIOT3 webhook behavior:
   - `finished` event updates task/trip state.
   - `failed` event records dispatch exception or failure state.
   - Unknown task ID returns a safe response and does not crash.

3. Idempotency:
   - Duplicate `POST /api/delivery-orders` with the same `Idempotency-Key` returns the same order ID.
   - Same body with a different key creates a new order as designed.

4. SLA validation:
   - SLA below minimum threshold is rejected.
   - Valid SLA moves order to `ReadyToPlan`.

5. Charging policy:
   - Battery below threshold records or emits `VehicleBatteryLowIntegrationEvent`.

6. Outbox:
   - Domain/integration event writes an outbox row.
   - Outbox processor marks message processed.
   - Failure keeps message retryable.

7. Capability assignment:
   - Job requiring `LIFT` only selects a vehicle/group with matching capability.

8. Amendment and timeline:
   - Patch order creates an amendment record.
   - Timeline exposes amendment/audit event.

9. Multi-tenancy regression:
   - Tenant A cannot see Tenant B delivery order.
   - Tenant A cannot dispatch Tenant B trip.
   - Tenant A cannot assign Tenant B vehicle.

**Acceptance Criteria**

- Readiness scenarios are automated.
- Tests run in CI without manual PostgreSQL setup.
- External dependencies are mocked, simulated, or contract-tested.
- Tests fail if tenant filtering is removed.

**Estimate:** 4-6 days

## Phase 4 - Load And Stress Testing

**Objective:** prove the non-functional target and expose bottlenecks before launch.

**Target**

- Validate at least 500 orders/minute, or document a revised product-approved target.

**Tasks**

- Add repeatable load scripts, preferably `k6`.
- Cover:
  - Auth/token setup.
  - Map/station/vehicle seed.
  - Order submit burst.
  - Planning and assignment mix.
  - Dispatch webhook callbacks.
- Capture:
  - p50/p95/p99 latency.
  - Error rate.
  - Database CPU, locks, connections, slow queries.
  - Redis hit rate.
  - Outbox backlog.
  - RabbitMQ queue depth.
  - API memory and thread pool behavior.
- Run:
  - Short smoke load.
  - 30-60 minute soak.
  - Spike test above target.
- Document bottlenecks and tuning values.

**Acceptance Criteria**

- Target throughput is met with acceptable latency and error rate.
- Outbox does not grow unbounded.
- Database connection pool remains stable.
- Rate limiting behavior is understood under load.

**Estimate:** 2-3 days

## Phase 5 - Release Gate

**Objective:** make go-live a checklist with evidence, not a judgment call.

**Tasks**

- Confirm secrets are supplied by environment variables or Vault/KMS:
  - `Jwt:Secret`
  - `ConnectionStrings:DefaultConnection`
  - `RabbitMq:*`
  - RIOT3 credentials/base URL
- Confirm readiness checks include required dependencies.
- Confirm logs, traces, and metrics are visible in the target environment.
- Confirm PostgreSQL backup and restore process.
- Confirm rollback procedure.
- Confirm RIOT3 route-cost response shape against the real vendor spec.
- Confirm action catalog with RIOT3 vendor spec.
- Confirm deployment runbook includes migrations.
- Update `PRODUCTION_READINESS.md` with:
  - final status,
  - test evidence,
  - load-test evidence,
  - owner/date for each release gate.

**Acceptance Criteria**

- No hardcoded secrets.
- Production starts only with valid configuration.
- Release checklist has owner/date/evidence.
- Final readiness state can be changed to `Production-ready`.

**Estimate:** 1-2 days

## Recommended Execution Sequence

1. Phase 0: verify baseline and fix documentation drift.
2. Phase 1: scaffold and verify migrations before broad schema changes.
3. Phase 2: implement multi-tenancy and update test fixtures.
4. Phase 3: complete readiness integration tests against the tenant-aware model.
5. Phase 4: run load and stress tests on staging-like infrastructure.
6. Phase 5: complete release gate and update production readiness evidence.

## Rough Timeline

| Phase | Estimate |
|---|---:|
| Baseline verification | 0.5 day |
| EF migrations | 1-2 days |
| Multi-tenancy | 4-7 days |
| Integration tests | 4-6 days |
| Load/stress testing | 2-3 days |
| Release gate | 1-2 days |
| **Total** | **2-3 weeks** |

## Immediate Next Actions

1. Run baseline verification and update the true test count.
2. Fix `PRODUCTION_READINESS.md` drift in the header/final estimate.
3. Scaffold migrations and remove production `EnsureCreated` fallback.
4. Start H3 with tenant context and API auth behavior before touching every entity.
5. Add cross-tenant integration tests as soon as the first tenant-owned aggregate is migrated.
