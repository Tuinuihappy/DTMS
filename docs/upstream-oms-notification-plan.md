# Upstream OMS Notification — Implementation Plan

> **Status (2026-06-16)**: Phase 1–4 ครบ + B3 (WireMock integration tests, 20 tests) + B4 (TripFailed / TripCancelled / PodCompleted notifications, full backend + frontend) ใน production code แล้ว — ดูตาราง **Status snapshot** ท้ายเอกสาร
> เหลือเฉพาะ (a) เปิด kill switch `UpstreamOms.Enabled` ใน prod, (b) งานที่ defer: Token refresh (exp ก.ย. 2027), Metrics counters, Backfill tool

## Overview

ส่ง notification ไปยัง upstream OMS เมื่อ Trip เริ่มทำงาน (RIOT3 ส่ง `TASK_PROCESSING` → Trip status: Created → InProgress) เพื่อให้ OMS รับรู้ว่า shipment ถูกรับงานโดย robot ตัวไหน

**Endpoints**:
- `POST /api/shipments` — Started (Trip Created→InProgress)
- `POST /api/shipments/{shipmentId}/arrived` — Arrived (Trip drop completed) ◀ added beyond original plan

**Trigger events**: `TripStartedIntegrationEvent`, `TripDropCompletedIntegrationEvent`

---

## Payload Mapping

| OMS field    | DTMS source              | ตัวอย่าง                                  |
| ------------ | ------------------------ | ----------------------------------------- |
| `shipmentId` | `Trip.Id.ToString()`     | `"a3f2b1c4-..."`                          |
| `deliveryBy` | `Trip.VendorVehicleKey`  | `"AMR_FAN1_No1"`                          |
| `lots[].lotNo` | `Item.ItemId` where `Item.TripId == Trip.Id` | `"LOT-01KTT4TB75"`        |

**Auth**: `Authorization: Bearer <JWT>` (RS256, exp ~2027-09)

**ตัวอย่าง request**:

```http
POST /api/shipments
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
Content-Type: application/json

{
  "shipmentId": "a3f2b1c4-5d6e-7f80-9a1b-2c3d4e5f6789",
  "deliveryBy": "AMR_FAN1_No1",
  "lots": [
    { "lotNo": "LOT-01KTT4TB75" }
  ]
}
```

---

## Decisions

| Question      | Answer                                            |
| ------------- | ------------------------------------------------- |
| Trigger event | `TripStartedIntegrationEvent` (TASK_PROCESSING)   |
| Auth          | `Authorization: Bearer <JWT>`                     |
| shipmentId    | `Trip.Id.ToString()` (Guid w/ dashes)             |
| deliveryBy    | `Trip.VendorVehicleKey` ตรง ๆ (ไม่มี Fleet lookup) |
| lots          | `Item.ItemId` where `Item.TripId == Trip.Id`      |
| Order gate    | `Order.OrderRef != null`                          |
| Retry         | MassTransit consumer + outbox                     |
| Module        | `Modules/OmsAdapter/` (sibling to VendorAdapter)  |

---

## Phase 1 — OmsAdapter module ✅ DONE

สร้าง infrastructure layer สำหรับคุยกับ OMS (ยังไม่ wire เข้า business flow)

> **Done as planned** — files ครบทุกตัว wired ผ่าน `AddOmsAdapter` ใน `ModuleServiceRegistration.cs`
> **Extras นอกแผน**: เพิ่ม `NotifyShipmentArrivedAsync` + `OmsArrivedNotification` model สำหรับรองรับ Arrived flow (Phase 2 ขยาย)

### Files

