# Remaining Phases — Implementation Plans

Plans สำหรับงานที่เหลือหลัง OMS Notification + Planning b8/b9/b10-frontend.1 ทุก phase ได้ implementation detail พอที่จะ build ได้ — ปรับ scope ก่อนเริ่มจริงตามข้อมูลใหม่ที่อาจมีเพิ่ม

อ้างอิง `planning-workflow-roadmap.md` สำหรับ priority + impact

---

## Sprint roadmap

```
Sprint N (1-2 weeks):
  ├─ b11           — Order ตาม Trip cancel cascade      (P0 / M)
  └─ b10-frontend.2 — Jobs queue page                    (P1 / M)

Sprint N+1:
  ├─ b12           — StatusHistory tables                (P0 / L)
  └─ b13           — Job.FailureCategory enum            (P1 / S)

Sprint N+2:
  ├─ OMS-refresh   — Token auto-refresh                  (P1 / M)
  └─ OMS-replay    — Backfill / dead-letter recovery     (P2 / S)

Backlog (do-when-needed):
  - Pickup/Drop visibility in Job
  - JobExceptions collection
  - Dead code cleanup (OrderStatus.Amended, ItemStatus.Returned)
  - Pre-b8 Trip backfill script
  - Mark{Status} naming refactor
  - Stateless state-machine library
```

---

# Phase b11 — Order ตาม Trip cancel cascade

## Problem

`Order=Dispatched + Trip=Cancelled` ค้างตลอดกาล — ตัวอย่างจาก prod data 3 orders เป็นแบบนี้

**Root cause**: [TripCancelledConsumer.cs](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Consumers/TripCancelledConsumer.cs) ปลด items เป็น `Pending` เพื่อรอ retry — แต่ถ้าไม่มีใคร retry ก็ค้างไปตลอด

## Design — เลือก hybrid (B + A)

| Option | เลือก? | เหตุผล |
|---|---|---|
| A. Background service auto-escalate `Held` | ✅ ใช้เป็น safety net | ทำงานเงียบ ๆ ไม่ต้องเทรน operator |
| B. Operator endpoint `/abandon-after-trip-cancel` | ✅ ใช้เป็น primary | Operator ตัดสินใจตอนเห็นจริง — semantically ตรง |
| C. Cascade Trip → Order Cancel | ❌ ไม่เลือก | ตัด option retry ออกหมด — เสียค่าใช้จ่ายเดิม |

**B = primary** (operator-driven) + **A = safety net** (1h timeout → auto-escalate `Held` แทน Cancel เพื่อให้ operator decide)

## Scope

### Backend

```
src/Modules/DeliveryOrder/.../Application/Commands/AbandonAfterTripCancel/
├── AbandonAfterTripCancelCommand.cs
└── AbandonAfterTripCancelCommandHandler.cs
```

```
src/Modules/DeliveryOrder/.../Application/Services/
└── StuckOrderEscalationService.cs   (BackgroundService)
```

### Files modified

- `DeliveryOrderEndpoints.cs` — new `POST /{id}/abandon-after-trip-cancel`
- `DeliveryOrder.Domain/Entities/DeliveryOrder.cs` — new method `MarkAbandonedAfterTripCancel(reason, abandonedBy)`
- `ModuleServiceRegistration.cs` — register `StuckOrderEscalationService` + options

### Domain logic

```csharp
public void MarkAbandonedAfterTripCancel(string reason, string? abandonedBy)
{
    if (Status != OrderStatus.Dispatched)
        throw new InvalidOperationException(
            $"Abandon-after-trip-cancel only valid from Dispatched; current = {Status}");

    // Items released to Pending by TripCancelledConsumer are still Pending —
    // mark them Cancelled too so the order isn't half-released.
    foreach (var item in _items.Where(i => i.Status == ItemStatus.Pending))
        item.MarkCancelled();

    Status = OrderStatus.Cancelled;
    UpdatedDate = DateTime.UtcNow;
    AddDomainEvent(new DeliveryOrderCancelledDomainEvent(
        Guid.NewGuid(), DateTime.UtcNow, Id, reason, abandonedBy));
    // Audit added by handler — keeps the entity ignorant of plumbing
}
```

### Background service

