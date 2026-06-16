# Phase P2 — Activity Timeline Projection Plan

> **Status:** Ready to start (pending UX decision in §4)
> **Decision date:** 2026-06-15
> **Predecessor:** [event-projection-implementation-plan.md](event-projection-implementation-plan.md) (P0+P1 shipped)
> **Discovery context:** [code-review findings on 2026-06-15 after pulling 6 upstream commits revealed `OrderActivityProjector` is ~60% done by another contributor — read model + read endpoint + frontend `FullAuditLog` already wired, only SignalR push + 5 event subscriptions + UX consolidation missing]

---

## 0. Executive Summary

P2 จะปิดให้ **Order Activity Timeline = unified, live, complete** โดย:

1. ปิด **P1 coverage gaps** ที่ค้นพบจาก review (3 events ใน Order, 1 event ใน Trip)
2. ลบ comment ที่ stale ใน 2 projector
3. **ตัดสินใจ UX** ว่า StatusTimelineSection (P1) + FullAuditLog (existing) จะอยู่ร่วมกันยังไง — เลือก 1 จาก 3 options
4. **ทำให้ OrderActivityProjector push SignalR** ผ่าน `IOrderRealtimePublisher.PublishActivityUpdatedAsync` (extend ที่มี ไม่สร้าง interface ใหม่)
5. ปิด **OrderActivityProjector coverage gaps** (5 events: 3 early lifecycle + RobotPassAck + PodCaptured)

**ของที่ทำเสร็จแล้ว ไม่ต้องทำซ้ำ:** read model table + projection store + read repository + REST endpoint + frontend FullAuditLog (REST polling) + migration + initial backfill — ทุกอย่างใน OrderActivity bounded context มีอยู่แล้ว

**Effort estimate:** S–M (2-3 วัน) — เพราะของหลักทำไปแล้ว

---

## 1. Pre-P2 Housekeeping (Step 1-3) — ครึ่งวัน

### 1.1 Stale comments — Step 1

**`src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Projections/OrderStatusHistoryProjector.cs:9-18`**

ปัจจุบัน:
```csharp
/// Coverage gaps (deliberate — no integration event exists today; will be
/// added in P0.2 hardening if needed):
///   - Submitted, Validated, Planning, Planned
```

แก้เป็น:
```csharp
/// Coverage:
///   - Confirmed, Dispatched, InProgress, Completed, PartiallyCompleted,
///     Failed, Cancelled, Rejected, Held, Released, Amended
///   - Created, Submitted, Validated (Phase P2 housekeeping, 2026-06-15)
///
/// Coverage gaps (deliberate — internal-only domain events):
///   - Drafted, DraftUpdated, PlanningStarted, Planned, Reopened, Redispatched
///     (no integration event published by DeliveryOrderDomainEventMapper)
```

**`src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Projections/OrderActivityProjector.cs:16-34`**

ปัจจุบัน:
```csharp
///   <item>Order lifecycle (11 events) — full coverage via DeliveryOrder
///         integration events.</item>
```

แก้เป็น:
```csharp
///   <item>Order lifecycle (14 events) — Created, Submitted, Validated,
///         Confirmed, Dispatched, InProgress, Completed,
///         PartiallyCompleted, Failed, Cancelled, Rejected, Held,
///         Released, Amended.</item>
///   <item>Trip execution (9 events) — Started, PickupCompleted,
///         DropCompleted, Completed, Failed, Cancelled, Paused, Resumed,
///         RobotPassAcknowledged.</item>
///   <item>POD scans (1 event) — PodCaptured.</item>
```

**Acceptance:** Comments accurately reflect what's coded after §2 + §5.

---

### 1.2 OrderStatusHistoryProjector +3 early lifecycle — Step 2

แก้ `OrderStatusHistoryProjector.cs`:

```csharp
public class OrderStatusHistoryProjector :
    IConsumer<DeliveryOrderCreatedIntegrationEventV1>,       // NEW
    IConsumer<DeliveryOrderSubmittedIntegrationEventV1>,     // NEW
    IConsumer<DeliveryOrderValidatedIntegrationEventV1>,     // NEW
    IConsumer<DeliveryOrderConfirmedIntegrationEventV1>,
    // ... existing 10 events
{
    public Task Consume(ConsumeContext<DeliveryOrderCreatedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Draft", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderSubmittedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Submitted", reason: null);

    public Task Consume(ConsumeContext<DeliveryOrderValidatedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.DeliveryOrderId, "Validated", reason: null);
    // ... existing handlers
}
```