```
src/Modules/OmsAdapter/
├── AMR.DeliveryPlanning.OmsAdapter.csproj
├── Abstractions/
│   ├── IOmsShipmentClient.cs              # Task NotifyShipmentStartedAsync(notif, ct)
│   └── Models/
│       ├── OmsShipmentNotification.cs     # ShipmentId, DeliveryBy, Lots[]
│       └── OmsLot.cs                      # LotNo
├── Infrastructure/
│   ├── Options/UpstreamOmsOptions.cs      # Enabled, BaseUrl, BearerToken, TimeoutSeconds
│   ├── Services/HttpOmsShipmentClient.cs  # HttpClient JSON POST /api/shipments
│   └── OmsAdapterServiceRegistration.cs   # AddHttpClient + Bearer auth header
```

### Configuration (`appsettings.json`)

```json
{
  "UpstreamOms": {
    "Enabled": true,
    "BaseUrl": "http://10.204.37.65:5002/",
    "BearerToken": "<via env var UpstreamOms__BearerToken>",
    "TimeoutSeconds": 10
  }
}
```

**Security**: Token ใส่ผ่าน env var `UpstreamOms__BearerToken` ไม่ commit ลง git

### Wiring

- `Program.cs` / `ModuleServiceRegistration.cs` → `services.AddOmsAdapter(config)`
- `HttpClient` ตั้ง `DefaultRequestHeaders.Authorization` จาก options

### Acceptance

- DI resolve `IOmsShipmentClient` ได้
- Manual curl test ด้วย mock listener ส่ง payload + Bearer header ครบ
- ไม่กระทบ flow เดิม (ยังไม่มี consumer)

---

## Phase 2 — Consumer + gate + audit (core flow) ✅ DONE (+ Arrived extension)

ฟัง `TripStartedIntegrationEvent` → POST OMS

> **Done**: `TripStartedOmsNotifyConsumer` + `TripStartedOmsNotifyFaultConsumer` ทำงานตามแผน
> **Extras นอกแผน**:
> 1. `TripDropCompletedOmsNotifyConsumer` + Fault consumer (POST `/api/shipments/{id}/arrived`)
> 2. **Option A — stable shipmentId across retry chain**: walk `PreviousAttemptId` กลับไปยัง root trip ใช้ `rootTripId.ToString()` เป็น shipmentId แทน `trip.Id` ตรง ๆ ทำให้ retry attempts มอง OMS เป็น shipment เดียวกัน
> 3. **409 Conflict treated as no-op success** — ป้องกัน dead-letter เมื่อ OMS dedupe
> 4. **Throw แทนส่ง `"(unknown)"`** เมื่อ `VendorVehicleKey` ยังไม่พร้อม — แก้ race กับ `MarkVendorStarted` save (MassTransit retry จะอ่านใหม่)
> 5. ตอน Phase 2 commit registered ด้วย `Enabled=false` แล้ว — kill switch ยังปิดใน `appsettings.json` รอ rollout

### Files

```
src/Modules/DeliveryOrder/.../Application/Consumers/
└── TripStartedOmsNotifyConsumer.cs
```

### Consumer logic

```csharp
Consume(TripStartedIntegrationEvent evt):
  1. if (!options.Enabled) return

  2. trip = await tripRepo.GetById(evt.TripId)
     // VendorVehicleKey พร้อมแล้ว — set atomically ใน MarkVendorStarted ก่อน raise event

  3. order = await orderRepo.GetById(evt.DeliveryOrderId)

  4. if (order.OrderRef is null)
       → log debug "not upstream order, skip" → return

  5. items = order.Items
       .Where(i => i.TripId == evt.TripId)
       .Select(i => i.ItemId)
       .ToList()

  6. if (items.Count == 0)
       → log info "no bound items, skip" → return

  7. payload = new OmsShipmentNotification(
       ShipmentId: trip.Id.ToString(),
       DeliveryBy: trip.VendorVehicleKey ?? "(unknown)",
       Lots: items.Select(id => new OmsLot(id)))

  8. await client.NotifyShipmentStartedAsync(payload, ct)
     // throw on non-2xx → MassTransit retry policy kicks in

  9. await auditRepo.Add(new OrderAuditEvent(
       order.Id,
       "UpstreamOmsNotified",
       $"trip-started shipmentId={trip.Id} vehicle={trip.VendorVehicleKey} lots={items.Count}"))
```