```csharp
public class StuckOrderEscalationService : BackgroundService
{
    // Tunable: 1h threshold + 5min poll
    // Idempotent — operates on snapshots, MarkHeld() is no-op for already-Held
    private const int StuckThresholdMinutes = 60;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-StuckThresholdMinutes);
                var stuck = await _orderRepository.GetStuckDispatchedAsync(cutoff, ct);
                foreach (var order in stuck)
                {
                    if (!await HasActiveTripsAsync(order.Id, ct))
                    {
                        order.MarkHeld("Auto-escalated: Dispatched > 1h with no active Trip",
                                       heldBy: "system");
                        await _repo.SaveChangesAsync(ct);
                        _logger.LogWarning("[StuckOrder] {OrderId} → Held (auto-escalation)", order.Id);
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "[StuckOrder] poll failed"); }
            await Task.Delay(PollInterval, ct);
        }
    }
}
```

### Frontend

[detail-drawer.tsx](../frontend/components/delivery-orders/detail-drawer.tsx):
- เพิ่ม action button "Abandon after Trip cancel" — เด้งเฉพาะตอน `Order=Dispatched` + ทุก Trip ที่เห็น = Cancelled
- Confirm dialog (semi-destructive — releases items to Cancelled)

```tsx
{data.orderStatus === "Dispatched" 
 && trips?.every(t => t.status === "Cancelled")
 && (
  <DrawerActionButton tone="coral" onClick={() => onAction("abandon-trip-cancel", data.id)}>
    Abandon (all trips cancelled)
  </DrawerActionButton>
)}
```

## Acceptance

- ✅ Operator endpoint สำเร็จ → Order `Dispatched → Cancelled`, items Pending → Cancelled
- ✅ Background service detect `Dispatched + 0 active + items Pending > 1h` → escalate `Held`
- ✅ Idempotent (กดซ้ำได้ไม่ side-effect)
- ✅ Audit entry `OrderAbandonedAfterTripCancel` พร้อม actor/reason
- ✅ Order InProgress / Held / terminal states → reject (only Dispatched valid)

## Edge cases

| Case | Behavior |
|---|---|
| Order มี Trip InProgress | reject — ใช้ `/cancel` (cascade) แทน |
| Items บางตัว Picked แล้ว | reject — ของจริงอยู่ที่ robot, ต้องเก็บ Trip cycle ปกติ |
| Background service ขณะกำลังเปลี่ยน state | concurrent → `DbUpdateConcurrencyException` → log + skip รอบนี้ |

## Risk

| Risk | Mitigation |
|---|---|
| Background service "ฆ่า" order ตอน operator กำลังจะ retry | Threshold 1h + escalate เป็น `Held` ไม่ใช่ Cancel → operator ยังกู้คืนได้ |
| Stuck order count บานปลายช่วงแรก | Manual cleanup script รัน 1 ครั้งหลัง deploy |

## Effort: M (1-2 วัน) | Impact: สูง

---

# Phase b10-frontend.2 — Jobs queue page

## Problem

ตอนนี้ operator เห็น Jobs ได้แค่ใน drawer ของ Order ตัวเดียว — ถ้าอยาก triage Pending/Failed jobs ข้าม order ต้องเปิดทีละตัว

## Design

หน้าใหม่ `/delivery-orders/jobs` รูปแบบ list view เหมือน orders list — filter tabs + detail drawer ซ้อน

## Scope

### Backend (มีอยู่แล้ว — เพิ่มแค่ list query)

```
src/Modules/Planning/.../Application/Queries/GetJobsList/
├── GetJobsListQuery.cs
└── GetJobsListQueryHandler.cs
```

Filter: `status` (Pending|Failed|Cancelled|All), `priority`, `search`, `page`, `pageSize`
Sort: createdAt desc (default), failedAt, status

### Frontend pages

```
frontend/app/delivery-orders/jobs/
├── page.tsx                  # list + filters
└── jobs-experience.tsx       # client component (state + drawer)

frontend/components/planning/
├── jobs-list-table.tsx       # table view
├── job-detail-drawer.tsx     # detail drawer (stacks above list)
└── job-status-filter.tsx     # filter tabs (Pending | Failed | All)
```

