# Planning Workflow Roadmap — Remaining Options

ภาพรวมงานที่เหลือหลัง Phase b8 → b9 → b10/b11/b12/b13 land แล้ว
จัดลำดับตาม priority × effort × impact เพื่อช่วยเลือก phase ถัดไป

อ้างอิงการ review แบบเต็มในเซสชั่นวันที่ 2026-06-11 (initial) +
update 2026-06-14 หลัง P5 ของ Event Projection plan ปิด

---

## ที่ทำเสร็จแล้ว (ของจริงใน main)

| Phase | Scope | Commit | ผลลัพธ์ |
|---|---|---|---|
| **b8** | Job 1:1 envelope anchor + retry endpoint | (earlier) | `planning.Jobs` ทำงาน, `POST /jobs/{id}/retry` พร้อมใช้ |
| **b9** | Job mirrors Trip lifecycle (4 events) + Trip retry preserves JobId | (earlier) | Job.Status ตาม Trip จริง webhook กลับมา → verified E2E |
| **b10-frontend.1** | Job badge + Trip JobId chip ใน Order detail drawer | `de38acc6` | ops เห็น Job status + reason ใน UI |
| **b10-frontend.2** | Jobs queue page (/delivery-orders/jobs) + queue endpoint | `fe9affae` | ops จัดการ failed/stuck jobs จาก UI ได้ |
| **b11** | Order ตาม Trip cancellation — cascade + operator abandon endpoint | `748dea45` | แก้ stuck-Dispatched bug ที่ลูกค้าเห็น |
| **b12** | StatusHistory tables (Order / Job / Trip) — shipped as P0/P1 of Event Projection plan | `cb8a4f29` | Structured timeline + drawer integration + 23 unit tests |
| **b13** | Job.FailureCategory enum (8 values) + breaking signature change | `bf960c17` | Reports group by structured category, ไม่ต้อง LIKE 'Too Many Requests%' |
| **Event Projection P2** | Activity Timeline — transparent swap of /audit-full | `f23b54d8` | Single indexed read แทน 4-source UNION |
| **Event Projection P3.1+P3.2** | Order funnel + Fleet projections + dashboard subpages | `f2dbc140` / `2daea413` | /dashboard, /dashboard/orders, /dashboard/robots ใช้ real projection data |
| **Event Projection P4** | OrderListView projection + tsvector full-text search | `ee5b7434` | `GET /api/v1/delivery-orders` ใช้ projection + filter `hasFailedTrip` / `hasActiveJob` |
| **Event Projection P5** | BI fact tables (Order / Trip / Job) + pre-built /reports page | `8feb38cf` → `9b984cfc` | 5 report tabs + CSV export |
| **Observability CC4** | /admin/projections health dashboard | `8528a077` | Per-projector lag + processed count + status badge |
| **Cleanup (partial #8)** | Remove `OrderStatus.Amended` enum value | `bf960c17` | Amendment flow ยังทำงาน (string literal in projector); enum value ตายแล้วลบทิ้ง |
| **Demo polish** | 30-order 7d seed + ChartMount wrapper for /reports | `cb20d046` | /reports ทุก tab เห็น chart จริง |

---

## งานที่เหลือ — เรียงตาม priority

### 🟠 P2 — Lifecycle completeness (backlog, do-when-needed)

#### 1. TripPaused/Resumed → Job (mirror pause state)

**ปัญหา:** Trip.Pause/Resume มีอยู่ + integration events ขึ้นมาแล้วใน P1 (TripPausedIntegrationEventV1 / TripResumedIntegrationEventV1) แต่ Job ไม่ตาม. ถ้า Trip pause → Job ยัง Executing → confusing

**แนวทาง:**
- เพิ่ม Planning consumer ที่ subscribe `TripPausedIntegrationEventV1` / `TripResumedIntegrationEventV1` → `Job.MarkPaused()` / `Job.MarkResumed()`
- หรือยอมรับว่า Job ไม่ track pause + ใส่ comment ใน Job.cs explicit

**Effort:** S | **Impact:** ต่ำ-กลาง

---

#### 2. Pickup/Drop mid-trip visibility ใน Job

**ปัจจุบัน:** Phase b9 ตัดสินใจ skip TripPickup/TripDrop events เพราะเป็น item-level (DeliveryOrder.Items handle อยู่แล้ว)

**ถ้าจะทำ:**
- เพิ่ม JobId ใน TripPickupCompletedIntegrationEvent + TripDropCompletedIntegrationEvent (breaking event contract)
- หรือ consumer ใน Planning ทำ Trip→Job lookup
- เพิ่ม sub-status `Picked` / `Delivered` ใน JobStatus หรือ field แยก

**Effort:** M | **Impact:** ต่ำ (ของ DeliveryOrder.Items ครอบอยู่แล้ว)

---

#### 3. ExceptionRaised audit on Job

**Phase b9 ตัดสินใจ defer:** เพิ่ม `JobExceptions` collection ใช้ตอน reconciliation tooling ต้องการ trace exception lineage

**Effort:** M | **Impact:** ต่ำ

---

### 🟢 P3 — Cleanup / DX

#### 4. ItemStatus.Returned — decide implement vs delete

**ปัจจุบัน:** declared แต่ไม่เคย write. อ่าน 2 ที่ (`IsTerminalUnsuccessful()` + rebind reset). เก็บไว้ตามคำตัดสินใน b13 cleanup เพราะ "returned to sender" flow น่าจะมีอนาคต

**Effort:** S (ถ้าลบ) / M (ถ้า implement) | **Impact:** ต่ำ

---

#### 5. Backfill script — pre-b8 Trips ที่ JobId = Guid.Empty

**ปัญหา:** Trips เก่าก่อน Phase b8 ไม่มี Job link → consumer Phase b9 skip ทำให้ historical orders ไม่มี Job tracking

**แนวทาง:** script สร้าง Job rows สำหรับ Trip เก่าโดย derive จาก DeliveryOrder + UpperKey ผูก JobId กลับเข้า Trip rows

**Effort:** S | **Impact:** ต่ำ (legacy data only — dev DB ไม่มี trips เก่าแบบนี้แล้ว)

---

#### 6. Naming consistency refactor

| Aggregate | Method pattern |
|---|---|
| Trip | `MarkVendorStarted`, `MarkVendorCompleted`, `MarkVendorFailed` |
| Job | `MarkExecuting`, `MarkCompleted`, `MarkFailed` |
| Item | `MarkPicked`, `MarkDroppedOff`, `ConfirmDelivered` |
| Order | `MarkAsValidated`, `MarkPlanned`, `MarkDispatched` |

อ่านโค้ดข้าม aggregate งง — unify เป็น `Mark{Status}` ใน refactor cycle ถัดไป

**Effort:** L (ต้อง update test ทุก suite) | **Impact:** ต่ำ (DX)

---

#### 7. Formal state machine library

ตอนนี้ scattered `if (Status != X) throw` ใน method
ลอง [Stateless](https://github.com/dotnet-state-machine/stateless) ใน aggregate ใหม่

**Effort:** L | **Impact:** ต่ำ (DX)

---

### 🟣 P4 — Polish จากงานที่เพิ่งทำ (deferred from C)

#### 8. Recharts `width(-1) height(-1)` warning — kill จริงๆ

**ปัจจุบัน:** `<ChartMount>` กั้น render จนกว่า ResizeObserver จะ fire — แต่ Recharts log warning จาก internal first measurement ของตัวเอง ไม่สามารถ suppress จากนอกได้

**แนวทาง:**
- เปลี่ยน `<ResponsiveContainer>` เป็น `<BarChart width={W} height={H}>` แบบ fixed-px + ResizeObserver hook เอง per chart
- หรือ patch Recharts (ไม่คุ้ม)
- หรือ filter console.warn override (hacky)

**Effort:** M | **Impact:** ต่ำ (cosmetic)

---

#### 9. JobFacts.FailureCategory column + Top failures report grouping

**ปัญหา:** b13 เพิ่ม `Job.FailureCategory` ใน write side แล้ว แต่ `bi.JobFacts` ยังไม่ carry column นี้ → P5.3 Top failures report ยัง group by free-text `FailureReason` เหมือนเดิม

**แนวทาง:**
- เพิ่ม `FailureCategory` column ใน `bi.JobFacts` + migration
- อัพเดต `JobFactsProjector.SetFailedAtAsync` / `SetCancelledAtAsync` ให้ carry category
- ขยาย Top failures report ให้ group by category (พร้อมแสดง reason เป็น sub-row)

**Effort:** S-M | **Impact:** กลาง (Reports ใช้ structured data จริง)

---

#### 10. Vendor performance report — vendor identity vs envelope key

**ปัญหา:** `bi.TripFacts.VendorUpperKey` = `dispatch.Trips.UpperKey` ซึ่งเป็น per-order envelope key (เช่น `<orderId32hex>-G1`) ไม่ใช่ vendor identifier. ผลคือ Vendor performance chart มี 1 vendor bucket ต่อ trip — ใช้ไม่ได้

**แนวทาง:**
- เพิ่ม `VendorName` / `VendorCode` column ที่ trip emit (RIOT3 พอจะรู้ vendor identity?)
- หรือสกัด vendor prefix จาก `VendorOrderKey` (e.g. `VOK-AAA-...` → `RIOT3-AAA`)
- อัพเดต TripFacts + projector + report

**Effort:** M | **Impact:** สูง (report ปัจจุบันใช้งานไม่ได้จริง — แค่ render OK)

---

### 🔵 P5 — Optional, gated on triggers

#### 11. Event Projection P6 — Compliance

จาก [event-projection-plan.md](event-projection-plan.md):
- Event archival to cold storage
- Tamper-evident row chaining (Merkle hash)
- Event versioning + upcasting framework
- Compliance reports (signed PDF per aggregate)

**Effort:** L | **Trigger:** เฉพาะถ้าเข้า regulated context

---

#### 12. Monitoring/Reporting bounded context extraction

ตอนนี้ projection (bi.*, dashboards) กระจายอยู่ใน owning modules. ถ้าวันหนึ่งมี cross-cutting aggregate (e.g. "SLA breach rate" join Order × Trip × Fleet ที่ไม่มี natural module owner) → พิจารณา extract Monitoring module

**Effort:** L | **Trigger:** เมื่อมี cross-module aggregate ตัวที่ 1-2 ตัว

---

## ลำดับที่แนะนำสำหรับ phase ต่อไป

```
Top of mind (impact-driven):
  └─ #10: Vendor performance — vendor identity fix    (P4 / M / สูง)
     (report นี้ตอนนี้ render OK แต่ใช้งานไม่ได้จริง)

ถ้ามีเวลาเหลือ (small wins):
  ├─ #9:  JobFacts.FailureCategory + report grouping  (P4 / S-M / กลาง)
  └─ #1:  TripPaused/Resumed → Job mirror             (P2 / S / ต่ำ-กลาง)

Deep backlog (do-when-needed):
  - #2 Pickup/Drop visibility
  - #3 ExceptionRaised audit
  - #4 ItemStatus.Returned (delete or implement)
  - #5 Pre-b8 Trips backfill
  - #6 Naming refactor
  - #7 State machine library
  - #8 Recharts warning kill

Gated:
  - #11 P6 Compliance — regulatory trigger
  - #12 Monitoring context — cross-module aggregate trigger
```

---

## Cross-reference

- [event-projection-plan.md](event-projection-plan.md) — Event Projection phase doc (~95% done)
- [projection-conventions.md](projection-conventions.md) — Projection coding conventions
- Phase b8/b9 design notes → `~/.claude/projects/d--DTMS/memory/`
- Full review session log → conversation 2026-06-11 + 2026-06-13/14

อัพเดตไฟล์นี้เมื่อ phase ใดเข้า main แล้ว — เพื่อให้เป็น single source of truth ของงาน Planning module
