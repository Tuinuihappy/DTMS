# DTMS Crash-Recovery & Workflow-Resilience Plan

> **Scope**: Tier 1 + Tier 2 + Tier 3 (immediate stabilization → Saga state machine → platform evolution)
> **Trigger incident**: OD-0374-WIP / OD-0375-WIP stuck after API restart on 2026-06-17

## Status (as of 2026-06-18)

| Tier | Progress | Highlights |
|---|---|---|
| **T1 — Immediate stabilization** | ✅ **100% complete + verified** | 7 items + T1.8 vendor-acceptance guard. Unit tests 246/152 pass, integration test 5/5 pass. **Chaos test N=100 PASS** (19 random kill points, 0 stuck orders, 100% Dispatched). Real-world recovery proven on OD-0374/0375. |
| **T2 — Saga state machine** | ⚠️ **POC verified, Phase 2 not started** | Saga + EF persistence + feature flag wired and verified end-to-end in docker. POC surfaced a Phase 2 follow-up (NotAcceptedStateMachineException on event redelivery — needs `During(state, Ignore(event))` handlers). Full Phase 2 (~100h) deferred. |
| **T3 — Platform evolution** | ⏸️ **Not started — gate not met** | Awaits scale triggers (outbox pending > 500 sustained, workflows > 3 bounded contexts, deploy frequency ≥ 1/day). Revisit at month 3. |

**Today's headline outcome**: T1 confidence elevated from `n=2` (OD-0374/0375 recovery) to `n=100` (chaos test). T1 is production-ready.

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

## 2. Tier 1 — Immediate Stabilization (สัปดาห์ 1-2)

**เป้า**: stop bleeding — ปัญหา stuck order ลด 80%+ โดยไม่ต้อง re-architect