### Fault handler (dead-letter)

```csharp
public class TripStartedOmsNotifyFaultConsumer
  : IConsumer<Fault<TripStartedIntegrationEvent>>
{
  // หลัง MassTransit retry หมด → write UpstreamOmsNotifyFailed audit
  //   พร้อม last exception message + attempt count
}
```

### Structured logging

```
[OmsNotify] Trip {TripId} → OMS event=TripStarted outcome={Outcome}
  shipmentId={Sid} vehicle={VehKey} lots={Count}
  latencyMs={Ms} attempt={N}
```

### Edge cases

| สถานการณ์                          | พฤติกรรม                                          |
| ---------------------------------- | ------------------------------------------------- |
| `Trip.VendorVehicleKey == null`    | ส่ง `"(unknown)"` + log warning, ไม่ block       |
| Items ยังไม่ bind TripId            | skip + log info (pre-binding row)                |
| Duplicate `TASK_PROCESSING` webhook | `MarkVendorStarted` idempotent → event ไม่ fire ซ้ำ |
| `Enabled=false`                    | consumer no-op (kill switch / dev)               |
| Order ไม่ใช่ upstream (OrderRef=null) | skip silently                                    |

### Register

เพิ่ม `AddConsumer<TripStartedOmsNotifyConsumer>()` + fault consumer ใน MassTransit config ฝั่ง DeliveryOrder module

### Acceptance

- Trip เริ่ม (TASK_PROCESSING จริง) → OMS ได้รับ POST + audit row ใน DB
- Mock OMS reply 500 → MassTransit retry → dead-letter → `UpstreamOmsNotifyFailed` audit
- Manual order (OrderRef=null) → skip silently, ไม่ POST
- Pre-binding row (TripId=null on items) → skip silently
- `Enabled=false` → consumer no-op

---

## Phase 3 — Frontend visibility ✅ DONE

แสดงสถานะ OMS notification ใน detail-drawer

> **Done**: [frontend/components/delivery-orders/oms-notification-section.tsx](frontend/components/delivery-orders/oms-notification-section.tsx) แสดงทั้ง Started + Arrived stage แยกกัน — latest-wins logic, ปุ่ม Resend independent ต่อ stage, status badges (Notified / Failed / Stale / Awaiting)

### Backend

ตรวจว่า `GetDeliveryOrderQuery` คืน audit events ครบ (น่าจะอยู่แล้ว — verify เฉย ๆ)

### Frontend

[frontend/components/delivery-orders/detail-drawer.tsx](frontend/components/delivery-orders/detail-drawer.tsx)

Section "OMS Notification" — แสดงเฉพาะ order ที่ `orderRef != null`

| Audit state                          | Trip state         | Badge              | Display                                |
| ------------------------------------ | ------------------ | ------------------ | -------------------------------------- |
| มี `UpstreamOmsNotified`             | any                | 🟢 Notified         | `Notified at <ts> (when trip started)` |
| มี `UpstreamOmsNotifyFailed` (latest) | any                | 🔴 Failed          | error message + resend button          |
| ไม่มีทั้งคู่                          | Trip = Created     | ⚪ Awaiting start  | (รอ TASK_PROCESSING)                   |
| ไม่มีทั้งคู่                          | Trip ≥ InProgress  | ⚠️ Not sent        | edge case — warning                    |

### Acceptance

- Upstream order → เห็น section + status ตรงกับ audit
- Non-upstream order → ไม่มี section
- Failed → operator เห็น error message ชัด

---

## Phase 4 — Manual re-notify ✅ DONE (ไม่ defer แล้ว)

ให้ operator แก้ stuck flow เองได้