**Note:** `Created` → ToStatus = `"Draft"` (state at creation per `DeliveryOrder.Create()`). Verify via reading `DeliveryOrder.cs`.

**Tests:** เพิ่ม unit test ใน `OrderStatusHistoryProjectorTests.cs` คล้ายของเดิมแต่ใช้ 3 events ใหม่.

**Acceptance:**
- รัน `dotnet test` ผ่านครบ
- Trigger new order ผ่าน `POST /api/v1/delivery-orders` → 10s ภายหลัง `order_status_history` มี row `null→Draft`
- Status timeline ของ Order ที่สร้างหลัง deploy แสดง Draft → Submitted → Validated → Confirmed

---

### 1.3 TripStatusHistoryProjector +RobotPassAcknowledged — Step 3

แก้ `TripStatusHistoryProjector.cs`:

```csharp
public class TripStatusHistoryProjector :
    IConsumer<TripStartedIntegrationEvent>,
    // ... existing 7 events
    IConsumer<TripRobotPassAcknowledgedIntegrationEventV1>   // NEW
{
    public Task Consume(ConsumeContext<TripRobotPassAcknowledgedIntegrationEventV1> ctx)
        // Trip.Status doesn't change — we re-record the current ToStatus
        // but flag in Reason that the operator intervened.
        => Project(ctx, ctx.Message.TripId,
            deliveryOrderId: null,        // pulled from latest row
            jobId: null,                  // pulled from latest row
            toStatus: "InProgress",       // unchanged but recorded
            reason: $"Robot pass acknowledged (vehicleKey: {ctx.Message.VendorVehicleKey})");
}
```

**Caveat:** ToStatus เท่ากับ FromStatus (InProgress → InProgress) — `TripStatusHistoryProjector.Project` มี out-of-order guard ที่อาจต้อง bypass สำหรับ marker-only events. ตัวเลือก:
- **(a)** ใส่ `toStatus: "InProgress"` แล้ว guard ผ่านได้เพราะ `OccurredAt > prev.OccurredAt`
- **(b)** เพิ่ม special handling ที่ skip status comparison เลย

แนะนำ **(a)** เพราะลด complexity.

**Tests:** Unit test ใน `TripStatusHistoryProjectorTests.cs` — robot pass event → row appended with reason mentioning vehicleKey.

**Acceptance:** Trip detail drawer แสดง entry "Robot pass acknowledged" เมื่อ ops กด pass-through button (P0+P1 SignalR push จะทำให้ขึ้น live).

---

## 2. UX Decision (Step 4) — ต้องการ user confirm

P1 + existing setup ตอนนี้มี **2 timeline components** ใน Order detail drawer ที่ overlap:

| Component | Source | Realtime? | Coverage |
|---|---|---|---|
| `StatusTimelineSection` (P1) | `order_status_history` (status_history projector) | ✅ via OrderHub.TimelineUpdated | Status transitions only (14 incl. P2 housekeeping) |
| `FullAuditLog` (existing) | `OrderActivity` (activity projector) | ❌ REST polling only | Status + Amendment + TripExecution + TripRetry + (P2-added) PodCaptured + OMS + RobotPass |

**Problem:** 80% data overlap, 2 vertical sections, 2 different "live" behaviors (one live one polling)

### Options

| Option | Description | Pros | Cons |
|---|---|---|---|
| **A: ลบ `StatusTimelineSection`, ทำให้ `FullAuditLog` live (Recommended)** | FullAuditLog เป็น single timeline + SignalR live. มี filter chips สำหรับ "Status only" view | Single component, FullAuditLog เป็น superset ของ status, filter "Status" = สิ่งที่ StatusTimelineSection ให้ | ลบ P1 component ที่เพิ่ง ship — แต่ data + flow ไม่หาย, แค่ retire wrapper |
| **B: ขยาย `StatusTimelineSection` ให้รับ category, retire `FullAuditLog`** | StatusTimelineSection กลายเป็น universal timeline | ใช้ TimelineView pattern เดียวกัน | ต้องเพิ่ม filter chips + trip-click handler ใน StatusTimelineSection = re-implement สิ่งที่ FullAuditLog มีแล้ว |
| **C: เก็บทั้ง 2 component ทำ FullAuditLog ให้ live ด้วย** | Status timeline = "quick glance", FullAuditLog = "deep dive" | Two distinct UX layers | 2 components ต้อง maintain, data overlap 80%, 2 sets of CSS, more vertical space |

