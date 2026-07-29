# RIOT3 ↔ DTMS Lifecycle Reference

> **จุดประสงค์:** map สถานะระหว่าง RIOT3 (vendor) กับ DTMS (`Trip.Status`, `TripMissionEvent`,
> Fleet vehicle state, `DeliveryOrder`) ให้อยู่ที่เดียว ใช้ debug / ออกแบบ guard / อ่าน webhook
>
> **หลักการสำคัญ:** `Trip.Status` เป็น **lossy projection** ของ RIOT3 — RIOT3 มี ~11 task events,
> 2 vocabulary (notify vs order-query), และ 3 ระดับ (order / mission / vehicle) แต่ DTMS ยุบ
> ระดับ order เหลือ **6 สถานะ** ส่วน mission กับ vehicle แยกเก็บคนละที่ ไม่ยุบเข้า `Trip.Status`

---

## ภาพรวม 4 ชั้น

```
┌─ RIOT3 (vendor) ────────────────────────────┐      ┌─ DTMS ───────────────────────────────┐
│                                              │      │                                       │
│  Level 1  Order    (task.state / orderState) │ ───► │  Trip.Status  (6 ค่า)                 │
│  Level 2  Mission  (SUB_TASK_*)              │ ───► │  TripMissionEvent  (timeline)         │
│  Level 3  Vehicle  (systemState / eStop)     │ ───► │  Fleet VehicleState  (ไม่แตะ Trip)    │
│                                              │      │                                       │
└──────────────────────────────────────────────┘     │      Trip domain events               │
                                                      │            │                          │
                                                      │            ▼                          │
                                                      │  DeliveryOrder.Status + ItemStatus    │
                                                      └───────────────────────────────────────┘
```

Webhook เข้าจุดเดียว: `POST /api/webhooks/riot3/notify/{secret?}` แล้วแยกด้วย `payload.Type`
(`task` / `subtask` / `vehicle`) → `Riot3Webhooks.cs`

---

## Level 1 — Order ↔ `Trip.Status`

`Trip.Status` มี 8 ค่า: `Created, InProgress, Hang, Held, Completed, Failed, Cancelled, Rejected`
(`DispatchEnums.cs`)
- `Rejected` เพิ่ม 2026-07-28: vendor ปฏิเสธก่อนได้ execute (แยกจาก `Failed` = ทำแล้วล้ม)
  ฝั่ง DeliveryOrder ตอบสนองเหมือน Failed ทุกประการ
- `Hang`/`Held` แทน `Paused` เดิม (แยก 2026-07-28): `Hang` = ระบบหยุดเอง (TASK_HANG),
  `Held` = คนสั่งหยุด (TASK_HELD / ปุ่ม Pause) — status เป็น source of truth ของคำสั่ง resume
  (`CONTINUE_FROM_HANG` vs `CONTINUE_FROM_HELD`, สลับกันโดน E639999); รองรับ **re-flavour**
  เมื่อ vendor เปลี่ยน HANG↔HELD กลางทาง (event ติดธง Reflavour, PauseCount ไม่บวกซ้ำ)

RIOT3 พูดถึง order ด้วย **2 vocabulary ที่ไม่ตรงกัน**:
- **notify webhook** → field `taskEventType` (เช่น `TASK_FINISHED`)
- **order-query GET** `/api/v4/orders/{key}?isUpper=true` → field `orderState` (เช่น `SUCCEEDED`)
  ใช้โดย reconciler poller เป็น safety-net เวลา webhook หาย

| RIOT3 notify (`taskEventType`) | RIOT3 query (`orderState`) | Trip method | `Trip.Status` ผลลัพธ์ |
|---|---|---|---|
| `TASK_CREATE`   | `CREATE`    | *(ignore)* | คง `Created` |
| `TASK_QUEUEING` | `QUEUEING`  | *(ignore)* | คง `Created` |
| `TASK_PROCESSING` | `PROCESSING` | `MarkVendorStarted()` | **`InProgress`** |
| `TASK_FINISHED` | **`SUCCEEDED`** ⚠️ | `MarkVendorCompleted()` | **`Completed`** |
| `TASK_FAILED`   | `FAILED`    | `MarkVendorFailed()` | **`Failed`** |
| `TASK_REJECTED` | `REJECTED`  | `MarkVendorRejected()` | **`Rejected`** |
| `TASK_CANCELED` | **`CANCELLED`** ⚠️ | `Cancel()` | **`Cancelled`** |
| `TASK_HANG`     | `HANG`      | `Pause(Hang)` | **`Hang`** |
| `TASK_HELD`     | `HELD`      | `Pause(Held)` | **`Held`** |
| `TASK_HANG_TO_CONTINUE` | *(กลับเป็น `PROCESSING`)* | `Resume()` | **`InProgress`** |
| `TASK_HELD_TO_CONTINUE` | *(กลับเป็น `PROCESSING`)* | `Resume()` | **`InProgress`** |