### Proxy routes

```
frontend/app/api/planning/jobs/
├── route.ts                  # GET list (already drafted)
└── [id]/
    ├── route.ts              # GET single
    └── retry/
        └── route.ts          # POST retry
```

### Files modified

- [left-rail.tsx](../frontend/components/shell/left-rail.tsx) — add "Jobs" nav entry under Delivery Orders group
- [lib/api/jobs.ts](../frontend/lib/api/jobs.ts) — add `getJobsList`, `getJobById`, `retryJob`

## Page UX (sketch)

```
┌─────────────────────────────────────────────────────────┐
│  Jobs                              [+ New from Order]  │
│  ─────────────────────────────────────────────────────  │
│  [Pending 3] [Failed 7] [Cancelled 1] [All]            │
│  Search: ____________________                           │
│                                                         │
│  Job ID         Order       Status   Failure   Created │
│  ──────────────────────────────────────────────────────│
│  a3f2…  G1     OD-0330-WIP  Failed   429        12:32 │
│  8c2a…  G2     OD-0331-WIP  Pending   —         12:30 │
│  ...                                                   │
└─────────────────────────────────────────────────────────┘
```

Click row → JobDetailDrawer:
- Job state machine timeline (state transitions)
- Linked Trip (chip → opens TripDetailDrawer)
- Linked DeliveryOrder (chip → opens OrderDetailDrawer)
- **Retry button** (เฉพาะ Failed) → POST `/api/planning/jobs/{id}/retry`
- Failure reason + category (when b13 lands)

## Acceptance

- ✅ List โหลดได้, filter ทุกตัวทำงาน, pagination ใช้ได้
- ✅ Job drawer แสดง full lifecycle + linked Trip/Order
- ✅ Retry button POST → audit row "JobRetryTriggered" — Job status flip → Trip created
- ✅ Drawer stacks ถูก (Job > Trip > Order — esc ปิดทีละชั้น)
- ✅ Soft-fail: ถ้า trip/order link 404 — แสดง warning chip ไม่ block UI

## Edge cases

| Case | Behavior |
|---|---|
| Retry บน Job ที่ trip InProgress | backend reject — UI แสดง toast error |
| Job ถูกลบจาก DB (cancel cascade) | List filter hide; direct URL → 404 page |
| Pagination ตอนมี job ใหม่เข้า | infinite scroll หรือ "Load more" — ไม่ถี่ขนาดต้อง websocket |

## Effort: M (1-2 วัน) | Impact: สูง

---

# Phase b12 — StatusHistory tables

## Problem

ตอบคำถาม "Order นี้ entered Planning ตอนไหน" ต้อง `LIKE 'Planning%'` บน `OrderAuditEvent.EventType` — fragile, slow, ไม่มี structured `FromStatus / ToStatus`

## Design

3 ตาราง 1:N กับ aggregate root — write บน state transition ทุกครั้ง

```sql
CREATE TABLE deliveryorder."OrderStatusHistory" (
  "Id"           uuid PRIMARY KEY,
  "OrderId"      uuid NOT NULL REFERENCES deliveryorder."DeliveryOrders"("Id") ON DELETE CASCADE,
  "FromStatus"   varchar(20) NULL,                  -- null = initial state (Draft)
  "ToStatus"     varchar(20) NOT NULL,
  "OccurredAt"   timestamptz NOT NULL,
  "TriggeredBy"  varchar(100) NULL,                 -- user-id / "system" / "vendor-webhook"
  "Reason"       text NULL,
  CONSTRAINT "UX_OrderStatusHistory" UNIQUE ("OrderId","OccurredAt")
);
CREATE INDEX "IX_OrderStatusHistory_OrderId" ON deliveryorder."OrderStatusHistory"("OrderId");
CREATE INDEX "IX_OrderStatusHistory_ToStatus_OccurredAt"
  ON deliveryorder."OrderStatusHistory"("ToStatus","OccurredAt" DESC);

-- Mirror tables in planning + dispatch schemas
```

## Approach — EF interceptor (Recommended)

`AuditSaveChangesInterceptor` มีอยู่แล้ว — extend ให้ detect status property changes บน aggregates ที่ register ไว้

