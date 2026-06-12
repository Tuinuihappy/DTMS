# Upstream OMS Notification — Implementation Plan

## Overview

ส่ง notification ไปยัง upstream OMS เมื่อ Trip เริ่มทำงาน (RIOT3 ส่ง `TASK_PROCESSING` → Trip status: Created → InProgress) เพื่อให้ OMS รับรู้ว่า shipment ถูกรับงานโดย robot ตัวไหน

**Endpoint**: `POST http://10.204.37.65:5002/api/shipments`

**Trigger event**: `TripStartedIntegrationEvent`

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

## Phase 1 — OmsAdapter module

สร้าง infrastructure layer สำหรับคุยกับ OMS (ยังไม่ wire เข้า business flow)

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

## Phase 2 — Consumer + gate + audit (core flow)

ฟัง `TripStartedIntegrationEvent` → POST OMS

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

## Phase 3 — Frontend visibility

แสดงสถานะ OMS notification ใน detail-drawer

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

## Phase 4 — Manual re-notify (defer until needed)

ให้ operator แก้ stuck flow เองได้

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

| Step | Output                                                    | Risk   |
| ---- | --------------------------------------------------------- | ------ |
| 1    | Phase 1 commit (module + options + client + Bearer auth)  | ต่ำ    |
| 2    | Manual test: curl ผ่าน client → mock listener              | —      |
| 3    | Phase 2 commit (consumer + gate + audit) — `Enabled=false` | ต่ำ    |
| 4    | Dev: toggle `Enabled=true` → trigger TASK_PROCESSING → verify | กลาง |
| 5    | Phase 3 commit (frontend section)                         | ต่ำ    |
| 6    | Prod rollout (env var token พร้อม)                          | กลาง |
| 7    | Phase 4 เมื่อ operator ขอ                                  | ต่ำ    |

---

## Defer (ไม่ทำตอนนี้)

- Token refresh (JWT exp ~9 เดือน — มีเวลาเตรียม)
- Metrics counters (`oms_notify_total`, `oms_notify_latency_ms`)
- Integration test ด้วย WireMock.Net
- ขยาย `IOmsShipmentClient` รองรับ TripFailed/Canceled/PodCompleted/DropCompleted
- Backfill tool (replay missed notifications)

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
