# OMS Operational Hardening — Implementation Plan

> **⚠️ HISTORICAL (2026-07-17):** References to the legacy OMS adapter (`IOmsShipmentClient`, `UpstreamOmsOptions`, `TripStarted/DropCompletedOmsNotifyConsumer`, `UpstreamOms__*` env) describe code REMOVED in `a81d009`; the OMS-branded audit labels/permission/routes were made system-neutral in `1bca5b0`. OMS now runs entirely on the federated pipeline (SystemEventSubscriptions + SystemCredentials + keyed formatters). Kept as a planning record — do not implement from this document.

> **Status:** Drafted 2026-06-16 — not started
> **Predecessor:** [upstream-oms-notification-plan.md](upstream-oms-notification-plan.md) (Phase 1–4 + B3 + B4 shipped)
> **Trigger:** Before flipping `UpstreamOms.Enabled=true` in prod, close the operational blind-spots that the existing notification flow doesn't cover.

---

## Why this plan exists

Phase 1–4 + B3/B4 ครอบคลุม "happy + retry + manual recovery" path เสร็จแล้ว แต่ยังเหลือ blind-spots ที่จะกัดตอน prod:

| Risk | สภาพปัจจุบัน |
|------|---------------|
| **No-one notices when OMS dies** | Fault consumer เขียน audit + log warning — operator ต้องเปิด detail-drawer ดูเอง |
| **Retry storm** | MassTransit ใช้ default `Immediate(5)` (ไม่มี `UseMessageRetry` explicit ที่ [ModuleServiceRegistration.cs:341](../src/DTMS.Api/Modules/ModuleServiceRegistration.cs#L341)) → OMS down 5 นาที = ทุก message dead-letter ทันที |
| **No metric visibility** | "OMS ตอบ 5xx กี่ครั้งเมื่อวาน?" ต้อง grep log container |
| **Bulk recovery ช้า** | OMS down 2 ชม. → 50 shipment fail → operator กด Resend ทีละ order |
| **Timeline gap** | OMS notify outcomes ไม่อยู่ใน `OrderActivity` ([OrderActivityProjector.cs:30](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Projections/OrderActivityProjector.cs#L30) gap note) |
| **JWT expiry blind** | Token exp ก.ย. 2027 — vanish ไม่มี warning |

**Cross-cutting nature:** Alerting backbone + retry policy ที่ออกแบบในแผนนี้ออกแบบให้ **reusable** กับ adapter อื่น (VendorAdapter outages, Trip stuck > 30 min, etc.) ไม่ใช่ OMS-only

---

## Decisions

| Question | Answer | เหตุผล |
|----------|--------|--------|
| Alerting channel | Slack webhook + SignalR ops hub | reuse SignalR infra ที่มี ([Realtime/](../src/DTMS.Api/Realtime/)); Slack สำหรับ off-hours persistence |
| Alert dedup | In-memory coalescing per (Kind, 60s window) | OMS outage = node ตัวเดียวเห็นภาพรวมพอ; ย้าย Redis ถ้า scale-out |
| Retry policy | MassTransit `UseMessageRetry` + `UseDelayedRedelivery` | Exponential backoff + redelivery delays → ทน OMS down 30 นาทีได้ |
| Metrics | OpenTelemetry meter `DTMS.OmsAdapter` | match pattern `DTMS.SignalR` meter ([signalr-hub-catalog.md §9](signalr-hub-catalog.md)) |
| Circuit breaker | Polly `AddStandardResilienceHandler` | reuse pattern จาก [VendorAdapter ResilienceExtensions.cs](../src/Modules/VendorAdapter/DTMS.VendorAdapter.Infrastructure/Extensions/ResilienceExtensions.cs) |
| Timeline integration | New integration event `UpstreamOmsNotifyOutcomeIntegrationEvent` | match existing projector subscription pattern (no direct audit→projector coupling) |
| Bulk replay | Admin endpoint `POST /api/admin/oms/replay?since={ts}` | gated by admin role; query `OrderAuditEvent` table directly |

---

## Phase A — Alerting backbone (P0) ⬜

**Goal:** เมื่อ dead-letter เกิด → operator รู้ภายใน 5 วินาทีโดยไม่ต้องเปิด detail-drawer

### Files

```
src/DTMS.Api/Realtime/
├── Hubs/
│   ├── OpsAlertsHub.cs                        # /hubs/ops-alerts (ใหม่)
│   └── Clients/IOpsAlertClient.cs             # AlertRaised(payload)
└── Publishers/
    ├── SignalROpsAlertPublisher.cs            # implements IOpsAlertPublisher
    └── CoalescingOpsAlertPublisher.cs         # decorator: dedup per (Kind, 60s)

src/DTMS.SharedKernel/Ops/
├── IOpsAlertPublisher.cs                      # cross-cutting interface
├── OpsAlert.cs                                # record (Kind, Severity, Title, Detail, OrderId?, TripId?, DeepLink?)
└── ISlackAlertSink.cs                         # HTTP webhook contract

src/DTMS.Api/Infrastructure/Slack/
└── SlackAlertSink.cs                          # POST to Slack incoming webhook
```

### Wiring

แก้ [TripStartedOmsNotifyFaultConsumer.cs](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Consumers/TripStartedOmsNotifyFaultConsumer.cs) (+ 4 fault consumers ที่เหลือจาก B4):

```csharp
// หลังเขียน audit row → push alert (non-blocking, swallow errors)
try
{
    await _alertPublisher.RaiseAsync(new OpsAlert(
        Kind: "oms.notify.started.failed",
        Severity: "error",
        Title: $"OMS notify dead-lettered — Trip {evt.TripId}",
        Detail: $"[{exceptionType}] {errorMessage}",
        OrderId: evt.DeliveryOrderId,
        TripId: evt.TripId,
        DeepLink: $"/delivery-orders/{evt.DeliveryOrderId}"
    ), context.CancellationToken);
}
catch (Exception ex)
{
    _logger.LogError(ex, "[OpsAlert] failed to publish — non-fatal");
}
```

### Frontend

`frontend/components/ops/ops-alert-toast.tsx` (ใหม่) — subscribe `/hubs/ops-alerts` แสดง toast มุมขวาบน

### Config

```json
{
  "Ops": {
    "Alerts": {
      "SlackWebhookUrl": "via env var Ops__Alerts__SlackWebhookUrl",
      "Channel": "#dtms-ops",
      "DeepLinkBase": "https://dtms.internal",
      "CoalesceWindowSeconds": 60
    }
  }
}
```

### Acceptance

- Mock OMS reply 500 → MassTransit retry หมด → Slack ping + toast บน dashboard ภายใน 5 วิ
- OMS down 5 นาที → 30 trips fail → Slack ได้รับ **2 messages** (first + summary) ไม่ใช่ 30
- Alert publisher fail → fault consumer ไม่ crash (audit row + log ยังลง)

**Estimated effort:** 3–4 ชม.

---

## Phase B — MassTransit retry hardening (P1) ⬜

**Goal:** OMS down 10 นาที ไม่ควรทำให้ทุก message ลง dead-letter

### Change

แก้ [ModuleServiceRegistration.cs](../src/DTMS.Api/Modules/ModuleServiceRegistration.cs#L300-L311):

```csharp
bus.UsingRabbitMq((context, cfg) =>
{
    // ... host config ...

    // 👇 Per-endpoint retry for OMS notify consumers
    cfg.ReceiveEndpoint("oms-notify-started", e =>
    {
        e.ConfigureConsumer<TripStartedOmsNotifyConsumer>(context);
        e.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(2),
            maxInterval: TimeSpan.FromMinutes(2),
            intervalDelta: TimeSpan.FromSeconds(5)));
        e.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30)));
    });
    // ... ทำซ้ำสำหรับ Arrived/Failed/Cancelled/PodCompleted (5 endpoints) ...

    cfg.ConfigureEndpoints(context); // for everything else
});
```

### Why these numbers

- 5 retries × exponential 2s→2min ≈ ทน outage 5 นาทีโดยไม่ delay
- Delayed redelivery (1m / 5m / 30m) ทน outage 35 นาทีก่อน dead-letter
- รวม ~40 นาที window — ครอบคลุม "OMS deploy ใหม่ + smoke test"

### Acceptance

- Kill OMS 10 นาที → restart → message ทั้งหมด process สำเร็จไม่ dead-letter
- Kill OMS 1 ชม. → dead-letter ลง audit + alert (Phase A)
- Latency เพิ่มไม่เกิน 7s ในกรณี OMS healthy (1 immediate retry max)

**Estimated effort:** 1 ชม.

---

## Phase C — Token expiry + Metrics (P1) ⬜

### C.1 Token expiry startup check

แก้ [OmsAdapterServiceRegistration.cs](../src/Modules/OmsAdapter/OmsAdapterServiceRegistration.cs):

```csharp
// Parse JWT exp claim at startup → log warning ถ้า < 30 วัน, error ถ้า < 7 วัน
var token = options.BearerToken;
if (!string.IsNullOrWhiteSpace(token))
{
    var exp = ParseJwtExp(token);
    var daysLeft = (exp - DateTime.UtcNow).TotalDays;
    if (daysLeft < 7) logger.LogError("[OmsAdapter] Bearer token expires in {Days}d", daysLeft);
    else if (daysLeft < 30) logger.LogWarning("[OmsAdapter] Bearer token expires in {Days}d", daysLeft);
}
```

Bonus: เพิ่ม health check endpoint `/health/oms-token` → 503 ถ้า exp < 7 วัน → CD pipeline alert ได้

### C.2 OpenTelemetry meter `DTMS.OmsAdapter`

```csharp
internal static class OmsMetrics
{
    public static readonly Meter Meter = new("DTMS.OmsAdapter", "1.0");
    public static readonly Counter<long> NotifyTotal =
        Meter.CreateCounter<long>("oms_notify_total",
            description: "Total OMS notify calls, tagged stage + outcome + status_code");
    public static readonly Histogram<double> NotifyLatencyMs =
        Meter.CreateHistogram<double>("oms_notify_latency_ms");
    public static readonly Counter<long> NotifyAttempts =
        Meter.CreateCounter<long>("oms_notify_attempts",
            description: "Retry attempt distribution per shipmentId");
}
```

Wire ใน [HttpOmsShipmentClient.cs](../src/Modules/OmsAdapter/Infrastructure/Services/HttpOmsShipmentClient.cs) (shared `PostStageAsync` helper):
```csharp
sw.Stop();
OmsMetrics.NotifyTotal.Add(1,
    new("stage", stage),
    new("outcome", outcome),
    new("status_code", (int)response.StatusCode));
OmsMetrics.NotifyLatencyMs.Record(sw.Elapsed.TotalMilliseconds,
    new("stage", stage), new("outcome", outcome));
```

### Acceptance

- Token < 30d → startup log warning
- `/health/oms-token` returns 503 เมื่อ exp < 7d
- Prometheus scrape เห็น metrics ทั้ง 3 ตัว
- Grafana dashboard "OMS Notify" plot success-rate + p95 latency

**Estimated effort:** 2–3 ชม.

---

## Phase D — Circuit breaker (P2) ⬜

**Goal:** OMS down → หยุดยิง request หลัง 5 fails ติด ลด pressure บน OMS + consumer thread pool

### Change

แก้ [OmsAdapterServiceRegistration.cs](../src/Modules/OmsAdapter/OmsAdapterServiceRegistration.cs):

```csharp
services.AddHttpClient<IOmsShipmentClient, HttpOmsShipmentClient>(...)
    .AddStandardResilienceHandler(o =>
    {
        // Retry layer handled by MassTransit (Phase B) — turn off here
        o.Retry.MaxRetryAttempts = 0;

        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.MinimumThroughput = 10;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });
```

### Why two retry layers don't conflict

- MassTransit retry: message-level (across process restarts, durable)
- Polly retry: in-flight HTTP only — disable to avoid double-retry
- Circuit breaker: short-circuit fast ก่อนเข้า HTTP layer → MassTransit redelivery จะ catch แทน

### Acceptance

- Kill OMS → ครั้งที่ 6+ ขว้าง `BrokenCircuitException` ภายใน 1ms (ไม่รอ 10s timeout)
- Circuit half-open หลัง 30s → ถ้า OMS up → continue ปกติ

**Estimated effort:** 1 ชม.

---

## Phase E — OMS events → OrderActivity timeline (P2) ⬜

**Goal:** ปิด gap ใน [OrderActivityProjector.cs:30](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Projections/OrderActivityProjector.cs#L30) — OMS notify outcomes โผล่ใน unified timeline

### New integration event

```csharp
public record UpstreamOmsNotifyOutcomeIntegrationEvent(
    Guid DeliveryOrderId,
    Guid TripId,
    string Stage,          // "Started" | "Arrived" | "Failed" | "Cancelled" | "PodCompleted"
    string Outcome,        // "Success" | "Failed" | "ManuallyResent"
    string? ErrorMessage,
    int AttemptNumber,
    DateTime OccurredAtUtc);
```

### Wiring

- Raise ใน 5 main consumers + 5 fault consumers + 5 resend handlers (15 emit points)
- Subscribe ใน [OrderActivityProjector.cs](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Projections/OrderActivityProjector.cs) เพิ่ม `IConsumer<UpstreamOmsNotifyOutcomeIntegrationEvent>`
- Map → `OrderActivityRow` with `Category="OmsNotify"`, `Source="System"`

### Acceptance

- Timeline แสดง row "Upstream OMS notified (Started) — vehicle AMR_FAN1_No1" ระหว่าง Trip lifecycle rows
- Failed row แสดงเป็น error severity
- Manual resend แสดงเป็น "user-action" subtype

**Estimated effort:** 3–5 ชม.

---

## Phase F — Bulk replay tool (P2) ⬜

**Goal:** OMS up มาจาก 2-ชม. outage → ยิง resend 50 shipments ใน 1 command

### Backend

```
POST /api/admin/oms/replay
Body: { sinceUtc, untilUtc?, stage?, dryRun: true }
Response: { matched: 47, replayed: 47, failed: 0, details: [...] }
```

Logic:
1. Query `OrderAuditEvent` where `EventType IN (UpstreamOmsNotifyFailed, UpstreamOmsArrivedNotifyFailed, ...)` AND `OccurredAt >= sinceUtc`
2. Dedup by `(OrderId, TripId, Stage)` — keep latest
3. For each → call corresponding `ResendOms*NotificationCommand` (existing infra from Phase 4)
4. Return summary

### Frontend

Admin page `frontend/app/admin/oms-replay/page.tsx` — date picker + preview table + Replay button

### Acceptance

- Dry run แสดง matched count + ไม่ POST จริง
- Live run → audit `UpstreamOmsManuallyResent` ลง + Slack alert "Bulk replay: 47 shipments"
- Idempotent — กดซ้ำ 2 ครั้ง OMS รับเป็น no-op (409 path)

**Estimated effort:** 2–3 ชม.

---

## Build & rollout order

| Step | Output | Risk | Block flip? |
|------|--------|------|-------------|
| 1 | Phase A — Alerting backbone | ต่ำ | **YES** — blind spot ใหญ่สุด |
| 2 | Phase B — Retry policy | ต่ำ | **YES** — กัน dead-letter storm จากสิ่งเล็ก ๆ |
| 3 | **Flip kill switch** ใน dev → smoke test | กลาง | — |
| 4 | Phase C — Token expiry + Metrics | ต่ำ | No (รอ post-flip) |
| 5 | **Flip kill switch** ใน prod | กลาง | — |
| 6 | Phase D — Circuit breaker | ต่ำ | No |
| 7 | Phase E — Timeline integration | กลาง | No |
| 8 | Phase F — Bulk replay tool | ต่ำ | No |

**Critical path before prod flip:** Phase A + B + smoke test (~5 ชม.)
**Total effort all phases:** ~12–17 ชม.

---

## Status snapshot

### ✅ Completed
- (none yet — plan drafted 2026-06-16)

### ⬜ Remaining (in priority order)
- Phase A — Alerting backbone
- Phase B — MassTransit retry hardening
- Phase C — Token expiry + OTel metrics
- Phase D — Polly circuit breaker
- Phase E — OMS → OrderActivity timeline
- Phase F — Bulk replay admin tool

---

## Cross-references

| Doc | Relevance |
|-----|-----------|
| [upstream-oms-notification-plan.md](upstream-oms-notification-plan.md) | Phase 1–4 + B3/B4 — the happy/retry/recovery path this plan hardens |
| [signalr-hub-catalog.md](signalr-hub-catalog.md) | Add OpsAlertsHub here when Phase A lands |
| [projector-catalog.md](projector-catalog.md) | Update OrderActivityProjector entry when Phase E lands |
| [replay-runbook.md](replay-runbook.md) | Link Phase F bulk-replay tool when shipped |
| [AMR_Delivery_Planning_System_Design.md](AMR_Delivery_Planning_System_Design.md) | Reference for cross-cutting integration patterns |

### Reusable beyond OMS
- **OpsAlertsHub + IOpsAlertPublisher** (Phase A) → future use: VendorAdapter outage, Trip stuck > 30 min, Outbox backlog > N
- **Retry policy pattern** (Phase B) → apply to VendorAdapter HTTP consumers next