```csharp
public class StatusHistoryInterceptor : SaveChangesInterceptor
{
    private static readonly Dictionary<Type, string> StatusPropertyMap = new()
    {
        { typeof(DeliveryOrder), nameof(DeliveryOrder.Status) },
        { typeof(Job),            nameof(Job.Status) },
        { typeof(Trip),           nameof(Trip.Status) },
    };

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct)
    {
        var ctx = eventData.Context!;
        foreach (var entry in ctx.ChangeTracker.Entries()
                 .Where(e => e.State == EntityState.Modified
                          && StatusPropertyMap.ContainsKey(e.Entity.GetType())))
        {
            var prop = entry.Property(StatusPropertyMap[entry.Entity.GetType()]);
            if (!prop.IsModified || Equals(prop.OriginalValue, prop.CurrentValue))
                continue;

            // Stage status history row in the same transaction
            var historyEntry = BuildHistoryEntry(entry, prop);
            ctx.Add(historyEntry);
        }
        return await base.SavingChangesAsync(eventData, result, ct);
    }
}
```

## Scope

### Migrations
```
src/Modules/DeliveryOrder/.../Migrations/20260612000000_AddOrderStatusHistory.cs
src/Modules/Planning/.../Migrations/20260612000100_AddJobStatusHistory.cs
src/Modules/Dispatch/.../Migrations/20260612000200_AddTripStatusHistory.cs
```

### Domain entities
- `DeliveryOrder.Domain/Entities/OrderStatusHistory.cs`
- `Planning.Domain/Entities/JobStatusHistory.cs`
- `Dispatch.Domain/Entities/TripStatusHistory.cs`

### Infrastructure
- `*/Infrastructure/Interceptors/StatusHistoryInterceptor.cs` (per module หรือ shared)
- Register ใน each `DbContext.OnConfiguring` ผ่าน `AddInterceptors`

### Query helper
```
GetOrderTimelineQuery — extend ให้ merge status history เข้า timeline ที่มีอยู่
```

### Frontend
- Audit timeline ใน drawer แสดง state transition แยกจาก audit events
- Color-code: status transition (blue) vs audit event (gray)

## Backfill (one-shot)

```sql
-- Bootstrap from existing OrderAuditEvents — best effort
INSERT INTO deliveryorder."OrderStatusHistory"
  ("Id","OrderId","FromStatus","ToStatus","OccurredAt","TriggeredBy","Reason")
SELECT
  gen_random_uuid(),
  e."DeliveryOrderId",
  NULL,                                                -- can't reconstruct FromStatus
  regexp_replace(e."EventType", '^(Marked|Order)?(\w+)$', '\2'),
  e."OccurredAt",
  e."ActorId",
  e."Details"
FROM deliveryorder."OrderAuditEvents" e
WHERE e."EventType" ~ '^(Marked|Order)?(Validated|Confirmed|Dispatched|InProgress|Completed|Cancelled)$';
```

หรือทำใน background service สำหรับ historical orders

## Acceptance

- ✅ State transition ทุก aggregate → row ใน history table
- ✅ Query "Order X entered Planning at ?" → single index seek
- ✅ Interceptor idempotent — duplicate save ไม่ insert ซ้ำ
- ✅ Backfill script ครอบคลุม pre-b12 data
- ✅ Audit timeline UI ระบุ state transition แยกได้

## Risk

| Risk | Mitigation |
|---|---|
| Interceptor crash → save fail | try/catch internal — status history เป็น best-effort, audit หลัก (OrderAuditEvent) ยังคงอยู่ |
| Performance — interceptor runs ทุก save | StatusPropertyMap lookup เร็ว — overhead < 1ms ต่อ save |
| Disk growth — high-throughput orders | ~50 transitions/order, low cardinality. Partition by OccurredAt year ถ้าเกิน 10M rows |

## Effort: L (2-3 วัน) | Impact: สูง

---

# Phase b13 — Job.FailureCategory enum

## Problem

`Job.FailureReason` เป็น text — query "Jobs ที่ vendor 429" ต้อง `LIKE 'Too Many Requests%'` — fragile

## Design

เพิ่ม structured enum + คงไว้ `FailureReason` text สำหรับ free-form details