⚠️ = vocabulary drift ระหว่าง 2 ช่องทาง — code ทั้ง webhook และ reconciler ต้องรับทั้งคู่
(`FINISHED`/`SUCCEEDED`, `CANCELED`/`CANCELLED`) ไม่งั้น reconciler จะมองไม่เห็น completion

### State diagram

```mermaid
stateDiagram-v2
    [*] --> Created: dispatch (pre-vendor, DTMS สร้าง Trip)
    Created --> InProgress: TASK_PROCESSING / orderState=PROCESSING
    InProgress --> Hang: TASK_HANG (system)
    InProgress --> Held: TASK_HELD (operator / ปุ่ม Pause)
    Hang --> Held: re-flavour (vendor drift)
    Held --> Hang: re-flavour (vendor drift)
    Hang --> InProgress: TASK_HANG_TO_CONTINUE / Resume()
    Held --> InProgress: TASK_HELD_TO_CONTINUE / Resume()
    InProgress --> Completed: TASK_FINISHED / SUCCEEDED
    InProgress --> Failed: TASK_FAILED
    Created --> Rejected: TASK_REJECTED (ปฏิเสธก่อน execute)
    InProgress --> Rejected: TASK_REJECTED
    InProgress --> Cancelled: TASK_CANCELED
    Completed --> [*]
    Failed --> [*]
    Rejected --> [*]
    Cancelled --> [*]
```

### หมายเหตุพฤติกรรมสำคัญ

- **`Created` เป็น state ฝั่ง DTMS ล้วน** — เกิดตอนสั่ง dispatch ก่อน webhook ตัวแรกมา ไม่มี
  RIOT3 event ไหน map กลับมาเป็น `Created`. RIOT3 `CREATE`/`QUEUEING` ถูก **ignore** เพราะ Trip
  มีอยู่แล้ว (`Riot3Webhooks.cs` default case)
- **`TASK_PROCESSING` = allocation ไม่ใช่การลงมือทำ mission จริง** — order ถูก commit ให้หุ่นและ
  เข้าสถานะ running แต่หุ่นอาจยังไม่ขยับ. สัญญาณ "หุ่นทำ mission จริง" คือ Level 2 (`SUB_TASK_PROCESSING`)
  / Level 3 (`systemState=BUSY`)
- **`MarkVendorStarted()` flip status แค่ครั้งแรก** (`Created → InProgress`). `TASK_PROCESSING` ซ้ำ
  จะ **ไม่** re-fire event แต่ยังอัปเดต vehicle assignment (ดู reassignment ด้านล่าง)
- **Pause 2 flavor แยก status เต็มตัวแล้ว (2026-07-28)** — `Hang`/`Held` ตรงตาม vendor;
  resume เลือกคำสั่งจาก status ตรง ๆ (ส่งผิดได้ error `E639999`) `VendorPauseSource` เขียนคู่ขนาน
  ไว้เป็น rollback path 1 release แล้วจะ drop; แถวประวัติ `toStatus="Paused"` ยุคเก่าคงไว้ตามเดิม
- **Failure 3 flavor แยกสถานะครบ (ตั้งแต่ 2026-07-28)** → `TASK_FAILED` เป็น `Failed` (ทำแล้วล้ม);
  `TASK_REJECTED` เป็น `Rejected` (ปฏิเสธก่อนได้ทำ — order ยัง propagate เป็น Failed เหมือนกัน,
  retry ผ่าน Reopen ได้เหมือนกัน); `TASK_CANCELED` เป็น `Cancelled` (ปล่อย DeliveryOrder ให้ re-dispatch ได้)
