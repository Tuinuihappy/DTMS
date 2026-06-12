# Planning Workflow Roadmap — Remaining Options

ภาพรวมงานที่เหลือหลัง Phase b8 → b9 → b10-frontend.1 land แล้ว
จัดลำดับตาม priority × effort × impact เพื่อช่วยเลือก phase ถัดไป

อ้างอิงการ review แบบเต็มในเซสชั่นวันที่ 2026-06-11

---

## ที่ทำเสร็จแล้ว (ของจริงใน main)

| Phase | Scope | ผลลัพธ์ |
|---|---|---|
| **b8** | Job 1:1 envelope anchor + retry endpoint | `planning.Jobs` ทำงาน, `POST /jobs/{id}/retry` พร้อมใช้ |
| **b9** | Job mirrors Trip lifecycle (4 events) + Trip retry preserves JobId | Job.Status ตาม Trip จริง webhook กลับมา → verified E2E |
| **b10-frontend.1** | Job badge + Trip JobId chip ใน Order detail drawer | ops เห็น Job status + reason ใน UI |

---

## งานที่เหลือ — เรียงตาม priority

### 🔴 P0 — แก้ pain point ที่เจอใน production data

#### 1. Order ไม่ตาม Trip cancellation (stuck Dispatched bug)

**ปัญหา:** จาก data จริงตอนนี้ 3 orders ทุกตัวมี `Order=Dispatched + Trip=Cancelled`
Order ค้างใน "Dispatched" ตลอดกาลถึงแม้ Trip ตายไปแล้ว

**สาเหตุ:** [TripCancelledConsumer.cs](../src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Consumers/TripCancelledConsumer.cs)
รีลีส items กลับเป็น Pending เพื่อรอ retry แต่ไม่มีใคร "ปิด" Order
ถ้าไม่ retry → Order อยู่ Dispatched ไปเรื่อย ๆ

**แนวทาง:**
- **Option A** Background service detect "Order=Dispatched + 0 active Trips + items Pending > 1h" → escalate `Held`
- **Option B** Operator endpoint `POST /orders/{id}/abandon-after-trip-cancel` ให้กดเอง
- **Option C** Cascade tinggi: เมื่อ Trip Cancel ทุก Trip ใน group → Order ก็ Cancel ด้วย

**Effort:** M (1-2 วัน) | **Impact:** สูง (ลูกค้าเห็น UI ผิดจริง)

---

#### 2. StatusHistory tables — audit ที่ debug ได้

**ปัญหา:** ตอนนี้ตอบ "Order นี้ entered Planning ตอนไหน" → ต้อง parse text จาก `OrderAuditEvent.EventType`
ไม่มี structured status transition log → query เป็น `LIKE` เปราะ

**แนวทาง:**
```sql
CREATE TABLE deliveryorder."OrderStatusHistory" (
  Id uuid PRIMARY KEY,
  OrderId uuid NOT NULL,
  FromStatus varchar(20) NULL,  -- null = initial state
  ToStatus varchar(20) NOT NULL,
  OccurredAt timestamptz NOT NULL,
  TriggeredBy varchar(100) NULL,  -- user id / "system" / "vendor-webhook"
  Reason text NULL,
  CONSTRAINT IX UNIQUE (OrderId, OccurredAt)
);
```

ทำซ้ำกับ `planning.JobStatusHistory` + `dispatch.TripStatusHistory`

Aggregate write entry ทุกครั้ง state เปลี่ยน — หรือใช้ EF interceptor

**Effort:** L (2-3 วัน) | **Impact:** สูง (debug E2E ใหม่ง่ายขึ้นมาก)

---

### 🟡 P1 — Ops productivity

#### 3. b10-frontend.2 — Jobs queue page

**ขอบเขต:**
- `/delivery-orders/jobs` — list Pending + Failed jobs (filter tab)
- Job detail drawer (วินาทีเดียว: ใช้ JobDto ที่มีอยู่)
- Retry button (POST `/api/v1/planning/jobs/{id}/retry` — endpoint b8 พร้อมอยู่)
- Proxy routes: `frontend/app/api/planning/jobs/[id]/{,retry}/route.ts`

**Effort:** M (1-2 วัน) | **Impact:** สูง

**ทำไมยังไม่ทำใน b10-frontend.1:**
b10-frontend.1 ตั้งเป้าให้ ops "เห็นว่ามี Job" ก่อน b10-frontend.2 จะให้ "ทำงานกับ Job" ได้

---

#### 4. Job.FailureCategory enum

**ปัญหา:** ตอนนี้ Job.FailureReason เป็น text รวม 6 categories
ทำให้ query "ทุก Job ที่ vendor 429" ต้อง `LIKE 'Too Many Requests%'` — fragile

**แนวทาง:**
```csharp
public enum JobFailureCategory {
  None,                    // success path
  TemplateMissing,
  TemplateResolveFailed,
  VendorRejected,          // 4xx/5xx
  VendorRateLimited,       // 429
  VendorExecutionFailed,   // Phase b9 — TripFailed webhook
  TripPersistenceFailed,   // orphan
  OperatorCancelled,       // Phase b9 — TripCancelled webhook
}
```