```csharp
public enum JobFailureCategory
{
    None,                    // success path
    TemplateMissing,
    TemplateResolveFailed,
    VendorRejected,          // 4xx/5xx (excluding 429)
    VendorRateLimited,       // 429
    VendorExecutionFailed,   // Phase b9 — TripFailed webhook
    TripPersistenceFailed,   // orphan
    OperatorCancelled,       // Phase b9 — TripCancelled webhook (operator-driven)
    VendorCancelled,         // Phase b9 — TripCancelled (vendor-driven, e.g. abort)
}
```

## Scope

### Backend
- `Planning.Domain/Enums/JobFailureCategory.cs` (new)
- `Planning.Domain/Entities/Job.cs` — add `FailureCategory` property + extend `MarkFailed`/`MarkCancelled`
  ```csharp
  public void MarkFailed(JobFailureCategory category, string reason)
  ```
- All callers update:
  - `TripFailedJobConsumer.cs` → `VendorExecutionFailed`
  - `TripCancelledJobConsumer.cs` → distinguish operator vs vendor
  - `MarkJobFailedCommandHandler.cs` → expose category in command
  - `CreateJobAnchorCommandHandler.cs` (template error) → `TemplateMissing/Resolve*`
  - `RetryJobCommandHandler.cs` (vendor reject on retry) → `VendorRejected`/`VendorRateLimited`
- Migration `20260613000000_AddJobFailureCategory.cs` — new column + index

### Frontend
- `lib/api/jobs.ts` — add `failureCategory: JobFailureCategory | null` to JobDto
- `components/planning/badges.tsx` — `FailureCategoryBadge` per category
- Jobs queue page (b10-frontend.2) — filter by category

## Heuristic mapping

จาก existing FailureReason text → category:
| Pattern | Category |
|---|---|
| "Template not found" | TemplateMissing |
| HTTP 429 / "Too Many Requests" | VendorRateLimited |
| HTTP 4xx / 5xx (other) | VendorRejected |
| "operator cancelled" | OperatorCancelled |
| "vendor cancelled" | VendorCancelled |
| TripFailedIntegrationEvent | VendorExecutionFailed |

Backfill: SQL update categorize existing rows ตาม pattern เดียวกัน

## Acceptance

- ✅ Job entity คงไว้ Reason (text) + category (enum) → ตอบ "ทำไม fail" ทั้งสองมุม
- ✅ Filter UI by category ใน Jobs queue page
- ✅ Backfill script ครอบคลุม historical Jobs ≥ 95%

## Effort: S (4-6 ชม) | Impact: กลาง

---

# OMS-refresh — Token auto-refresh

## Problem

OMS Bearer JWT มี exp ~2027-09 แต่ server มี session-timeout layer ต่างหาก (test เคยเจอ 401 `session_expired` หลัง inactivity ~1h+)

## Design

**ต้องคุยกับ OMS team ก่อน**: มี `/login` endpoint refresh-token flow ไหม? เป็น static long-lived หรือ session?

ถ้ามี refresh flow:

```
src/Modules/OmsAdapter/Infrastructure/Auth/
├── IOmsAuthTokenProvider.cs
└── OmsAuthTokenProvider.cs   (cached + auto-refresh on 401)
```

```csharp
public class OmsAuthTokenProvider : IOmsAuthTokenProvider
{
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-5))
            return _cachedToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-5))
                return _cachedToken;  // double-check after lock

            var fresh = await _http.PostAsJsonAsync("api/auth/login", _credentials, ct);
            var body = await fresh.Content.ReadFromJsonAsync<OmsAuthResponse>(ct);
            _cachedToken = body!.Token;
            _expiresAt = body.ExpiresAt;
            return _cachedToken;
        }
        finally { _refreshLock.Release(); }
    }
}
```

แล้ว `HttpOmsShipmentClient` ใช้ `DelegatingHandler` ที่ inject token ทุก request:

```csharp
public class OmsAuthHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _provider.GetAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token expired mid-flight — force refresh + retry once
            _provider.Invalidate();
            token = await _provider.GetAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response.Dispose();
            response = await base.SendAsync(request, ct);
        }
        return response;
    }
}
```