- **First-terminal-failure-wins** — `MarkVendorFailed`/`MarkVendorRejected` no-op เมื่อ Trip อยู่
  terminal ใดแล้ว (`Failed`/`Rejected`/`Cancelled`): webhook กับ reconciler ส่ง terminal ขัดกันได้
  ตัวที่มาทีหลังห้าม flip status หรือยิง order-failing event ซ้ำ **รวมถึง fix เดิม:
  `TASK_FAILED` ที่มาช้าหลัง operator cancel จะไม่ทับ `Cancelled` เป็น `Failed` อีกต่อไป**
  (เดิมทับได้ → order โดนลาก Failed ทั้งที่ cancel ปล่อย items คืนแล้ว)

### Vehicle reassignment (VendorVehicleKey เปลี่ยนได้ระหว่างทาง)

RIOT3 reassign หุ่นได้ (หุ่น A พัง → หุ่น B). ทุก `TASK_PROCESSING` เรียก
`AmrTripExtension.RecordVehicleAssignment()`:
- append เข้า history (`AmrVehicleAssignment`) + อัปเดต cache `VendorVehicleKey` (last-write-wins)
- idempotent — key+name เดิม = no-op
- **PASS / CANCEL ยิงไปที่ `VendorVehicleKey` (cache) ซึ่งเป็นตัวล่าสุดเสมอ**
- payload มี `appointVehicleKey` (หุ่นที่ระบุเจาะจง) แยกจาก `processingVehicle` (หุ่นที่ execute) —
  DTMS เก็บเฉพาะ `processingVehicle`

> ⚠️ **Gap:** การอัปเดต key พึ่ง webhook ล้วน ๆ ถ้า RIOT3 reassign แล้วไม่ส่ง `TASK_PROCESSING`
> ใหม่ (หรือ DTMS drop) → cache ค้าง (stale) → PASS ยิงหุ่นเก่า. reconciler poller ช่วย self-heal
> จาก `ResolvedVehicle` ของ order-query แต่เป็นการ poll ไม่ใช่ real-time

---

## Level 2 — Mission ↔ `TripMissionEvent`

`SUB_TASK_*` events **ไม่แตะ `Trip.Status`** — เก็บเป็น `TripMissionEvent` ไว้ทำ Mission Timeline
ใน operator drawer (`Riot3Webhooks.HandleSubTaskEvent`)

| RIOT3 (`taskEventType`) | `TripMissionEvent.State` |
|---|---|
| `SUB_TASK_PROCESSING` | `PROCESSING` |
| `SUB_TASK_FINISHED`   | `FINISHED` |
| `SUB_TASK_FAILED`     | `FAILED` |
| `SUB_TASK_CANCELED`   | `CANCELED` |

- mission type: `MOVE` (เคลื่อนที่) | `ACT` (ทำ action เช่น lift/load/dispense)
- **Pickup/Drop detection:** เมื่อ `MOVE`/`ACT` = `FINISHED` ที่ pickup หรือ drop station จะ fire
  domain event ให้ฝั่ง DeliveryOrder flip item status (ดู Level 4). resolve station ด้วย vendor
  station id (`VendorRef`) ไม่ใช่ชื่อ (RIOT3 casing ต่าง)
- idempotent ที่ repo: `UNIQUE (TripId, MissionKey, State)` — webhook ซ้ำ / reconciler upsert ซ้ำได้

---

## Level 3 — Vehicle ↔ Fleet (ไม่แตะ `Trip.Status`)

`vehicle` events emit เป็น integration event ไป Fleet module เท่านั้น **ไม่ผูกกับ Trip**
(`Riot3Webhooks.HandleVehicleEvent`)

| RIOT3 `systemState` | canonical (Fleet) |
|---|---|
| `IDLE` | `Idle` |
| `BUSY` / `RUNNING` / `EXECUTING` | `Moving` |
| `ERROR` | `Error` |
| `CHARGING` | `Charging` |
| *(อื่น ๆ)* | `Offline` |

- → `VehicleStateChangedIntegrationEvent` (+ battery %)
- `safetyState.eStop != "NONE"` (AUTOACK/MANUAL/REMOTE) = **emergency** → ปัจจุบัน **`LogWarning`
  เฉย ๆ** ไม่ผูกกับ Trip/PASS