**คำแนะนำ:** **Option A** — FullAuditLog เป็น superset อยู่แล้ว, มี filter chips + trip-click handler พร้อม, แค่เพิ่ม SignalR live = ได้ unified live timeline เต็มรูปแบบ. ลบ StatusTimelineSection จาก 3 detail drawers (Order/Job/Trip) → footprint ลดลง.

**ถ้าเลือก A:**
- เพิ่ม `liveEntry` prop ใน FullAuditLog (เหมือนที่ทำใน StatusTimelineSection)
- Order drawer: ลบ StatusTimelineSection block, FullAuditLog ใช้ `useOrderHubSubscription({ ActivityUpdated })` แทน `TimelineUpdated`
- Job/Trip drawer: ต้องสร้าง FullAuditLog equivalent (`JobActivityLog`, `TripActivityLog`) — หรือ extend FullAuditLog ให้ generic

**ถ้า Job/Trip ไม่มี ActivityProjector:**
- เก็บ StatusTimelineSection ไว้สำหรับ Job/Trip เฉพาะ (P1 ส่วน Order ลบ)
- Activity timeline เป็น **Order-only feature** ใน P2 (Job/Trip activity ทำใน phase หลัง)

> ✅ **Decided 2026-06-15:** Option A — ลบ `StatusTimelineSection` จาก Order detail drawer, ทำ `FullAuditLog` live ผ่าน `OrderHub.ActivityUpdated`. Job/Trip drawer ยังคง `StatusTimelineSection` ไว้ก่อน (Activity projector สำหรับ Job/Trip เป็น phase หลัง)

---

## 3. P2 Backend Implementation (Step 5) — 1 วัน

### 3.1 Extend `IOrderRealtimePublisher`

**`src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Projections/IOrderRealtimePublisher.cs`:**

```csharp
public interface IOrderRealtimePublisher
{
    Task PublishTimelineUpdatedAsync(Guid orderId, OrderTimelineEntryDto entry, CancellationToken ct = default);

    // NEW (P2)
    Task PublishActivityUpdatedAsync(Guid orderId, OrderActivityEntryDto entry, CancellationToken ct = default);
}

public sealed record OrderActivityEntryDto(
    Guid EventId,
    Guid OrderId,
    string Category,    // "OrderLifecycle" | "TripExecution" | "Amendment" | "Pod" | "OmsNotify"
    string Summary,     // human-readable line (matches FullAuditLog "title" column)
    string? Severity,   // "info" | "warning" | "error" (drives color)
    DateTime OccurredAt,
    Guid? TripId,       // for trip-click navigation
    string? TriggeredBy,
    string? Reason);
```

**`SignalROrderRealtimePublisher`:**
```csharp
public async Task PublishActivityUpdatedAsync(Guid orderId, OrderActivityEntryDto entry, CancellationToken ct = default)
{
    try
    {
        await _hub.Clients
            .Group(OrderHub.GroupKey(orderId))
            .ActivityUpdated(entry);   // IOrderClient.ActivityUpdated already exists (P0 Day 3)
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to push ActivityUpdated for Order {OrderId}", orderId);
    }
}
```

`NoopOrderRealtimePublisher` ก็เพิ่ม no-op method ให้ครบ interface.

### 3.2 Wire push ใน `OrderActivityProjector`

Inject `IOrderRealtimePublisher` → หลัง `_store.AppendAsync` สำเร็จ:

```csharp
_ = _realtime.PublishActivityUpdatedAsync(
    orderId,
    new OrderActivityEntryDto(
        EventId: evt.EventId,
        OrderId: orderId,
        Category: category,    // CatOrderLifecycle/TripExecution/Amendment
        Summary: summary,
        Severity: severity,
        OccurredAt: evt.OccurredOn,
        TripId: tripId,
        TriggeredBy: GetTriggeredBy(evt),
        Reason: reason),
    ct);
```

**Note:** `OrderActivityProjector.Project` ต้องส่ง `tripId` + severity ลงไป (เป็น column ใน `OrderActivity` row อยู่แล้ว). Mapping จาก event → severity:
- error: `Failed`, `Cancelled`, `Rejected`, `ExceptionRaised`
- warning: `Held`, `Amended`, `RobotPassAcknowledged`
- info: ทุกอย่างที่เหลือ

### 3.3 ปิด coverage gap — 5 events

เพิ่ม `IConsumer<TEvent>` + handler ใน OrderActivityProjector:

| Event | Category | Summary template | Severity |
|---|---|---|---|
| `DeliveryOrderCreatedIntegrationEventV1` | OrderLifecycle | "Order created" | info |
| `DeliveryOrderSubmittedIntegrationEventV1` | OrderLifecycle | "Order submitted" | info |
| `DeliveryOrderValidatedIntegrationEventV1` | OrderLifecycle | "Order validated" | info |
| `TripRobotPassAcknowledgedIntegrationEventV1` | TripExecution | "Robot checkpoint pass acknowledged" | warning |
| `PodCapturedIntegrationEvent` | Pod (new category const) | "POD captured at stop X" | info |

เพิ่ม `private const string CatPod = "Pod";` ที่ classes constants.

### 3.4 Tests

- Unit tests สำหรับ 5 event handlers ใน `OrderActivityProjectorTests.cs`
- Publisher test: `IOrderRealtimePublisher.PublishActivityUpdatedAsync` called once per successful AppendAsync (NSubstitute)

### 3.5 Build verify

```bash
dotnet build && dotnet test
```

---

## 4. P2 Frontend Implementation — 0.5-1 วัน (ขึ้นกับ UX decision)

### ถ้าเลือก Option A (Recommended)

**`frontend/components/delivery-orders/full-audit-log.tsx`:**
1. เพิ่ม `liveEntry?: FullAuditEntryDto | null` prop
2. `useEffect` ที่ dedup-merge `liveEntry` เข้า `data.entries` ตาม `eventId`
3. Sort หลัง merge เพื่อรักษา newest-first

**`frontend/lib/realtime/hubs/order-hub.ts`:**
```typescript
export type OrderHubEvents = {
  TimelineUpdated?: (entry: unknown) => void;
  StatusChanged?: (change: unknown) => void;
  ActivityUpdated?: (entry: unknown) => void;   // already declared, just wire usage
};
```

**`frontend/components/delivery-orders/detail-drawer.tsx`:**
1. เปลี่ยน hub event handler จาก `TimelineUpdated` → `ActivityUpdated`
2. ลบ block `<StatusTimelineSection ... />` (P1 wrapper ที่ถูก retire)
3. Pass `liveEntry` ให้ `<FullAuditLog />`:

```tsx
const [liveActivityEntry, setLiveActivityEntry] = useState<FullAuditEntryDto | null>(null);
useOrderHubSubscription(orderId, {
  ActivityUpdated: (entry) => setLiveActivityEntry(entry as FullAuditEntryDto),
});

<FullAuditLog
  orderId={data.id}
  onOpenTrip={...}
  liveEntry={liveActivityEntry}   // NEW
/>
```

**Job/Trip drawer:** เก็บ `StatusTimelineSection` ไว้ก่อน (ไม่มี Job/Trip ActivityProjector — phase หลัง).

### Type-check + build verify

```bash
cd frontend && npx tsc --noEmit && npm run build
```

---

## 5. Docs Updates — 0.5 วัน

### 5.1 อัพเดต `docs/event-projection-implementation-plan.md`

- Phase status table: P2 → ✅ Done
- เพิ่ม link ไปยัง doc นี้

### 5.2 อัพเดต `docs/signalr-hub-catalog.md`

- เพิ่ม `IOrderClient.ActivityUpdated` ใน section §3 — producer = `OrderActivityProjector`, payload = `OrderActivityEntryDto`