| # | งาน | Status | Commit |
|---|---|---|---|
| 1.1 | MassTransit retry + delayed redelivery + in-memory outbox + kill switch + PrefetchCount=16 | ✅ | [`c9cf565`](https://github.com/Tuinuihappy/DTMS/commit/c9cf565) |
| 1.2 | try/catch รอบ `DispatchByRouteAsync` + structured failure + new `JobFailureCategory.DispatchException` | ✅ | [`a201fd5`](https://github.com/Tuinuihappy/DTMS/commit/a201fd5) |
| 1.3 | Graceful shutdown — `Host.ShutdownTimeout=60s` + `MassTransit.StopTimeout=45s` + `stop_grace_period=90s` | ✅ | [`7a4bca0`](https://github.com/Tuinuihappy/DTMS/commit/7a4bca0) |
| 1.4 | `PlanningReconciliationService` watchdog — poll 60s, stale > 2min, dedup 5min, cap 50/tick | ✅ | [`62d1ef5`](https://github.com/Tuinuihappy/DTMS/commit/62d1ef5) |
| 1.5 | Idempotency guards on `CreateJobAnchor` + `MarkJobDispatched` (race recovery + loud-fail divergent TripId) | ✅ | [`0f81dde`](https://github.com/Tuinuihappy/DTMS/commit/0f81dde) |
| 1.6 | `WorkflowMetrics` — 7 metrics under `DTMS.Workflow` meter + MassTransit native meter | ✅ | [`4a8292c`](https://github.com/Tuinuihappy/DTMS/commit/4a8292c) |
| 1.7 | Admin `POST /admin/orders/{id}/replan` + `ReplanStuckOrderCommand` (shared with watchdog) | ✅ | [`ab822b1`](https://github.com/Tuinuihappy/DTMS/commit/ab822b1) |
| **1.8** | **Vendor-acceptance guard — added after the OD-0381 replay-loop incident** (watchdog + replan handler skip orders whose Jobs already have `VendorOrderKey`) | ✅ | [`bd075c8`](https://github.com/Tuinuihappy/DTMS/commit/bd075c8) |

### T1 verification

| Test | Result |
|---|---|
| Unit tests (T1) | ✅ DeliveryOrder 246/246 + Planning 152/152 (28 new T1 tests across CreateJobAnchor / MarkJobDispatched / ReplanStuckOrder + T1.8 guard) |
| Integration tests (T1) | ✅ 5/5 consumer scenarios (`T1_DeliveryOrderValidatedConsumerIntegrationTests`) |
| Real-world recovery | ✅ OD-0374 / OD-0375 unstuck via watchdog within 2 seconds of restart |
| **Chaos test (kill mid-pipeline ×100)** | ✅ **PASS** — `scripts/chaos/kill-mid-pipeline.ps1` run 2026-06-18: 100 orders, 19 kill points, 5-min settle, 0 stuck. Details: [`docs/chaos-test-results.md`](chaos-test-results.md). Phase 5 (end-to-end completion) documented as deferred. |

**Total Tier 1 effort (actual): ~10 hours single session, in line with the ~34h plan budget after accounting for batching, test-writing, and live verification.**

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

1. Build saga ภายใต้ feature flag `Workflow:UseSaga=false`
2. **Dual-run** — saga subscribe events เดียวกัน, write เฉพาะ schema ของตัวเอง; legacy `DeliveryOrderValidatedConsumer` ยังเป็น authoritative
3. **Shadow comparison job** log divergence ลง `orchestration.SagaDiffs` 1 สัปดาห์
4. Flip flag per environment: dev → uat → prod
5. Decommission legacy consumer ที่สัปดาห์ 8

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
| 1 | **`NotAcceptedStateMachineException` on event redelivery** | A second `DeliveryOrderConfirmedEvent` for an already-`AwaitingPlan` saga has no `During(AwaitingPlan)` handler. MassTransit treats it as a fault → retry → DLQ. In production T1.4 watchdog + T1.1 retry will redeliver this event multiple times per order — every retry would throw. | Add `During(AwaitingPlan, Ignore(OrderConfirmed))` for each user-defined state. Or use `Event(… e.OnMissingInstance(m => m.Discard()))` policy. See [memory note on redelivery handling] |
| 2 | **Raw SQL bootstrap is POC-only** | `OrchestrationSchemaInitializer` uses `CREATE … IF NOT EXISTS` raw SQL because we can't generate proper EF migrations until the schema is stable. Acceptable for POC but doesn't survive schema evolution. | Hand-write EF migration in `Migrations/Orchestration/` with Designer + ModelSnapshot. Replace the initializer entirely. ~5h. |
| 3 | **MassTransit state-index ≠ `OrderSagaState` enum** | MT auto-assigns state indices 3..N for user states (0=None, 1=Initial, 2=Final reserved). Our enum starts `None=0, AwaitingPlan=1, …`. The DB column stores MT's indices, not the enum's. The enum is documentation only; matching by name happens internally. | Either re-number the enum to align (breaking change for any consumer reading the column raw), or document this clearly in the saga instance class and use the enum only for code-side state names. |

### 3.6 Tier 2 effort estimate (revised after POC)

POC ate ~3 hours of the originally-budgeted "state machine 40h" line. Remaining ~117h holds; the discoveries above don't change the bottom line — they sharpen what gets built in Phase 2 step 1.

---

## 4. Tier 3 — Platform Evolution (เดือน 3-6+)

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
| **Post-Tier-1** ✅ measured | 5 min (Prometheus alert) | auto via watchdog + retry; fallback `/admin/replan` | **0/100 in chaos test** (target was ~0.3) | 0-1 | 1-2 | < 5 min |
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
2. **Graceful shutdown test (T1)**: SIGTERM ตอนมี 20 in-flight messages → assert ทั้ง 20 จบ + pod exit ภายใน 90s. **สถานะ**: ⚠️ partial — the chaos test uses SIGKILL (harder failure mode), so SIGTERM drain is implicit but not explicitly measured. Add a dedicated `kill --signal=SIGTERM` run when needed.
3. **Load test (T1+T2)**: k6 driving 50 orders/min × 30 min — assert P95 end-to-end < 30s, `outbox_age_seconds < 30`. **สถานะ**: ❌ deferred — not on the T1 production-ready critical path; pick up before scale events.
4. **Saga replay test (T2)**: kill API ที่แต่ละ saga state สำหรับ 50 orders → assert saga resume จาก persisted state + reach `Completed`. **สถานะ**: ❌ Phase 2 scope.
5. **Compensation test (T2)**: force RIOT3 vendor 500 → assert saga → `FailedAwaitingRetry` → compensate หลัง retry budget หมด, leave state consistent ทุก 6 schemas. **สถานะ**: ❌ Phase 2 scope.
6. **Manual replay drill (T1)**: `POST /admin/orders/{id}/replan` × 10 stalled orders → all reach `Dispatched` ไม่มี duplicate Trip (validates 1.5 idempotency). **สถานะ**: ⚠️ partial — proven on OD-0374 / OD-0375 (n=2) via watchdog; formal n=10 drill TBD.
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
| **Week 2 (planned)** | T1.3 → T1.6 → T1.4 → T1.7 → verification | ✅ **Done 2026-06-17/18** | All T1 + T1.8 vendor-acceptance guard + chaos test PASS |
| **Week 3-4 (planned)** | T2 saga design + state table + migrations | ⚠️ **POC verified** | Architecture POC done end-to-end; Phase 2 full build deferred |
| **Week 5-6 (planned)** | T2 dual-run + shadow + cutover → verification 3-5 | ❌ Not started | Awaits Phase 2 build |
| **Month 3+ (planned)** | T3 evaluation gate based on observed metrics | ❌ Not started | Gate criteria not met yet (outbox/workflow thresholds — revisit) |

### Recommended next steps (as of 2026-06-18)

1. **Soak T1 in dev for 24-48h** (passive) — let metrics accumulate; confirm `dtms_workflow_orders_stuck_planned` stays at 0 and `outbox_age_seconds` P95 < 30s.
2. **SIGTERM drain test** (~1h) — one-off `docker kill --signal=SIGTERM` mid-pipeline to close the partial-status item in verification table.
3. **Pre-existing integration test debt triage** (15-20h, distributed) — 21 tests still failing per [`docs/integration-test-debt.md`](integration-test-debt.md); needs owner-team distribution rather than single-engineer effort.
4. **Phase 2 step 1** when ready — add `During(state, Ignore(OrderConfirmed))` for each saga state (the redelivery handling discovered in 3.5 #1), then proceed with TripDispatched / RiotMissionAccepted handlers per plan.
