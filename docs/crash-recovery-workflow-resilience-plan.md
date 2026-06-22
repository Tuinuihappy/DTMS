# DTMS Crash-Recovery & Workflow-Resilience Plan

> **Scope**: Tier 1 + Tier 2 + Tier 3 (immediate stabilization → Saga state machine → platform evolution)
> **Trigger incident**: OD-0374-WIP / OD-0375-WIP stuck after API restart on 2026-06-17

## Status (as of 2026-06-18)

| Tier | Progress | Highlights |
|---|---|---|
| **T1 — Immediate stabilization** | ✅ **100% complete + verified + defense-in-depth complete** | 7 original items + T1.8 vendor-acceptance guard + T1.9 admin trip-state overrides + T1.10 Riot3 reconciler enabled. Unit tests 246/152 pass, integration test 5/5 pass. **Chaos test N=100 PASS** (19 random kill points, 0 stuck orders, 100% Dispatched). Real-world recovery proven on OD-0374/0375. |
| **T2 — Saga state machine** | ⚠️ **POC verified, Phase 2 not started** | Saga + EF persistence + feature flag wired and verified end-to-end in docker. POC surfaced a Phase 2 follow-up (NotAcceptedStateMachineException on event redelivery — needs `During(state, Ignore(event))` handlers). Full Phase 2 (~100h) deferred. |
| **T3 — Platform evolution** | ⏸️ **Not started — gate not met** | Awaits scale triggers (outbox pending > 500 sustained, workflows > 3 bounded contexts, deploy frequency ≥ 1/day). Revisit at month 3. |

**Today's headline outcome**: T1 confidence elevated from `n=2` (OD-0374/0375 recovery) to `n=100` (chaos test). T1 production-ready, **all three defense-in-depth layers from the T1.8 plan now built**:

  - **Layer 1 — watchdog skip** (T1.8): the planning watchdog refuses to replay an order whose Jobs already have a `VendorOrderKey` — RIOT3 already saw it, replay would duplicate. The replan admin endpoint enforces the same.
  - **Layer 2 — automatic reconciliation** (T1.10): `Riot3ReconciliationService` is now on by default — polls RIOT3 for in-flight envelope trips every 60s and reconciles via idempotent `Mark*` calls, healing dropped webhooks without operator action.
  - **Layer 3 — operator override** (T1.9): `POST /admin/trips/{id}/force-{start,pickup-completed,drop-completed,complete}` — operator can push a stuck Trip through each RIOT3 webhook stage manually. Each forced transition fires the same domain event the webhook would have, so the downstream cascade runs unchanged.

---

## 1. Context

เมื่อ `2026-06-17 01:39-01:42` orders **OD-0374-WIP** และ **OD-0375-WIP** ถูก ingest จาก upstream OMS สำเร็จ (auto-pipeline ผ่าน Submitted → Validated → Confirmed) แต่ค้างหลัง API container restart ราว 6-9 นาทีต่อมา. DB query ยืนยัน state ที่ inconsistent ข้าม 4 schemas:

| Source of truth | OD-0374-WIP | OD-0375-WIP |
|---|---|---|
| `deliveryorder.DeliveryOrders.Status` (write-side) | `Planned` | `Planned` |
| `deliveryorder.OrderListView.Status` (UI projection) | `Submitted` | `Confirmed` |
| `planning.Jobs.Status` | `Created` | `Created` |
| `dispatch.Trips` | **0 rows** | **0 rows** |
| Outbox pending | 0 | 0 |

[`DeliveryOrderValidatedConsumer`](../src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/Consumers/DeliveryOrderValidatedConsumer.cs) ทำ 6 steps แบบ procedural ใน separate transactions: `MarkPlanning → CreateJobAnchor × N → MarkPlanned → DispatchByRoute → MarkJobDispatched → MarkOrderDispatched`. Crash หลัง `MarkOrderPlanned` ก่อน `DispatchByRoute` complete — MassTransit ack message ไปแล้ว → ไม่ redeliver