- battery < 20% → `VehicleBatteryLowIntegrationEvent`

> ⚠️ **Gap (emergency vs PASS):** eStop / suspend มาทางช่องนี้ ซึ่ง **ไม่ map เข้า `Trip.Status`**
> ดังนั้น guard ของ `AcknowledgeRobotPass` (ที่เช็คแค่ `Status == InProgress`) มองไม่เห็น emergency
> ที่ไม่ได้ถูก mirror เป็น `TASK_HANG`. หุ่นโดน E-stop แต่ order ยังไม่ HANG → PASS ยิงออกได้ แล้วไป
> พึ่ง RIOT3 ปฏิเสธ. ถ้าจะอุด: เก็บ eStop/systemState ลง field ที่ผูกกับ trip/หุ่น แล้วให้ guard เช็คด้วย

---

## Level 4 — Trip → `DeliveryOrder` propagation

Trip fire domain/integration event → consumer ฝั่ง DeliveryOrder ขับ `OrderStatus` + `ItemStatus`

### OrderStatus flow (เต็ม)

```
Draft → Submitted → Validated → Confirmed → Planning → Planned → Dispatched → InProgress
                                                                                   │
                                              ┌────────────────────────────────────┤
                                              ▼            ▼               ▼        ▼
                                          Completed  PartiallyCompleted  Failed  (Cancelled)
```
ค่าอื่น: `Held` (block ชั่วคราว), `Cancelled`, `Rejected` (`OrderStatus.cs`)

### ItemStatus flow

```
Pending → Picked → DroppedOff → Delivered        (+ Failed, Returned, Cancelled)
                └─(ไม่ต้อง POD)──► Delivered
```

### Trip event → DeliveryOrder consumer

| Trip event / signal | Consumer | ทำอะไรกับ DeliveryOrder |
|---|---|---|
| `TripStartedIntegrationEvent` (จาก `MarkVendorStarted`) | `TripStartedConsumer` | `MarkInProgressIfNotYet()` → `Dispatched → InProgress` |
| Pickup ที่ pickup station | `TripPickupCompletedConsumer` | `MarkTripItemsPicked()` → items `Pending → Picked` |
| Drop ที่ drop station | `TripDropCompletedConsumer` | POD required: `Picked → DroppedOff`; ไม่ต้อง POD: `Picked → Delivered` |
| `TripCompleted` (จาก `MarkVendorCompleted`) | `TripCompletedConsumer` | `MarkTripItemsDeliveredOrLeaveForPod()` + `RecomputeStatusFromItems()` / `MarkAsCompleted()` → `Completed` / `PartiallyCompleted` / `Failed` |
| `TripFailed` (จาก `MarkVendorFailed`) | `TripFailedConsumer` | `MarkFailed()` → `Failed` |
| `TripRejected` (จาก `MarkVendorRejected`) | `TripFailedConsumer` (handler เดียวกัน) | เหมือน `TripFailed` ทุกประการ — ต่างแค่ Trip.Status/history/facts บันทึกเป็น `Rejected` |
| `TripCancelled` (จาก `Cancel`) | `TripCancelledConsumer` | ปล่อย items ออกจาก trip (`UnassignItemsFromTrip`) + `RecomputeStatusFromItems()`; **ไม่ auto-fail** — order ยัง eligible re-dispatch. cascade เป็น `Cancelled` เฉพาะเมื่อเป็น trip สุดท้ายและ order ยัง in-flight |

หมายเหตุ:
- `PartiallyCompleted` = terminal เมื่อ ≥1 item `Delivered` และ ≥1 item ไม่ `Delivered` ตอน trip จบ
- `TripCancelled` ต่างจาก `TripFailed`: cancel ไม่ทำ order เป็น Failed (ให้ re-dispatch), fail propagate ไป Failed

---

## Sequence — ตัวอย่าง flow จริง (end-to-end)

### A. Happy path: dispatch → pickup → drop → complete