## Configuration
```json
"UpstreamOms": {
  "Auth": {
    "LoginUrl": "http://10.204.37.65:5002/api/auth/login",
    "Username": "...",
    "Password": "...",       // via env var
    "TokenTtlSeconds": 3600  // server-side TTL
  }
}
```

## Scope

- `UpstreamOmsOptions.Auth` sub-options
- `IOmsAuthTokenProvider` + impl
- `OmsAuthHandler` (DelegatingHandler)
- Register handler ใน `OmsAdapterServiceRegistration.cs`
- ลบ static `BearerToken` config (deprecate gradually)

## Acceptance

- ✅ Token cached + refresh ก่อน expire 5min
- ✅ 401 mid-request → auto-refresh + retry 1 ครั้ง
- ✅ Concurrent requests share single refresh (semaphore)
- ✅ Login fail → throw → MassTransit retry — ไม่ infinite loop

## Effort: M (1-2 วัน) | Impact: สูง (production blocker)

---

# OMS-replay — Backfill / dead-letter recovery

## Scope

Tool / endpoint สำหรับ replay missed notifications:

```
src/Modules/OmsAdapter/Infrastructure/Recovery/
└── OmsDeadLetterRecoveryService.cs   (admin endpoint)
```

```csharp
// POST /api/v1/admin/oms/replay-failed?since=<utc>&max=<int>
//
// Scan UpstreamOms*NotifyFailed audit rows, group by shipmentId/event-type,
// re-fire latest event-type per shipmentId (deduplicate). Wraps each in
// existing Resend command handler — gets same gates + audit.
```

## Acceptance

- ✅ Admin-only endpoint (require auth + role)
- ✅ Dry-run mode (return what would be replayed)
- ✅ Rate-limited (batch of 10 per call)

## Effort: S (4-6 ชม) | Impact: กลาง

---

# Backlog phases (brief)

## D1. TripPaused/Resumed → Job mirror
- `TripPaused/ResumedIntegrationEvent` (new — currently DomainEvent only)
- `TripPaused/ResumedJobConsumer` in Planning
- `Job.MarkPaused/MarkResumed` methods
- **Effort**: S | **Impact**: ต่ำ-กลาง

## D2. Pickup/Drop visibility in Job
- Breaking event contract: TripPickup/DropCompletedIntegrationEvent + `JobId`
- Or: cross-module Trip→Job lookup ใน consumer
- Sub-status `Picked/Delivered` or separate field
- **Effort**: M | **Impact**: ต่ำ (DeliveryOrder.Items already covers)

## D3. JobExceptions collection
- `Planning.Domain/Entities/JobException.cs`
- `Job.RaiseException(code, severity, detail)` method
- Wire `ExceptionRaisedIntegrationEvent` consumer
- **Effort**: M | **Impact**: ต่ำ (reconciliation tooling only)

## E1. Dead code cleanup
- Choose & remove: `OrderStatus.Amended` (+ `DeliveryOrderAmendedIntegrationEvent`)
- Choose & remove: `ItemStatus.Returned`
- **Effort**: S (1-2 ชม) | **Impact**: ต่ำ

## E2. Pre-b8 Trip backfill script
- One-shot SQL/CLI: create Job row per pre-b8 Trip, derive from OrderRef + UpperKey, set `Trip.JobId`
- **Effort**: S | **Impact**: ต่ำ (legacy data only)

## E3. Mark{Status} naming refactor
- Unify across Trip / Job / Item / Order aggregates
- Heavy test churn — schedule with quiet period
- **Effort**: L | **Impact**: ต่ำ (DX)

## E4. Stateless state-machine library
- Adopt [Stateless](https://github.com/dotnet-state-machine/stateless) in new aggregates first
- Migrate existing aggregates incrementally
- **Effort**: L | **Impact**: ต่ำ (DX)

---

## Cross-references

- Phase b8/b9 design notes → user memory
- OMS notification plan → [upstream-oms-notification-plan.md](upstream-oms-notification-plan.md)
- Planning roadmap → [planning-workflow-roadmap.md](planning-workflow-roadmap.md)

อัพเดตไฟล์นี้เมื่อ phase ใดเข้า main แล้ว — เพื่อเก็บ implementation plan เป็น single source of truth ของงาน DTMS