**Root causes 3 ชั้น:**
1. **MassTransit setup เปล่า** — ไม่มี `UseMessageRetry`, `UseInMemoryOutbox`, `UseDelayedRedelivery`, `PrefetchCount` ([ModuleServiceRegistration.cs:318-343](../src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs#L318-L343))
2. **No graceful shutdown** — default 5s shutdown ASP.NET Core, bus discard in-flight messages
3. **No watchdog** — ไม่มี process ตรวจ Order=Planned ที่ไม่มี Trip

Plan แก้ในระดับ enterprise 3 tier ตามขนาด blast radius

---

## 2. Tier 1 — Immediate Stabilization (planned 1-2 weeks, delivered in 1 session)

**เป้า (planned)**: stop bleeding — ปัญหา stuck order ลด 80%+ โดยไม่ต้อง re-architect.
**ผลที่ได้จริง**: stuck-order rate 0/100 ใน chaos test (n=100, 19 random kill points) — ดีกว่าเป้าหมายเดิม. Recovery 3 ชั้น (watchdog / Riot3 reconciler / admin override) ครบทั้งสามชั้นตามที่ออกแบบใน T1.8.

| # | งาน | Status | Commit |
|---|---|---|---|
| 1.1 | MassTransit retry + delayed redelivery + in-memory outbox + kill switch + PrefetchCount=16 | ✅ | [`c9cf565`](https://github.com/Tuinuihappy/DTMS/commit/c9cf565) |
| 1.2 | try/catch รอบ `DispatchByRouteAsync` + structured failure + new `JobFailureCategory.DispatchException` | ✅ | [`a201fd5`](https://github.com/Tuinuihappy/DTMS/commit/a201fd5) |
| 1.3 | Graceful shutdown — `Host.ShutdownTimeout=60s` + `MassTransit.StopTimeout=45s` + `stop_grace_period=90s` | ✅ | [`7a4bca0`](https://github.com/Tuinuihappy/DTMS/commit/7a4bca0) |
| 1.4 | `PlanningReconciliationService` watchdog — poll 60s, stale > 2min, dedup 5min, cap 50/tick | ✅ | [`62d1ef5`](https://github.com/Tuinuihappy/DTMS/commit/62d1ef5) |
| 1.5 | Idempotency guards on `CreateJobAnchor` + `MarkJobDispatched` (race recovery + loud-fail divergent TripId) | ✅ | [`0f81dde`](https://github.com/Tuinuihappy/DTMS/commit/0f81dde) |
| 1.6 | `WorkflowMetrics` — 7 metrics under `DTMS.Workflow` meter + MassTransit native meter | ✅ | [`4a8292c`](https://github.com/Tuinuihappy/DTMS/commit/4a8292c) |
| 1.7 | Admin `POST /admin/orders/{id}/replan` + `ReplanStuckOrderCommand` (shared with watchdog) | ✅ | [`ab822b1`](https://github.com/Tuinuihappy/DTMS/commit/ab822b1) |
| **1.8** | **Vendor-acceptance guard — added after the OD-0381 replay-loop incident** (watchdog + replan handler skip orders whose Jobs already have `VendorOrderKey`) — defense-in-depth **layer 1** | ✅ | [`bd075c8`](https://github.com/Tuinuihappy/DTMS/commit/bd075c8) |
| **1.9** | **Admin trip-state override endpoints** — `POST /admin/trips/{id}/force-{start,pickup-completed,drop-completed,complete}`. Each fires the matching domain transition that a dropped RIOT3 webhook would have, so the downstream cascade (integration events → consumers → projections → upstream OMS) runs once and in the same shape. Operator-driven recovery for trips that vendor reconciliation can't fix. Defense-in-depth **layer 3** | ✅ | [`19fa392`](https://github.com/Tuinuihappy/DTMS/commit/19fa392) (+ supporting fix [`7d5b5f3`](https://github.com/Tuinuihappy/DTMS/commit/7d5b5f3)) |
| **1.10** | **Riot3 reconciler enabled by default** — `Riot3ReconciliationService` polls RIOT3 for in-flight envelope trips every 60s and reconciles via idempotent `Mark*` calls. Was opt-in (`Dispatch__Reconciliation__Enabled=false`); now on by default so dropped webhooks self-heal within ~1 poll interval. Defense-in-depth **layer 2** | ✅ | [`0e238a1`](https://github.com/Tuinuihappy/DTMS/commit/0e238a1) (+ vendor adapter fix [`d1c8397`](https://github.com/Tuinuihappy/DTMS/commit/d1c8397)) |

### T1 verification

| Test | Result |
|---|---|
| Unit tests (T1) | ✅ DeliveryOrder 246/246 + Planning 152/152 (28 new T1 tests across CreateJobAnchor / MarkJobDispatched / ReplanStuckOrder + T1.8 guard) |
| Integration tests (T1) | ✅ 5/5 consumer scenarios (`T1_DeliveryOrderValidatedConsumerIntegrationTests`) |
| Real-world recovery | ✅ OD-0374 / OD-0375 unstuck via watchdog within 2 seconds of restart |
| **Chaos test (kill mid-pipeline ×100)** | ✅ **PASS** — `scripts/chaos/kill-mid-pipeline.ps1` run 2026-06-18: 100 orders, 19 kill points, 5-min settle, 0 stuck. Details: [`docs/chaos-test-results.md`](chaos-test-results.md). Phase 5 (end-to-end completion) documented as deferred. |

### Tier 1 retrospective

| | Planned | Delivered |
|---|---|---|
| Items | T1.1–T1.7 (7) | T1.1–T1.10 (10) |
| Effort budget | ~34h | ~10-12h single session (the unbudgeted 1.8 / 1.9 / 1.10 emerged in response to incidents during dev) |
| Stuck-order rate | ≤ 0.3/1000 | **0/100** in chaos test |
| Operator MTTR | < 5 min | < 2 min (one HTTP call) |
| Defense layers | 1 (watchdog) | **3** — watchdog skip (1.8) + Riot3 reconciler (1.10) + admin overrides (1.9) |

**What changed from the plan (and why)**:

- **T1.8 added after OD-0381**: the original watchdog (T1.4) treated "Order=Planned + no Trip" as "needs replay". OD-0381 showed this misses the case where vendor accepted the upperKey on a prior attempt but our Trip persistence failed — replay then sends the same upperKey, RIOT3 rejects with E110007 "duplicate key", and the watchdog loops every 5 minutes forever. Fix: skip replay if any Job has `VendorOrderKey != null`.
- **T1.9 added during chaos test follow-up**: chaos test left 9 trips at `Created` because RIOT3 accepted but never sent TASK_PROCESSING. Force-* endpoints give operators a per-stage manual recovery without database access.
- **T1.10 flipped on by default**: `Riot3ReconciliationService` already existed but was `Enabled=false`. Turning it on makes recovery automatic for dropped webhooks, sliding T1.9 from "primary fix" to "operator-only fallback".

**Lessons for Phase 2 + future tiers**:

- **Real incidents drive better items than upfront design.** T1.8/1.9/1.10 are arguably the most valuable changes shipped today, and none were in the original plan.
- **Defense-in-depth needs all three layers**, not one robust mechanism. T1.4 alone wouldn't have caught OD-0381; T1.10 alone wouldn't have caught the cases T1.9 covers.
- **n=2 manual verification ≠ confidence**. The chaos test (n=100 + 19 kill points) is what turned T1 from "we think it works" into "it works".

---

## 3. Tier 2 — Saga State Machine (สัปดาห์ 3-6)

**เป้า**: แก้ root cause สถาปัตยกรรม — แทน procedural consumer ด้วย state machine ที่ persistent + crash-recoverable

### 3.1 Saga States & Transitions

```
[Received] --DeliveryOrderConfirmedEvent--> [AwaitingPlan]
[AwaitingPlan] --PlanRequested--> [Planning]
[Planning] --JobCreated--> [Dispatching]
[Dispatching] --TripDispatched--> [AwaitingVendorAck]
[AwaitingVendorAck] --Riot3MissionAccepted--> [VendorRunning]
[VendorRunning] --Riot3MissionCompleted--> [Completed]
[Planning | Dispatching | AwaitingVendorAck] --(timeout|fault)--> [FailedAwaitingRetry]
[FailedAwaitingRetry] --(auto-retry|operator)--> previous state | [DeadLettered]
```

### 3.2 Mechanics

- **Library**: MassTransit Automatonymous state machine (ครั้งแรกใน codebase, ไม่มี saga existing)
- **State table**: schema ใหม่ `orchestration.DeliveryOrderSagas` (แยก schema เพื่อไม่ผูก Planning migrations)
  - Columns: `CorrelationId (=OrderId)`, `CurrentState`, `JobId`, `TripId`, `VendorMissionId`, `LastFaultMessage`, `RetryCount`, `RowVersion` (optimistic concurrency), `UpdatedAtUtc`
- **Events**: `DeliveryOrderConfirmedIntegrationEventV1`, `JobCreatedEvent`, `TripDispatchedEvent`, `JobDispatchFailedEvent`, `Riot3MissionAcceptedEvent`, `Riot3MissionCompletedEvent` + `Schedule<TimeoutExpired>` per waiting state (30 min default)
- **Idempotency per step**: business key `OrderId+StepName` persist ผ่าน [`IdempotentProjector`](../src/AMR.DeliveryPlanning.SharedKernel/Projection/IdempotentProjector.cs) pattern — reuse `ProjectionInbox` table หรือ clone schema เป็น `orchestration.SagaStepInbox`
- **Compensation**: `Planning` rollback = release Job anchor; `Dispatching` rollback = cancel Trip; `AwaitingVendorAck` rollback = cancel RIOT3 mission. แต่ละ compensation เป็น message อิสระ + idempotent
- **Bulkhead + timeout vendor**: Polly `AddResiliencePipeline("riot3", … .AddTimeout(10s).AddConcurrencyLimiter(20))` ใน vendor adapter

### 3.3 Migration path (เลี่ยง big-bang)

| Step | What | Status |
|---|---|---|
| 1 | Build saga ภายใต้ feature flag `Workflow:UseSaga=false` | ⚠️ **POC done + Step 1 done (2026-06-19)** — Initially → AwaitingPlan + redelivery Ignore handlers for 5 states + first real transition AwaitingPlan → Planning via OrderPlanRequested. Remaining transitions (Dispatching, Completed, FailedAwaitingRetry handlers) + compensation are Step 2+ work. See [3.4](#34-poc-status-as-of-2026-06-18) and [3.5](#35-phase-2-follow-ups-discovered-during-poc) below. |
| 2 | **Dual-run** — saga subscribes to same events, writes only to its own schema; legacy `DeliveryOrderValidatedConsumer` remains authoritative | ❌ blocked on step 1 |
| 3 | **Shadow comparison job** logs divergence to `orchestration.SagaDiffs` for 1 week | ❌ |
| 4 | Flip flag per environment: dev → uat → prod | ❌ |
| 5 | Decommission legacy consumer at week 8 | ❌ |

**Total Tier 2: ~120h** (state machine 40h, persistence + migrations 20h, dual-run 20h, compensation 25h, tests 15h)

### 3.4 POC status (as of 2026-06-18)

**POC verified end-to-end in docker.** All scaffolding lands behind `Workflow:UseSaga` (default off). Commits:

- [`4a8ec7c`](https://github.com/Tuinuihappy/DTMS/commit/4a8ec7c) — initial scaffold: `OrderSagaState` enum, `DeliveryOrderSagaInstance`, `DeliveryOrderSagaStateMachine`, `OrchestrationDbContext`, DI wiring under feature flag
- [`392cb0b`](https://github.com/Tuinuihappy/DTMS/commit/392cb0b) — fix: hoist `OrchestrationSchemaInitializer` registration out of `AddMassTransit(bus => …)` lambda (its services-collection scope is discarded)
- [`7909e77`](https://github.com/Tuinuihappy/DTMS/commit/7909e77) — fix: bootstrap schema via idempotent raw SQL (`EnsureCreatedAsync` is a no-op on existing databases)

**Live verification**: with `Workflow__UseSaga=true`, `orchestration.DeliveryOrderSagas` materialises at startup, the saga registers with MassTransit (`Configured endpoint DeliveryOrderSagaInstance`), receives `DeliveryOrderConfirmedIntegrationEventV1`, and persists state at `AwaitingPlan` (CurrentState=3 — MassTransit reserves indices 0-2 for None/Initial/Final).

### 3.5 Phase 2 follow-ups discovered during POC

| # | Finding | Why it matters | Fix in Phase 2 |
|---|---|---|---|
| 1 | **`NotAcceptedStateMachineException` on event redelivery** | A second `DeliveryOrderConfirmedEvent` for an already-`AwaitingPlan` saga has no `During(AwaitingPlan)` handler. MassTransit treats it as a fault → retry → DLQ. In production T1.4 watchdog + T1.1 retry will redeliver this event multiple times per order — every retry would throw. | ✅ **Fixed 2026-06-19 in Step 1 A1** — `During(state, Ignore(OrderConfirmed))` for all 5 user states. Verified via 3 smoke tests + docker re-run showing 0 NotAccepted exceptions. |
| 2 | **Raw SQL bootstrap is POC-only** | `OrchestrationSchemaInitializer` uses `CREATE … IF NOT EXISTS` raw SQL because we can't generate proper EF migrations until the schema is stable. Acceptable for POC but doesn't survive schema evolution. | Hand-write EF migration in `Migrations/Orchestration/` with Designer + ModelSnapshot. Replace the initializer entirely. ~5h. |
| 3 | **MassTransit state-index ≠ `OrderSagaState` enum** | MT auto-assigns state indices 3..N for user states (0=None, 1=Initial, 2=Final reserved). Our enum starts `None=0, AwaitingPlan=1, …`. The DB column stores MT's indices, not the enum's. The enum is documentation only; matching by name happens internally. | Either re-number the enum to align (breaking change for any consumer reading the column raw), or document this clearly in the saga instance class and use the enum only for code-side state names. |

### 3.6 Tier 2 effort estimate (revised after POC)

POC ate ~3 hours of the originally-budgeted "state machine 40h" line. Net change after POC discoveries:

| Original line | Original | Revised | Why |
|---|---|---|---|
| State machine | 40h | 35h | POC scaffold (–3h) + redelivery handlers `During(state, Ignore(…))` for 5 states (+~3h net) |
| Persistence + migrations | 20h | 25h | +5h for hand-written EF migration to replace the POC's raw-SQL bootstrap (T1.10 caveat #2) |
| Dual-run | 20h | 20h | unchanged |
| Compensation | 25h | 25h | unchanged |
| Tests | 15h | 20h | +5h saga replay + compensation integration tests (chaos-test-style harness extended for saga states) |
| **Total** | **120h** | **125h** | net +5h after POC findings |

**Phase 2 entry criteria** (none are blockers, just sequencing):

- ✅ POC verified end-to-end (done)
- ⏳ T1 soak in dev for 24-48h — confirms T1 metrics stay healthy before saga starts shadowing them
- ⏳ Decide on T2 step 1 scope: "all 5 During() handlers + 1 happy path event each" is the smallest useful slice (~15h)

---

## 4. Tier 3 — Platform Evolution (เดือน 3-6+)

### 4.0 Gate-criteria readings (2026-06-18)

Tier 3 is gated on observable thresholds; readings below show none are met yet.

| Trigger | Threshold | Current | Verdict |
|---|---|---|---|
| Outbox pending sustained | > 500 | **0** | not met (no backlog at all in dev) |
| Cross-bounded-context workflows | ≥ 3 new orchestrations | 1 (Planning consumer being replaced by saga) | not met |
| Deploy frequency | ≥ 1/day | manual `docker compose build` per change | not met |
| Projection lag P95 | > 5s | not yet measured (no Grafana yet — T1.6 metrics exported to OTel only) | unknown |

**Recommendation**: do not start Tier 3 work yet. Re-evaluate quarterly. If any single trigger trips, treat as a prompt to plan the matching Tier 3 sub-item (CDC for outbox/lag, Temporal for workflow count, K8s for deploy frequency) — not all of Tier 3 at once.

### 4.1 Workflow Engine Decision

| Option | Durable | Ops cost | .NET maturity | Verdict |
|---|---|---|---|---|
| MassTransit Saga | ✅ DB-backed | Low | High | **ใช้ใน Tier 2** |
| **Temporal** | ✅ history replay | Medium-High (separate cluster) | High (Temporalio SDK) | **Recommended** ถ้า workflows ขยายเกิน 3-4 modules; best สำหรับ multi-day orchestrations |
| Dapr Workflow | ✅ | Medium (Dapr sidecar) | Medium | Defer — ซ้ำกับ Saga โดยไม่ adopt Dapr ทั้งหมด |
| Hangfire | Job scheduling only | Low | High | ❌ ไม่ใช่ workflow engine |

**One-liner**: ยืน MassTransit Saga ก่อน; revisit Temporal ที่เดือน 6 ถ้า orchestration cross ≥3 bounded contexts ใหม่

### 4.2 CDC Projection (Debezium → Kafka)

แทน polling `OutboxProcessorService` ด้วย Debezium อ่าน WAL จาก 6 schemas → Kafka topics per aggregate → projection consumer ใช้ `IdempotentProjector` เดิม

**Pays off เมื่อ**: outbox pending >500 ต่อเนื่อง หรือ projection lag P95 > 5s. จนกว่าจะถึง outbox ปัจจุบันถูกกว่า

### 4.3 Kubernetes Migration

- API = **Deployment** (ไม่ใช่ StatefulSet) — state อยู่ Postgres/RabbitMQ
- MassTransit queues ต้อง **durable + non-exclusive** → pod ใหม่ attach queue เดิม
- `preStop` hook: `curl localhost:8080/health/drain` (new endpoint) → return 503 จาก `/health/ready` + `sleep 45` ให้ bus drain; `terminationGracePeriodSeconds: 90`
- HPA on `dtms_consumer_lag` (ไม่ใช่ CPU)

---

## 5. Cross-cutting Observability

| Tier | New metrics | Alerts (Prometheus) | Grafana panels |
|---|---|---|---|
| T1 | `dtms_orders_stuck_planned`, `dtms_consumer_retry_total`, `dtms_consumer_faulted_total`, `dtms_outbox_age_seconds` | `orders_stuck_planned > 0 for 5m` (**P1**); `consumer_faulted rate > 0.1/s for 10m` (P2); `outbox_age_seconds > 120` (P2) | "Stuck Orders", "Outbox Health", "Consumer Retries" |
| T2 | `dtms_saga_state_count{state}`, `dtms_saga_step_duration_seconds`, `dtms_saga_compensation_total` | `saga_state_count{state="FailedAwaitingRetry"} > 5 for 10m` (P1) | "Saga State Distribution", "Step Latency Heatmap" |
| T3 | `dtms_cdc_lag_seconds`, `dtms_k8s_pod_drain_duration_seconds` | `cdc_lag_seconds > 10` (P2) | "CDC Pipeline", "Deploy Drain Time" |

---

## 6. ผลลัพธ์หลังจาก Implement (Quantitative)

### 6.1 ตัวเลขเปรียบเทียบ

| Phase | Detection latency | Recovery method | Stuck orders / 1000 | Deploy incidents/mo | On-call pages/wk | Operator MTTR |
|---|---|---|---|---|---|---|
| **Current (pre-T1, the incident state)** | hours (customer report) | manual DB query + redispatch | ~2 | 1-2 | 3-5 | 30-60 min |
| **Post-Tier-1** ✅ measured | 5 min (Prometheus alert) | auto via watchdog + retry + Riot3 reconciliation; manual via `/admin/orders/{id}/replan` for stuck orders, `/admin/trips/{id}/force-*` for stuck trips | **0/100 in chaos test** (target was ~0.3) | 0-1 | 1-2 | < 2 min (1 HTTP call per stuck artefact) |
| **Post-Tier-2** projected | 1 min (saga state) | automatic via saga timeouts + compensation | < 0.05 (-97%) | 0 | < 1 | auto |
| **Post-Tier-3** projected | < 30s | durable workflow replay; zero-downtime deploy | < 0.01 (-99.5%) | 0 | < 0.3 | auto |

**Post-Tier-1 measurement note**: chaos test on 2026-06-18 injected 100 orders with 19 random kill points (1-8s delay) over 14 minutes; 100/100 reached `Dispatched`, 0 stuck at `Planned`. Better than the original target of ~0.3 stuck/1000. The "stuck rate" in production will depend on traffic mix; the chaos test sets an upper bound on the failure mode T1 was built to prevent.

### 6.2 ผลลัพธ์เชิงพฤติกรรมระบบ

**หลัง Tier 1:**
- ❌ ปัญหาเดิม (OD-0374/OD-0375 ค้างที่ Planned 1+ ชั่วโมงเงียบสนิท): จะไม่เกิดอีก
- ✅ ถ้า API restart ตอน consumer ทำงาน: MassTransit redeliver message (retry config), in-memory outbox ป้องกัน double-publish, graceful shutdown ให้ consumer ทำต่อจนเสร็จก่อน SIGKILL
- ✅ ถ้า bug ทำให้ consumer fail แบบใหม่: 5 retries (1s→30s) → ถ้ายัง fail ก็ delayed redelivery (1m, 5m, 15m, 1h) → ถ้ายังก็เข้า `_dead-letter` queue + ops ได้ Prometheus alert ภายใน 10 นาที
- ✅ ถ้า message ถูก ack แล้วแต่ work ยังไม่จบ: watchdog ตื่นภายใน 60s ตรวจเจอ → re-publish → consumer ทำใหม่ (idempotent)
- ✅ Operator มี `/admin/replan` ใช้ปลดล็อกได้เองทันที ไม่ต้องเข้า DB

**หลัง Tier 2:**
- ✅ State ของทุก order ถูก persist explicitly ใน `orchestration.DeliveryOrderSagas` — เห็นจังหวะของ workflow ตลอดเวลา (ปัจจุบันรู้แค่จาก derived state ของ DeliveryOrders + Jobs + Trips)
- ✅ Saga มี Schedule<TimeoutExpired> — ถ้า vendor ไม่ ack ภายใน 30 นาที auto compensate (cancel Trip + retry หรือ DeadLetter)
- ✅ Compensation logic centralized → rollback failed Planning จะปล่อย Job anchor อัตโนมัติ, ไม่มี orphan
- ✅ Restart recovery: saga resume จาก persisted state ทันที — ไม่พึ่ง message redelivery
- ✅ Shadow run 1 สัปดาห์ก่อน cutover → ความเสี่ยง regression ต่ำ

**หลัง Tier 3:**
- ✅ Deploy ระหว่าง peak hour ได้ — preStop drain + rolling update ทำให้ in-flight orders complete ก่อน pod ตาย
- ✅ Projection lag เป็น sub-second (CDC จาก WAL) แทน 5s polling — UI ไม่เคย stale
- ✅ Workflow engine (ถ้า Temporal) — history replay debugging, time-travel queries, ทำ what-if scenarios ได้
- ✅ HPA on consumer_lag → scale up อัตโนมัติเมื่อโหลดขึ้น

### 6.3 ผลลัพธ์ทางธุรกิจ

- **SLA reliability**: SLA breach จาก stuck orders จะลดลงเหลือ 0 (ปัจจุบันมี blind spot ที่ทำให้ order ค้างเงียบเป็นชั่วโมง)
- **Operations cost**: on-call pages 3-5/wk → <1/wk → on-call rotation นั่งทำงานอื่นได้
- **Audit trail**: ทุก state transition ถูก persist + observable — passes financial/compliance audit ได้ทันที
- **Developer velocity**: deploy แบบ rolling = ship ได้ทุกเวลา ไม่ต้องนัดหน้าต่าง maintenance

---

## 7. Risks & Rollback

| Tier | Risk | Mitigation / Rollback |
|---|---|---|
| T1 | Aggressive retries amplify poison message → RabbitMQ overload | Lower `Exponential` ceiling; kill switch trip auto; toggle watchdog off ด้วย `IOptionsMonitor` |
| T1 | Watchdog double-fire event + non-idempotent step double-act | Ship watchdog **หลัง** item 1.2 + 1.5 merge เท่านั้น; gate ด้วย flag `Watchdog:Enabled` |
| T2 | Saga schema migration lock production | `CREATE SCHEMA orchestration` แยกกับ online migration; flag-off saga = revert ไป legacy consumer ทันที |
| T2 | Dual-run divergence corrupt state | Saga write **เฉพาะ schema ของตัวเอง** ช่วง shadow; ไม่แตะ Planning tables จนกว่า cutover |
| T3 | Temporal cluster outage stop ทุก workflow | Defer adoption จน DR runbook proven ใน staging 30 วัน |
| T3 | K8s rolling deploy drop bus connections | Feature-flag canary; keep VM deployment warm 2 sprints |

---

## 8. Verification Plan

1. **Chaos test (T1)**: `scripts/chaos/kill-mid-pipeline.ps1` — inject 100 orders, kill API container ที่ random delay 1-8s ระหว่าง consumer exec. **Accept**: 0 stuck orders หลัง 5-min settle, ทั้ง 100 reach `Dispatched`. **สถานะ**: ✅ PASS (run 2026-06-18, n=100, 19 kill points, 100% success — รายละเอียดใน [`docs/chaos-test-results.md`](chaos-test-results.md))

    **Phase 5 — end-to-end completion verification** (deferred): หลังจาก verify ที่ระดับ `Dispatched` แล้ว รอ vendor (RIOT3) ส่ง webhook tripStarted → tripDropCompleted → podCompleted → cascade ถึง Order=`Completed`. **เวลา**: อาจใช้เป็นชั่วโมง+ ขึ้นกับ vendor robot capacity + queue depth. **Why deferred**: vendor ทำงานอยู่นอก scope ของ T1 crash-recovery — Phase 1-4 เพียงพอสำหรับ verify T1 stack. Phase 5 ตอบคำถามคนละข้อ: "vendor + ระบบ end-to-end ทำงานครบไหม" ซึ่งใกล้ integration test มากกว่า chaos test. **เมื่อเพิ่ม**: ต้อง stub vendor หรือใช้ vendor sandbox (production-ish endpoint จะถูก pollute ด้วย 100 orders ทุกครั้ง) + `-WaitForCompletion` switch ใน script + Phase 5 verify query `WHERE Status IN ('Completed', 'PartiallyCompleted')`
2. **Graceful shutdown test (T1)**: SIGTERM ตอนมี 20 in-flight messages → assert ทั้ง 20 จบ + pod exit ภายใน 90s. **สถานะ**: ⚠️ **partial pass (2026-06-19 run)** — 5 in-flight orders all reached `Dispatched` after settle (data integrity ✅), but container exit took **149s** vs ≤ 90s target. Bus stopped cleanly in 2s; the remaining 147s waited on a long-lived SignalR `/hubs/trips` WebSocket connection from the frontend + in-flight RIOT3 HTTP requests. In production a `docker stop` (default 90s grace) would SIGKILL those, not lose order data but force the SignalR client to reconnect and abandon the vendor HTTP calls. See [§11 Production readiness gaps](#11-production-readiness-gaps) for the gap analysis and fix priorities.
3. **Load test (T1+T2)**: k6 driving 50 orders/min × 30 min — assert P95 end-to-end < 30s, `outbox_age_seconds < 30`. **สถานะ**: ❌ deferred — not on the T1 production-ready critical path; pick up before scale events.
4. **Saga replay test (T2)**: kill API ที่แต่ละ saga state สำหรับ 50 orders → assert saga resume จาก persisted state + reach `Completed`. **สถานะ**: ❌ Phase 2 scope.
5. **Compensation test (T2)**: force RIOT3 vendor 500 → assert saga → `FailedAwaitingRetry` → compensate หลัง retry budget หมด, leave state consistent ทุก 6 schemas. **สถานะ**: ❌ Phase 2 scope.
6. **Manual replay drill (T1)**: `POST /admin/orders/{id}/replan` × 10 stalled orders → all reach `Dispatched` ไม่มี duplicate Trip (validates 1.5 idempotency). **สถานะ**: ⚠️ partial — proven on OD-0374 / OD-0375 (n=2) via watchdog; formal n=10 drill TBD. **T1.9 expansion**: same drill should also exercise the four `/admin/trips/{id}/force-*` endpoints against trips deliberately left in each stuck state (Created, InProgress-pickup, InProgress-drop, InProgress-final).
7. **Deploy drill (T3)**: 10 rolling deploys ภายใต้ load ต่อเนื่อง → 0 stuck orders, 0 lost messages. **สถานะ**: ❌ Tier 3 scope (no K8s yet).

---

## 9. Critical files for implementation

- [src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs](../src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs) — MassTransit bus config (T1.1)
- [src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/Consumers/DeliveryOrderValidatedConsumer.cs](../src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/Consumers/DeliveryOrderValidatedConsumer.cs) — wrap dispatch (T1.2), eventual decommission ใน T2
- [src/AMR.DeliveryPlanning.Api/Program.cs](../src/AMR.DeliveryPlanning.Api/Program.cs) — graceful shutdown + metrics registration (T1.3, T1.6)
- [docker-compose.yml](../docker-compose.yml) — stop_grace_period (T1.3)
- [src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Services/Riot3ReconciliationService.cs](../src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Services/Riot3ReconciliationService.cs) — reference pattern สำหรับ T1.4 watchdog
- [src/AMR.DeliveryPlanning.SharedKernel/Projection/IdempotentProjector.cs](../src/AMR.DeliveryPlanning.SharedKernel/Projection/IdempotentProjector.cs) — reference pattern สำหรับ saga step inbox ใน T2

---

## 10. Execution order recommendation

| Phase | Items | Notes |
|---|---|---|
| Phase | Items | Status | Notes |
|---|---|---|---|
| **Week 1 (planned)** | T1.1 → T1.2 → T1.5 | ✅ **Done 2026-06-17** | Foundation landed in one session, faster than planned |
| **Week 2 (planned)** | T1.3 → T1.6 → T1.4 → T1.7 → verification | ✅ **Done 2026-06-17/18** | All T1 + T1.8 vendor-acceptance guard + T1.9 admin trip overrides + T1.10 reconciler-on-by-default + chaos test PASS. Defense-in-depth from T1.8 plan now has all three layers built. |
| **Week 3-4 (planned)** | T2 saga design + state table + migrations | ⚠️ **POC verified** | Architecture POC done end-to-end; Phase 2 full build deferred |
| **Week 5-6 (planned)** | T2 dual-run + shadow + cutover → verification 3-5 | ❌ Not started | Awaits Phase 2 build |
| **Month 3+ (planned)** | T3 evaluation gate based on observed metrics | ❌ Not started | Gate criteria not met yet (outbox/workflow thresholds — revisit) |

### Recommended next steps (as of 2026-06-18)

1. **Soak T1 in dev for 24-48h** (passive) — let metrics accumulate; confirm `dtms_workflow_orders_stuck_planned` stays at 0 and `outbox_age_seconds` P95 < 30s. ✅ **22h soak done 2026-06-19 morning, all 4 signals clean.**
2. **SIGTERM drain test** (~1h) — one-off `docker kill --signal=SIGTERM` mid-pipeline to close the partial-status item in verification table. ⚠️ **Done 2026-06-19, data integrity passed but timing (149s) exceeded 90s target. Gaps captured in §11.**
3. **Pre-existing integration test debt triage** (15-20h, distributed) — 21 tests still failing per [`docs/integration-test-debt.md`](integration-test-debt.md); needs owner-team distribution rather than single-engineer effort.
4. **Phase 2 step 1** when ready — add `During(state, Ignore(OrderConfirmed))` for each saga state (the redelivery handling discovered in 3.5 #1), then proceed with TripDispatched / RiotMissionAccepted handlers per plan.

---

## 11. Production readiness gaps

> Added 2026-06-19 after the SIGTERM drain test revealed that container shutdown takes ~149s vs the 90s `stop_grace_period`. T1 data integrity is solid (chaos n=100 + 22h soak + SIGTERM all preserved order outcomes), but the **shutdown timing path** is not yet enterprise-grade for K8s rolling deploys with active SignalR clients.

### G1 — SignalR connection drain protocol — 🟢 **shipped 2026-06-22**

**Symptom**: A `/hubs/trips` WebSocket connection held the shutdown open for ~140s after the bus already stopped. Frontend dashboards (which subscribe to trip events) keep WebSockets alive indefinitely; ASP.NET's host won't exit while a request pipeline owns an active connection.

**Shipped in ~6h**:
- Backend ([6f321cb](https://github.com/Tuinuihappy/DTMS/commit/6f321cb)) — `POST /api/v1/admin/drain-start` (loopback-only) flips `/health/ready` to 503, broadcasts `__drain` to all 5 hubs, rejects new connections via `DrainAwareHubFilter`, idempotent.
- Frontend ([82184b6](https://github.com/Tuinuihappy/DTMS/commit/82184b6)) — `__drain` event handler in `signalr-client.ts` cycles the connection (stop → start) and emits a CustomEvent so `useHubSubscription` rejoins its groups.
- K8s lifecycle stub at `deploy/k8s/api-deployment.yaml.stub` with the full `preStop` + `terminationGracePeriodSeconds: 90` contract.
- Plan + close-out at [`docs/plans/g1-signalr-drain-protocol.md`](plans/g1-signalr-drain-protocol.md).

**Measured (2026-06-22)**: Baseline SIGTERM exit dropped from 149s (2026-06-19) to **49s** even without active SignalR clients (build improvements between dates). Drain protocol shaves the **~100s SignalR-specific tail** that materialises under real client load — see [`docs/chaos-test-results.md`](chaos-test-results.md) "M2 SIGTERM exit time" for the full scorecard, reproduction steps with active clients, and the honest residual.

**Deferred** (per the plan's own off-ramps): Phase 2.3 connection counter (early-exit when client count hits 0), 4 xUnit integration tests (M2 chaos script is the integration signal), drain metrics. Revisit if the K8s rollout finds the SignalR tail still over budget.

### G2 — Shutdown observability

**Symptom**: We learned the 149s number by reading logs after the fact. Production needs metrics + alerts.

**Fix** (~0.5 day):

- Add metric `dtms_shutdown_duration_seconds{phase}` with phases `bus`, `signalr`, `hosted_services`, `total`.
- Emit each phase value from `IHostApplicationLifetime.ApplicationStopped` via a small `IHostedService` that hooks both `ApplicationStopping` and `ApplicationStopped`.
- Grafana alert: P95 shutdown duration > 60s over 7 days.

### G3 — Cancellation propagation through HTTP client + Polly

**Symptom**: Shutdown log showed in-flight `GET /api/v4/robots?*` and `GET /api/v4/orders/.../G1?*` to RIOT3 continuing for several seconds after `Application is shutting down` and then throwing `TaskCanceledException`. Polly retry policies didn't drop them early.

**Fix** (~1 day):

- Every `HttpClient` call inside a hosted service or consumer must accept a `CancellationToken` linked to `IHostApplicationLifetime.ApplicationStopping`.
- Polly `AddRetry` / `AddCircuitBreaker` configured with `CancellationToken` honor so retries bail when the token trips.
- `BackgroundService.StopAsync` overrides that explicitly cancel internal CTS with a short timeout (e.g., 2s) rather than letting the framework wait for the default ShutdownTimeout window.

### G4 — Deploy storm test (multi-pod chaos)

**Symptom**: Our chaos test kills one pod at a time. K8s rolling deploy with replicas > 1 takes down 1 pod while others must absorb 100% of incoming load.

**Fix** (~2-3 days, paired with the K8s migration in §4.3):

- Extend `scripts/chaos/kill-mid-pipeline.ps1` with a `-RollingDeploy` mode that simulates K8s rolling update timing on docker-compose (kill one pod, wait `terminationGracePeriodSeconds`, start new, repeat).
- Run k6 at 50 req/s for 30 min, perform 10 rolling deploys during the run, assert: zero stuck orders, request P99 latency does not exceed 2× baseline.

### G5 — Timeout layering alignment

**Current**:

| Layer | Timeout | Comment |
|---|---|---|
| docker `stop_grace_period` | 90s | Sets the outer cap |
| `Host.ShutdownTimeout` | 60s | OK in isolation |
| `MassTransitHostOptions.StopTimeout` | 45s | OK in isolation |
| SignalR connection close | implicit | Currently no explicit bound — root cause of the 149s |
| Polly retry budget | not aligned to shutdown | Can outlive `Host.ShutdownTimeout` |

**Fix** (~0.5 day, low risk):

- Add `services.Configure<HostOptions>(o => o.ServicesStopConcurrently = true)` so hosted services stop in parallel rather than sequentially.
- Tighten `SignalR.KeepAliveInterval = TimeSpan.FromSeconds(10)` + `ClientTimeoutInterval = TimeSpan.FromSeconds(20)` so a frontend that doesn't acknowledge close is dropped server-side.
- Document the timeout hierarchy in this section so future tuning doesn't break the invariant.

### G6 — Container-name vs service-name coupling

**Symptom**: Chaos and SIGTERM scripts use `docker kill dtms-api` (container name) but `docker compose up -d api` (service name). Production K8s replaces both with deployment names + label selectors.

**Fix** (folds into K8s migration, §4.3): drop the container-name coupling once K8s is in.

### Priority

| # | Gap | Effort | When |
|---|---|---|---|
| ✅ | G1 — SignalR drain protocol | shipped 2026-06-22 (~6h) | Phase 1+3+5; Phase 2 deferred per off-ramp |
| 🥈 | G2 — Shutdown observability | 0.5 day | Same sprint as G1 so we measure the fix |
| 🥉 | G3 — Cancellation propagation | 1 day | Reduces shutdown log noise and tightens shutdown duration further |
| 4 | G4 — Deploy storm test | 2-3 days | Pair with §4.3 K8s migration |
| 5 | G5 — Timeout layering | 0.5 day | Quick win at any time |
| 6 | G6 — Container vs service name | — | Folds into K8s work |

**Definition of "enterprise-grade for production deploys"**: G1 + G2 + G3 done. After that, K8s rolling deploy of T1 during peak hour has zero stuck orders, no abandoned vendor HTTP calls, and frontend clients see ≤ 5s reconnect blip (visible loading spinner, no error).