### 5.3 อัพเดต `docs/projector-catalog.md`

- เพิ่ม row `OrderActivityProjector` (ถ้ายังไม่มี) — บอกว่า push to SignalR + ครอบคลุม 19 events

---

## 6. Effort Summary

| Step | Effort | Risk |
|---|---|---|
| §1.1 Stale comments | XS (15 min) | None |
| §1.2 OrderStatusHistory +3 events | S (1-2 hr) | Test fixture update |
| §1.3 TripStatusHistory +RobotPass | S (1-2 hr) | Out-of-order guard interaction |
| §2 UX decision | — | **Blocking — needs user input** |
| §3 Backend (publisher extension + push + 5 events) | S-M (3-4 hr) | Coverage tests |
| §4 Frontend (depends on UX) | S (2-3 hr) — Option A | Component retire affects 1 drawer |
| §5 Docs | XS (30 min) | None |

**Total: ~2-3 วัน** (single dev, sequential)

---

## 7. Risk Register

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | UX decision delays start | High | Confirm Option A/B/C ใน §2 ก่อนเริ่ม code |
| R2 | `TripRobotPassAcknowledged` skipped by out-of-order guard เพราะ ToStatus = FromStatus | Medium | Test scenario: PASS event followed by next PASS event same Trip — should both record |
| R3 | OrderActivityProjector pushed 2 events ต่อ 1 transition (status_history + activity) → double SignalR fan-out | Low | Rate limit + batching from P0 absorbs; metrics ดู before/after |
| R4 | Option A retires StatusTimelineSection ใน Order drawer แต่ Job/Trip ยังใช้ — inconsistent UX | Medium | Document clearly: "Activity timeline = Order-only ใน P2; Job/Trip activity = phase หลัง" |
| R5 | Frontend FullAuditLog ที่ยัง REST polling อยู่จะ race กับ live push (เห็น row ซ้ำ) | Medium | dedup-merge by eventId (เหมือนใน StatusTimelineSection) |
| R6 | `OrderActivityEntryDto` shape ที่ผม design อาจไม่ match `FullAuditEntryDto` ใน frontend | Medium | Check shape ก่อน — `liveEntry` ต้องเป็น `FullAuditEntryDto` ตรงๆ ไม่ใช่ DTO ใหม่ |

---

## 8. Acceptance Criteria — Phase P2 closure

- [ ] §1.1 — 2 projector comments accurate
- [ ] §1.2 — สร้าง order ใหม่ → status_history มี 4 rows (Draft→Submitted→Validated→Confirmed) ใน 10s
- [ ] §1.3 — Trip ที่มี robot pass → trip_status_history มี row "InProgress" with reason "Robot pass acknowledged..."
- [ ] §2 — UX decision documented + implemented
- [ ] §3 — OrderActivityProjector pushes `ActivityUpdated` after every `AppendAsync` (verified via unit test + manual trigger)
- [ ] §3 — Coverage: 5 missing events subscribed + projected (Created/Submitted/Validated/RobotPassAck/PodCaptured)
- [ ] §4 — Frontend live: เปิด Order drawer → trigger event → ใหม่ row animates in ภายใน 1s (no manual refresh)
- [ ] §5 — Docs ตรง
- [ ] No regression: ทุก unit test + integration test pass
- [ ] No regression: P1 SignalR Order/Job/Trip drawer ยังทำงาน

---

## 9. Next Phase (P3 — Dashboard Read Models)

หลัง P2 ปิดแล้ว, P3 จะใช้ pattern เดียวกัน:
- DashboardCounterBatcher (มีอยู่แล้วจาก P0 Day 4) เป็น push pipeline
- `IDashboardRealtimePublisher` interface — extend pattern ของ P2
- Counter / KPI / Funnel projectors + push to `DashboardHub.CountersUpdated`

---

## 10. Cross-references

- [event-projection-implementation-plan.md](event-projection-implementation-plan.md) §4 (Phase P2 original scope)
- [signalr-hub-catalog.md](signalr-hub-catalog.md) §3 (`IOrderClient.ActivityUpdated` declaration)
- [projector-catalog.md](projector-catalog.md) (full projector registry — may need P2 update)