```mermaid
sequenceDiagram
    autonumber
    participant D as DTMS Dispatch
    participant R as RIOT3
    participant WH as Webhook /notify
    participant T as Trip
    participant O as DeliveryOrder

    Note over O: Planned
    D->>R: POST /api/v4/orders (envelope, upperKey)
    R-->>D: 200 { orderKey }
    D->>T: create Trip + AttachVendorOrder(orderKey)
    Note over T: Created
    D->>O: MarkDispatched()
    Note over O: Dispatched

    R-->>WH: TASK_CREATE / TASK_QUEUEING
    Note over WH,T: ignore (Trip มีอยู่แล้ว)

    R-->>WH: TASK_PROCESSING (processingVehicle.key)
    WH->>T: MarkVendorStarted()
    Note over T: Created → InProgress<br/>บันทึก VendorVehicleKey
    T-->>O: TripStartedIntegrationEvent
    O->>O: MarkInProgressIfNotYet()
    Note over O: Dispatched → InProgress

    R-->>WH: SUB_TASK_FINISHED (MOVE/ACT @ pickup station)
    WH->>T: TripMissionEvent + pickup detection
    T-->>O: TripPickupCompleted
    O->>O: MarkTripItemsPicked()
    Note over O: items Pending → Picked

    R-->>WH: SUB_TASK_FINISHED (MOVE/ACT @ drop station)
    WH->>T: TripMissionEvent + drop detection
    T-->>O: TripDropCompleted
    O->>O: MarkTripItemsDelivered() / DroppedOff (POD)
    Note over O: items Picked → Delivered

    R-->>WH: TASK_FINISHED
    WH->>T: MarkVendorCompleted()
    Note over T: InProgress → Completed
    T-->>O: TripCompleted
    O->>O: RecomputeStatusFromItems() / MarkAsCompleted()
    Note over O: InProgress → Completed
```

> **จุดสังเกต:** `Trip.Status` เปลี่ยนแค่ 2 ครั้ง (`Created→InProgress→Completed`) — pickup/drop
> เป็น item-level ไม่แตะ `Trip.Status`. ถ้ามีบาง item ไม่ถึง drop ตอน `TASK_FINISHED`
> → `RecomputeStatusFromItems()` ให้ order เป็น `PartiallyCompleted` แทน `Completed`

### B. Branch: pause / resume + operator PASS

```mermaid
sequenceDiagram
    autonumber
    participant OP as Operator / Source
    participant API as DTMS API
    participant R as RIOT3
    participant WH as Webhook /notify
    participant T as Trip

    Note over T: InProgress

    R-->>WH: TASK_HANG (system) / TASK_HELD (operator)
    WH->>T: Pause(Hang|Held)
    Note over T: InProgress → Paused<br/>เก็บ VendorPauseSource

    R-->>WH: TASK_HANG_TO_CONTINUE / TASK_HELD_TO_CONTINUE
    WH->>T: Resume()
    Note over T: Paused → InProgress

    Note over OP,T: หุ่นรอที่ checkpoint — operator กด PASS
    OP->>API: POST /trips/{id}/acknowledge-robot-pass
    API->>T: AcknowledgeRobotPass()
    alt Trip.Status == InProgress และมี VendorVehicleKey
        T-->>API: ok (Status ไม่เปลี่ยน — เป็น nudge)
        API->>R: POST /api/v4/robots/operation (PASS, vehicleKey)
        alt RIOT3 code "0"
            R-->>API: Accepted
        else code อื่น / 404
            R-->>API: Rejected → "Vendor PASS failed"
        end
    else Trip Paused / ไม่มี key
        T-->>API: 400 "Only InProgress trips can acknowledge a robot pass"
    end
```