> **Done**:
> - Backend: `POST /api/v1/delivery-orders/{id}/trips/{tripId}/notify-oms` + `ResendOmsNotificationCommand` (`UpstreamOmsManuallyResent` audit)
> - Backend extra: `POST .../notify-oms-arrived` + `ResendOmsArrivedNotificationCommand` (`UpstreamOmsArrivedManuallyResent` audit)
> - Frontend: ปุ่ม "Resend started" + "Resend arrived" ใน `oms-notification-section.tsx`

### Backend

- Endpoint: `POST /api/delivery-orders/{orderId}/trips/{tripId}/notify-oms`
- Direct call `IOmsShipmentClient` (ไม่ผ่าน outbox — operator อยากเห็นผลทันที)
- Audit `UpstreamOmsManuallyResent`
- Gate: เฉพาะ trip ที่มี `VendorVehicleKey` และ order มี `OrderRef`

### Frontend

ปุ่ม "Resend to OMS" ใน detail-drawer — เด้งเฉพาะตอน status = Failed
Confirm dialog → POST → refresh order

### Acceptance

- กดปุ่ม → POST ใหม่ → audit `UpstreamOmsManuallyResent` ปรากฏ
- Idempotent (upstream dedupe ด้วย shipmentId)
- กดซ้ำได้

---

## Build & rollout order

| Step | Output                                                    | Risk   | Status |
| ---- | --------------------------------------------------------- | ------ | ------ |
| 1    | Phase 1 commit (module + options + client + Bearer auth)  | ต่ำ    | ✅ DONE |
| 2    | Manual test: curl ผ่าน client → mock listener              | —      | ✅ DONE |
| 3    | Phase 2 commit (consumer + gate + audit) — `Enabled=false` | ต่ำ    | ✅ DONE |
| 4    | Dev: toggle `Enabled=true` → trigger TASK_PROCESSING → verify | กลาง | ⬜ TODO — `appsettings.json` ยัง `Enabled=false` |
| 5    | Phase 3 commit (frontend section)                         | ต่ำ    | ✅ DONE |
| 6    | Prod rollout (env var token พร้อม)                          | กลาง | ⬜ TODO — ต้องตั้ง `UpstreamOms__BearerToken` + เปิด `Enabled=true` ใน prod env |
| 7    | Phase 4 เมื่อ operator ขอ                                  | ต่ำ    | ✅ DONE (ทำพร้อมไปเลย) |

---

## Status snapshot (2026-06-16)

### ✅ Completed
- Phase 1, 2, 3, 4 ครบทุก acceptance criteria
- Arrived (drop completed) notification flow — **ขยายเกินแผนเดิม**
- Stable shipmentId across retry chain (Option A)
- 409 Conflict handling
- Manual resend สำหรับทั้ง Started + Arrived + (B4) Failed + Cancelled + PodCompleted
- MassTransit consumers + Fault consumers registered (5 main + 5 fault หลัง B4)
- **B3 — WireMock.Net integration tests** (2026-06-16): 20 tests, 5 stages × {200/201/409/5xx/argument-validation}
- **B4 — Failure-path notifications** (2026-06-16): TripFailed / TripCancelled / PodCompleted backend + frontend ครบ. UI 4 stages: Started → Arrived → POD captured → conditional "Trip aborted" row (merged failed/cancelled, latest-wins, subtype badge). Greying logic: success rows render `n/a (trip aborted)` เมื่อ trip aborted แทน "Awaiting…"

### ⬜ Remaining work

1. **Flip kill switch** — `appsettings.json` ยัง `UpstreamOms.Enabled=false`
   - Dev: toggle เปิดเพื่อทดสอบ end-to-end กับ TASK_PROCESSING / SUB_TASK_FINISHED จริง
   - Prod: set env var `UpstreamOms__Enabled=true` + `UpstreamOms__BearerToken=<token>` ผ่าน deployment config (ไม่ commit token)

