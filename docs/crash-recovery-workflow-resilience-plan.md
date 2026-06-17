# DTMS Crash-Recovery & Workflow-Resilience Plan

> **Scope**: Tier 1 + Tier 2 + Tier 3 (immediate stabilization → Saga state machine → platform evolution)
> **Trigger incident**: OD-0374-WIP / OD-0375-WIP stuck after API restart on 2026-06-17

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

| # | งาน | ไฟล์ | สิ่งที่เปลี่ยน | Pattern ที่ copy | Effort |
|---|---|---|---|---|---|
| 1.1 | MassTransit retry + redelivery + in-memory outbox | [ModuleServiceRegistration.cs:318-343](../src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs#L318-L343) | เพิ่ม `UseMessageRetry(Exponential 5, 1s→30s)` + `UseDelayedRedelivery(1m, 5m, 15m, 1h)` + `UseInMemoryOutbox(context)` + `UseKillSwitch(threshold 15% over 10 msgs)` + `PrefetchCount=16`; per-endpoint `_error` + `_dead-letter` queues | New | 6h |
| 1.2 | try/catch รอบ `DispatchByRouteAsync` + structured failure | [DeliveryOrderValidatedConsumer.cs](../src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/Consumers/DeliveryOrderValidatedConsumer.cs) | wrap dispatch + `MarkJobDispatched` ใน try/catch; on failure publish `JobDispatchFailedEvent` แล้ว `throw` ให้ MassTransit retry; log `OrderId`+`JobId`+`StepName` ทุก step | forward-only guard idioms ในไฟล์เดียวกัน | 4h |
| 1.3 | Graceful shutdown (host + bus + container) | [Program.cs](../src/AMR.DeliveryPlanning.Api/Program.cs), [docker-compose.yml:54-113](../docker-compose.yml#L54-L113) | `Host.ConfigureHostOptions(o => o.ShutdownTimeout = 60s)` + `MassTransitHostOptions { WaitUntilStarted, StartTimeout=30s, StopTimeout=45s }` + `stop_grace_period: 90s` ใน docker-compose | New | 3h |
| 1.4 | **Planning reconciliation watchdog** | New: `src/Modules/Planning/.../Infrastructure/Services/PlanningReconciliationService.cs` | `BackgroundService` poll ทุก 60s — หา `DeliveryOrders.Status=Planned AND NOT EXISTS (Trip) AND age > 2 min` → re-publish `DeliveryOrderConfirmedIntegrationEventV1` (idempotent ด้วย step guards); hot-reload ผ่าน `IOptionsMonitor<PlanningWatchdogOptions>` | [Riot3ReconciliationService.cs](../src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Services/Riot3ReconciliationService.cs) | 8h |
| 1.5 | Idempotency guard บน `CreateJobAnchor` + `MarkJobDispatched` | `src/Modules/Planning/.../Commands/CreateJobAnchor/` + `MarkJobDispatched/` | เพิ่ม `WHERE Status IN (allowed-prior-states)` check; on conflict return success (ไม่ throw) ให้ตรง `MarkOrderPlanned` style | existing guarded commands ใน module เดียวกัน | 4h |
| 1.6 | Prometheus metrics สำหรับ stuck-state SLO | New: `src/AMR.DeliveryPlanning.SharedKernel/Diagnostics/WorkflowMetrics.cs` + register ใน [Program.cs](../src/AMR.DeliveryPlanning.Api/Program.cs) | Counters: `dtms_orders_stuck_planned`, `dtms_consumer_retry_total`, `dtms_consumer_faulted_total`, `dtms_outbox_pending`, `dtms_outbox_age_seconds`. Export ผ่าน OTel ที่มีอยู่ + เพิ่ม Prometheus scrape endpoint | [IdempotentProjector.cs](../src/AMR.DeliveryPlanning.SharedKernel/Projection/IdempotentProjector.cs) |  6h |
| 1.7 | Admin replan endpoint สำหรับ Planned orders | [DeliveryOrderEndpoints.cs:176](../src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/DeliveryOrderEndpoints.cs#L176) | เพิ่ม `POST /admin/orders/{id}/replan` re-fire `DeliveryOrderConfirmedDomainEvent` โดยไม่ต้องการ Status=Confirmed (Reopen-Redispatch flow ทำได้ในขั้นตอนเดียว); ครอบ admin policy | existing `/redispatch` endpoint | 3h |

**Total Tier 1: ~34h (1 sprint, 1 engineer)**

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
| **Current (incident)** | hours (customer report) | manual DB query + redispatch | ~2 | 1-2 | 3-5 | 30-60 min |
| **Post-Tier-1** | 5 min (Prometheus alert) | auto via watchdog + retry; fallback `/admin/replan` | ~0.3 (-85%) | 0-1 | 1-2 | < 5 min |
| **Post-Tier-2** | 1 min (saga state) | automatic via saga timeouts + compensation | < 0.05 (-97%) | 0 | < 1 | auto |
| **Post-Tier-3** | < 30s | durable workflow replay; zero-downtime deploy | < 0.01 (-99.5%) | 0 | < 0.3 | auto |

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

1. **Chaos test (T1)**: `scripts/chaos/kill-mid-pipeline.ps1` — inject 100 orders, kill API container ที่ random delay 1-8s ระหว่าง consumer exec. **Accept**: 0 stuck orders หลัง 5-min settle, ทั้ง 100 reach `Dispatched`
2. **Graceful shutdown test (T1)**: SIGTERM ตอนมี 20 in-flight messages → assert ทั้ง 20 จบ + pod exit ภายใน 90s
3. **Load test (T1+T2)**: k6 driving 50 orders/min × 30 min — assert P95 end-to-end < 30s, `outbox_age_seconds < 30`
4. **Saga replay test (T2)**: kill API ที่แต่ละ saga state สำหรับ 50 orders → assert saga resume จาก persisted state + reach `Completed`
5. **Compensation test (T2)**: force RIOT3 vendor 500 → assert saga → `FailedAwaitingRetry` → compensate หลัง retry budget หมด, leave state consistent ทุก 6 schemas
6. **Manual replay drill (T1)**: `POST /admin/orders/{id}/replan` × 10 stalled orders → all reach `Dispatched` ไม่มี duplicate Trip (validates 1.5 idempotency)
7. **Deploy drill (T3)**: 10 rolling deploys ภายใต้ load ต่อเนื่อง → 0 stuck orders, 0 lost messages

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
| **Week 1** | T1.1 → T1.2 → T1.5 | Foundation: retry + try/catch + idempotency เป็น prerequisite ของ T1.4 |
| **Week 2** | T1.3 → T1.6 → T1.4 → T1.7 → verification (1, 2, 6) | Stop bleeding ครบ + alerts ใช้งานได้ |
| **Week 3-4** | T2 saga design + state table + migrations | Foundation ของ state machine |
| **Week 5-6** | T2 dual-run + shadow comparison + cutover → verification (3, 4, 5) | Cutover ระวัง divergence |
| **Month 3+** | T3 evaluation gate based on observed metrics | ไม่ลงมือก่อนถ้า outbox lag / workflow count ไม่ถึง threshold |