เพิ่ม column `FailureCategory` + migration + อัพเดต MarkFailed/MarkCancelled ให้รับ category

**Effort:** S (4-6 ชม) | **Impact:** กลาง (improvability)

---

### 🟠 P2 — Lifecycle completeness

#### 5. TripPaused/Resumed → Job (mirror pause state)

**ปัญหา:** Trip.Pause/Resume มีอยู่ แต่ Job ไม่ตาม
ถ้า Trip pause → Job ยัง Executing → confusing

**แนวทาง:** เพิ่ม `TripPausedIntegrationEvent` + `TripResumedIntegrationEvent` (ตอนนี้มีแค่ DomainEvent) → consumer ใน Planning → `Job.MarkPaused/MarkResumed`

หรือยอมรับว่า Job ไม่ track pause + ใส่ comment ใน Job.cs

**Effort:** S | **Impact:** ต่ำ-กลาง

---

#### 6. Pickup/Drop mid-trip visibility ใน Job

**ปัจจุบัน:** Phase b9 ตัดสินใจ skip TripPickup/TripDrop events เพราะเป็น item-level (DeliveryOrder.Items handle)

**ถ้าจะทำ:**
- เพิ่ม JobId ใน TripPickupCompletedIntegrationEvent + TripDropCompletedIntegrationEvent (breaking event contract)
- หรือ consumer ใน Planning ทำ Trip→Job lookup
- เพิ่ม sub-status `Picked` / `Delivered` ใน JobStatus หรือ field แยก

**Effort:** M | **Impact:** ต่ำ (ของ DeliveryOrder.Items ครอบอยู่แล้ว)

---

#### 7. ExceptionRaised audit on Job

**Phase b9 ตัดสินใจ defer:** เพิ่ม `JobExceptions` collection
ใช้ตอน reconciliation tooling ต้องการ trace exception lineage

**Effort:** M | **Impact:** ต่ำ

---

### 🟢 P3 — Cleanup

#### 8. ลบ dead code

**OrderStatus.Amended** — declared แต่ไม่เคย write
- เลือก: implement amendment flow OR ลบ enum value + `DeliveryOrderAmendedIntegrationEvent`

**ItemStatus.Returned** — declared แต่ไม่เคย write
- เลือก: implement returned-to-sender flow OR ลบ

**Effort:** S (1-2 ชม) | **Impact:** ต่ำ (technical debt)

---

#### 9. Backfill script — pre-b8 Trips ที่ JobId = Guid.Empty

**ปัญหา:** Trips เก่าก่อน Phase b8 ไม่มี Job link → consumer Phase b9 skip
ทำให้ historical orders ไม่มี Job tracking

**แนวทาง:** script สร้าง Job rows สำหรับ Trip เก่าโดย derive จาก DeliveryOrder + UpperKey
ผูก JobId กลับเข้า Trip rows

**Effort:** S | **Impact:** ต่ำ (legacy data only)

---

#### 10. Naming consistency refactor

| Aggregate | Method pattern |
|---|---|
| Trip | `MarkVendorStarted`, `MarkVendorCompleted`, `MarkVendorFailed` |
| Job | `MarkExecuting`, `MarkCompleted`, `MarkFailed` |
| Item | `MarkPicked`, `MarkDroppedOff`, `ConfirmDelivered` |
| Order | `MarkAsValidated`, `MarkPlanned`, `MarkDispatched` |

อ่านโค้ดข้าม aggregate งง — unify เป็น `Mark{Status}` ใน refactor cycle ถัดไป

**Effort:** L (ต้อง update test ทุก suite) | **Impact:** ต่ำ (DX)

---

#### 11. Formal state machine library

ตอนนี้ scattered `if (Status != X) throw` ใน method
ลอง [Stateless](https://github.com/dotnet-state-machine/stateless) ใน aggregate ใหม่ตอน b10+

**Effort:** L | **Impact:** ต่ำ (DX)

---

## ลำดับที่แนะนำสำหรับ phase ต่อไป

```
Sprint ถัดไป (1-2 weeks):
  ├─ b11: Order ตาม Trip cancel              (P0 / M / สูง)
  └─ b10-frontend.2: Jobs queue page          (P1 / M / สูง)

Sprint ถัดไปอีกหน่อย:
  ├─ b12: StatusHistory tables                (P0 / L / สูง)
  └─ b13: Job.FailureCategory enum            (P1 / S / กลาง)

Backlog (do-when-needed):
  - TripPaused→Job mirror
  - Pickup/Drop visibility
  - ExceptionRaised audit
  - Backfill script
  - Dead code cleanup
  - Naming refactor
  - State machine library
```

---

## Cross-reference

- Phase b8 design notes → `~/.claude/projects/d--DTMS/memory/project_planning_phase_b8.md`
- Phase b9 design notes → `~/.claude/projects/d--DTMS/memory/project_planning_phase_b9.md`
- Full review session log → conversation 2026-06-11

อัพเดตไฟล์นี้เมื่อ phase ใดเข้า main แล้ว — เพื่อให้เป็น single source of truth ของงาน Planning module