> **จุดสังเกต:** PASS เป็น nudge — `Trip.Status` ไม่เปลี่ยน. guard เห็นแค่ pause ที่มาทาง order
> channel (`TASK_HANG`/`TASK_HELD`). ถ้าหุ่นโดน **E-stop** (Level 3 vehicle) แต่ order ยังไม่ HANG
> → `Status` ยังเป็น `InProgress` → PASS ยิงออกได้ แล้วไปพึ่ง RIOT3 ปฏิเสธ (ดู Known gaps #1)

### C. Safety-net: reconciler เมื่อ webhook หาย

```mermaid
sequenceDiagram
    autonumber
    participant REC as Reconciler (poll)
    participant R as RIOT3
    participant T as Trip

    Note over T: InProgress (ค้าง — TASK_FINISHED webhook หาย)
    loop ทุก tick
        REC->>R: GET /api/v4/orders/{key}?isUpper=true
        R-->>REC: { orderState: "SUCCEEDED", executeVehicleKey }
        REC->>T: ApplyVendorState() → MarkVendorCompleted()
        Note over T: InProgress → Completed<br/>backfill vehicle จาก executeVehicleKey
    end
```

> **จุดสังเกต:** reconciler อ่าน `orderState` (vocab คนละชุด — `SUCCEEDED` ไม่ใช่ `FINISHED`) และ
> backfill `VendorVehicleKey` จาก `ResolvedVehicle` เมื่อ `TASK_PROCESSING` เดิมหาย

### D. Reassignment: หุ่น A พัง → หุ่น B รับช่วง

```mermaid
sequenceDiagram
    autonumber
    participant OP as Operator
    participant API as DTMS API
    participant R as RIOT3
    participant WH as Webhook /notify
    participant T as Trip
    participant EX as AmrTripExtension

    Note over T,EX: InProgress — VendorVehicleKey = A (seq 1)

    R-->>WH: TASK_PROCESSING (processingVehicle.key = A) [ซ้ำ]
    WH->>T: MarkVendorStarted()
    Note over T: Status ยัง InProgress (ไม่ re-fire)
    T->>EX: RecordVehicleAssignment(A, "TASK_PROCESSING")
    Note over EX: key เดิม → no-op (idempotent)

    Note over R: หุ่น A พัง / offline — RIOT3 reassign ให้ B
    R-->>WH: TASK_PROCESSING (processingVehicle.key = B)
    WH->>T: MarkVendorStarted()
    T->>EX: RecordVehicleAssignment(B, "TASK_PROCESSING")
    Note over EX: append seq 2 = B<br/>cache VendorVehicleKey → B

    OP->>API: POST /trips/{id}/acknowledge-robot-pass
    API->>R: PASS → vehicleKey = B ✅
    Note over API,R: ยิงหุ่นล่าสุด (B) ไม่ใช่ A
```

> **จุดสังเกต:** status ไม่เปลี่ยน (flip แค่ครั้งแรกตอน Created→InProgress) — reassignment แค่ต่อ
> history + ขยับ cache pointer. PASS/CANCEL อ่าน cache `VendorVehicleKey` จึงยิงหุ่น B เสมอ.
> ถ้า `TASK_PROCESSING` ของ B **หาย** → cache ค้างที่ A → reconciler self-heal จาก
> `ResolvedVehicle` (source = `VehicleReconciled`) ดู Known gaps #2

### E. Cancel / Fail → item propagate เป็น PartiallyCompleted

Multi-item order (item1, item2): pickup ทั้งคู่ → item1 ถึง drop สำเร็จ แต่ item2 ไม่ถึง

```mermaid
sequenceDiagram
    autonumber
    participant R as RIOT3
    participant WH as Webhook /notify
    participant T as Trip
    participant O as DeliveryOrder

    Note over O: InProgress — item1, item2 = Picked

    R-->>WH: SUB_TASK_FINISHED (drop station, item1)
    WH->>T: drop detection
    T-->>O: TripDropCompleted
    O->>O: MarkTripItemsDelivered()
    Note over O: item1 Picked → Delivered<br/>item2 ยัง Picked

    alt Trip จบด้วย TASK_FINISHED (item2 ตกค้าง)
        R-->>WH: TASK_FINISHED
        WH->>T: MarkVendorCompleted()
        Note over T: InProgress → Completed
        T-->>O: TripCompleted
        O->>O: MarkTripItemsDeliveredOrLeaveForPod()<br/>+ RecomputeStatusFromItems()
        Note over O: item1 Delivered + item2 ไม่ Delivered<br/>→ PartiallyCompleted (terminal)
    else Trip ล้มทั้งใบด้วย TASK_FAILED
        R-->>WH: TASK_FAILED
        WH->>T: MarkVendorFailed()
        Note over T: InProgress → Failed
        T-->>O: TripFailed
        O->>O: MarkFailed()
        Note over O: → Failed (ทั้งใบ)
    else Trip ถูกยกเลิกด้วย TASK_CANCELED
        R-->>WH: TASK_CANCELED
        WH->>T: Cancel()
        Note over T: InProgress → Cancelled
        T-->>O: TripCancelled
        O->>O: UnassignItemsFromTrip()<br/>+ RecomputeStatusFromItems()
        Note over O: ปล่อย items คืน — ไม่ auto-fail<br/>order ยัง eligible re-dispatch<br/>(cascade Cancelled เฉพาะ trip สุดท้าย)
    end
```

> **จุดสังเกต:** 3 ปลายทางต่างกันชัด —
> **`TASK_FINISHED` + item ตกค้าง → `PartiallyCompleted`** (order ยังนับว่าจบงาน),
> **`TASK_FAILED` → `Failed` ทั้งใบ** (propagate ไป order),
> **`TASK_CANCELED` → ปล่อย items คืน ไม่ fail** (ให้ re-dispatch). `PartiallyCompleted` เกิดจาก
> `RecomputeStatusFromItems()` เมื่อมี ≥1 item `Delivered` และ ≥1 item ไม่ `Delivered` ตอน trip จบ

---

## Known gaps / caveats (สรุป)

1. **Emergency ↔ PASS** — eStop/suspend อยู่ Level 3 (vehicle) ไม่ map เข้า `Trip.Status` → guard
   ของ PASS มองไม่เห็น (ดู Level 3)
2. **Vehicle key stale** — reassignment พึ่ง `TASK_PROCESSING` webhook; หายเมื่อไหร่ cache ค้าง →
   PASS/CANCEL อาจยิงหุ่นผิด (reconciler self-heal แบบ poll)
3. **Vocabulary drift** — `FINISHED`/`SUCCEEDED`, `CANCELED`/`CANCELLED` ต้องรับทั้งคู่ทุกที่ที่อ่าน state
4. **Webhook loss** — ทุก transition พึ่ง webhook; reconciler poller (`Riot3ReconciliationService`)
   เป็น safety-net อ่าน order-query แล้ว re-apply. **ตั้งแต่ 2026-07-29 มี alert คุมช่องทางเงียบ**:
   `Riot3NotifyChannelSilent` (P1) fire เมื่อไม่มีเฟรม notify ใด ๆ เกิน 3 นาทีขณะมี trip in-flight
   (`ops/prometheus/rules/webhook-silence.yml` — บทเรียนจาก outage 24 ก.ค. ที่เงียบ 2.5 ชม. โดยไม่มีใครรู้)
5. **`Trip.Status` หยาบ** — อยากรู้ "หุ่นทำ mission จริง" ต้องอ่าน Level 2/3 ไม่ใช่ `Trip.Status`

---

## Source of truth (file references)

| หัวข้อ | ไฟล์ |
|---|---|
| Webhook dispatch (3 levels) | `src/Modules/Transport.Amr/DTMS.Transport.Amr/Webhooks/Riot3Webhooks.cs` |
| RIOT3 notify payload (event list) | `src/Modules/Transport.Amr/DTMS.Transport.Amr/Models/Riot3NotifyPayload.cs` |
| RIOT3 order-query (orderState vocab) | `src/Modules/Transport.Amr/DTMS.Transport.Amr/Models/Riot3OrderQueryResponse.cs` |
| Reconciler (orderState → transition) | `src/Modules/Transport.Amr/DTMS.Transport.Amr/Services/Riot3ReconciliationService.cs` |
| `Trip.Status` enum + `VendorPauseSource` | `src/Modules/Dispatch/DTMS.Dispatch.Domain/Enums/DispatchEnums.cs` |
| Trip domain transitions | `src/Modules/Dispatch/DTMS.Dispatch.Domain/Entities/Trip.cs` |
| Vehicle assignment / reassignment | `src/Modules/Dispatch/DTMS.Dispatch.Domain/Entities/AmrTripExtension.cs` |
| `OrderStatus` / `ItemStatus` enum | `src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Domain/Enums/OrderStatus.cs`, `ItemStatus.cs` |
| DeliveryOrder transitions | `src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Domain/Entities/DeliveryOrder.cs` |
| Trip→Order consumers | `src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Consumers/Trip*Consumer.cs` |