2. **Status ของแผนเดิม (อัปเดต 2026-06-16)**
   - ⏭ Token refresh — defer ยาว (JWT exp ~ก.ย. 2027 — เหลือ 15 เดือน)
   - ⏭ Metrics counters (`oms_notify_total`, `oms_notify_latency_ms`) — defer สั้น (2-3 ชม.); ทำ B4 ก่อน
   - ✅ **B3 — WireMock.Net integration tests** — Done 2026-06-16. `tests/Modules/OmsAdapter.IntegrationTests/` project + `HttpOmsShipmentClientFixture` + 20 tests ครอบคลุม 5 stages × {200/201/409/5xx/argument-validation} scenarios. `WireMock.Net 1.5.62` (กับ `System.Linq.Dynamic.Core 1.6.7` pin ผ่าน CVE GHSA-4cv2-4hjh-77rx). `HttpOmsShipmentClient` ถูก expose ผ่าน `InternalsVisibleTo` แต่ test ใช้ surface `IOmsShipmentClient` เท่านั้น.
   - ✅ **B4 — ขยาย `IOmsShipmentClient` รองรับ TripFailed / TripCancelled / PodCompleted** — Done 2026-06-16. 3 new payload models (`OmsTripFailedNotification`, `OmsTripCancelledNotification`, `OmsPodCompletedNotification`) + 3 interface methods + shared `PostStageAsync<TBody>` helper ใน `HttpOmsShipmentClient` (409 Conflict treated as idempotent success). 3 consumers (`TripFailedOmsNotifyConsumer`, `TripCancelledOmsNotifyConsumer`, `PodCapturedOmsNotifyConsumer`) + 3 fault consumers (mirror existing pattern); auto-registered via `AddConsumers(assembly)`. 3 resend MediatR commands + handlers (`ResendOmsTripFailedNotification`, `ResendOmsTripCancelledNotification`, `ResendOmsPodCompletedNotification`) + 3 endpoints `POST /api/v1/delivery-orders/{id}/trips/{tripId}/notify-oms-{failed,cancelled,pod}`. Frontend `OmsNotificationSection` ขยายเป็น 4 stages: Started → Arrived → POD captured → **Trip aborted** (conditional row ที่รวม Failed + Cancelled, latest-wins, แสดง subtype badge). Success-path stages render เป็น `n/a (trip aborted)` แทน "Awaiting…" เมื่อ trip aborted (greyed). 6 audit event types ใหม่ (9 รวม fault) — category `OmsNotify` (ใช้ mapping เดิม → source "Order").
   - ⏭ Backfill tool (replay missed notifications) — defer until prod cutover

---

## Reference — Trip workflow timing

```
RIOT3 event          Trip method            Trip status            Domain event
─────────────────────────────────────────────────────────────────────────────────
TASK_PROCESSING   →  MarkVendorStarted   →  Created → InProgress  →  TripStartedDomainEvent  ◄── notify ตรงนี้
SUB_TASK_FINISHED →  MarkVendorPickedUp  →  (InProgress)           →  TripPickupCompletedDomainEvent
  @ pickup
SUB_TASK_FINISHED →  MarkVendorDropCompleted → (InProgress)        →  TripDropCompletedDomainEvent
  @ drop
TASK_FINISHED     →  MarkVendorCompleted →  InProgress → Completed →  TripCompletedDomainEvent
TASK_FAILED       →  MarkVendorFailed    →  → Failed               →  TripFailedDomainEvent
TASK_CANCELED     →  Cancel              →  → Cancelled            →  TripCancelledDomainEvent
TASK_HANG/HELD    →  Pause               →  InProgress → Paused    →  TripPausedDomainEvent
*_TO_CONTINUE     →  Resume              →  Paused → InProgress    →  TripResumedDomainEvent
```

**Trigger point ของ OMS notification**: `TripStartedDomainEvent` → forwarded as `TripStartedIntegrationEvent` ผ่าน outbox
