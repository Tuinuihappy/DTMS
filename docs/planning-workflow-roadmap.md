# Planning Workflow Roadmap — Remaining Options

ภาพรวมงานที่เหลือหลัง Phase b8 → b9 → b10/b11/b12/b13 + #1/#9/#10 land แล้ว
จัดลำดับตาม priority × effort × impact เพื่อช่วยเลือก phase ถัดไป

อ้างอิงการ review แบบเต็มในเซสชั่นวันที่ 2026-06-11 (initial) +
update 2026-06-14 หลัง CC1 + ทุก top-priority items ปิด

**สถานะปัจจุบัน (2026-06-14):** Top-priority backlog ปิดหมดแล้ว. ที่
เหลือทั้งหมดเป็น 🚫 **Deferred** — แต่ละตัวมีเหตุผลชัดและ trigger
ที่จะปลด defer. ดู [event-projection-plan.md](event-projection-plan.md)
สำหรับด้าน projection ที่อยู่ในสภาพเดียวกัน (~95% done).

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
| **#1** | Trip Pause/Resume → Job mirror (JobStatus.Paused) | `68750a9d` | Jobs queue badge ตรงกับสถานะจริงตอน pause; status timeline เห็น Paused row |
| **#9** | `JobFacts.FailureCategory` column + "Job failures" report tab | `94103e39` | /reports tab ใหม่ group by structured category แทน text |
| **#10** | Vehicle performance — group by `VendorVehicleKey` (was envelope key) | `fa6fc3a4` | /reports tab ที่เคยใช้งานไม่ได้ตอนนี้แสดง 5 vehicle bars จริง |
| **Event Projection P2** | Activity Timeline — transparent swap of /audit-full | `f23b54d8` | Single indexed read แทน 4-source UNION |
| **Event Projection P3.1+P3.2** | Order funnel + Fleet projections + dashboard subpages | `f2dbc140` / `2daea413` | /dashboard, /dashboard/orders, /dashboard/robots ใช้ real projection data |
| **Event Projection P4** | OrderListView projection + tsvector full-text search | `ee5b7434` | `GET /api/v1/delivery-orders` ใช้ projection + filter `hasFailedTrip` / `hasActiveJob` |
| **Event Projection P5** | BI fact tables (Order / Trip / Job) + pre-built /reports page | `8feb38cf` → `9b984cfc` | 6 report tabs + CSV export |
| **Observability CC4** | /admin/projections health dashboard | `8528a077` | Per-projector lag + processed count + status badge |
| **CC1 Documentation** | `docs/projector-catalog.md` — 11-projector registry | `de32c493` | Single source for "which projector writes X / consumes Y / powers endpoint Z" |
| **Cleanup (partial #8)** | Remove `OrderStatus.Amended` enum value | `bf960c17` | Amendment flow ยังทำงาน (string literal in projector); enum value ตายแล้วลบทิ้ง |
| **Demo polish** | 30-order 7d seed + ChartMount wrapper for /reports | `cb20d046` | /reports ทุก tab เห็น chart จริง |

---

## งานที่เหลือ — ทั้งหมด 🚫 Deferred

ทุกตัวด้านล่างเป็น deferred โดยตั้งใจ ไม่ใช่ "ลืม" หรือ "ยังไม่มีเวลา" —
มีเหตุผลชัดเจนว่าทำไมเลื่อน + trigger ที่จะปลด defer. การหยิบขึ้นมาทำ
โดยไม่มี trigger จริง = premature work ที่จะ design ผิด, rot ก่อนใช้,
และมี opportunity cost เทียบกับ feature ที่ user ขอจริง

---

### 🟠 Lifecycle completeness (low-impact)

#### #2. Pickup/Drop mid-trip visibility ใน Job 🚫 Deferred

**Status:** ครอบโดย DeliveryOrder.Items แล้ว — ไม่ต้องทำซ้ำใน Job

**Why deferred:**
Phase b9 ตัดสินใจ skip TripPickup/TripDrop events เพราะเป็น item-level
event. DeliveryOrder.Items อัพเดต `Status` (Picked/DroppedOff/Delivered)
อยู่แล้ว → operator เห็น item lifecycle ใน order drawer. การเพิ่ม
sub-status ใน Job = duplicate ของข้อมูลที่มีอยู่

**Trigger to revisit:**
ops file ticket ว่าต้องการดู mid-trip progress ที่ Job level
(เช่น "Job 50% สำเร็จ — pick แล้วยัง drop ไม่ได้"). ถ้าวันนั้นมา
ต้องเลือก: (a) เพิ่ม `JobId` ใน TripPickup/Drop events (breaking
contract — 1 deployment needed), (b) consumer ใน Planning ทำ Trip→Job
lookup, หรือ (c) field แยก ไม่ใช่ status (e.g. `PickedCount` /
`DroppedCount`)

**Effort เมื่อ trigger:** M (1-2 วัน)

---

#### #3. ExceptionRaised audit on Job 🚫 Deferred

**Status:** Phase b9 defer — ตอนนี้ใช้ `OrderActivity` projection ดู exception lineage ผ่าน Order timeline ได้

**Why deferred:**
`JobExceptions` collection จะซ้ำกับ `dispatch.TripExceptions` table
+ `OrderActivity` projection ที่ project `ExceptionRaisedIntegrationEvent`
อยู่แล้ว. operator/oncall ดู exception ผ่าน 2 surface นี้ได้ — ไม่
ต้อง denormalize ลง Job

**Trigger to revisit:**
reconciliation tooling ต้องการ trace exception lineage จาก Job
viewpoint (e.g. "show me all exceptions on this Job across retries").
ตอนนี้ไม่มี tool แบบนี้

**Effort เมื่อ trigger:** M (~1 วัน)

---

### 🟢 Cleanup / DX (zero impact ต่อ user)

#### #4. ItemStatus.Returned — decide implement vs delete 🚫 Deferred

**Status:** เก็บไว้ตามคำตัดสินใน b13 cleanup (commit `bf960c17`)

**Why deferred:**
- Enum declared, อ่าน 2 ที่ (`IsTerminalUnsuccessful()` + rebind reset),
  **ไม่เคย write** ที่ไหน
- ตัดสินใจ b13: เก็บไว้เพราะ "returned-to-sender flow น่าจะมีอนาคต"
- เลือกลบตอนนี้ = อาจต้อง re-add ทีหลังพร้อม migration; เลือก
  implement ตอนนี้ = guessing business flow ที่ยังไม่เคย design

**Trigger to revisit:**
PM/ops design "returned to sender" flow ของจริง. ถ้าตัดสินใจไม่ทำ
ตลอดกาล → ลบ enum (1-2 ชม).

**Effort เมื่อ trigger:** S (ลบ) / M (implement full flow)

---

#### #5. Backfill script — pre-b8 Trips ที่ JobId = Guid.Empty 🚫 Deferred

**Status:** legacy data only — dev/staging DB ไม่มี trips เก่าแบบนี้แล้ว

**Why deferred:**
ปัญหา Trip ก่อน Phase b8 ที่ไม่มี Job link มีผลแค่กับ historical data
ใน production DB ที่เคย run ระบบเดิมอยู่. dev DB ปัจจุบันถูก reseed
หลัง b8 ลง — ไม่มี orphan trips เหลือแล้ว

**Trigger to revisit:**
deploy ครั้งแรกขึ้น production DB ที่มี trips เก่าก่อน b8 + ops
complain ว่าหา Job ของ historical orders ไม่เจอ. ถ้า production
fresh-start (ไม่มี legacy data) = skip ตลอดกาล

**Effort เมื่อ trigger:** S (~3-4 ชม)

---

#### #6. Naming consistency refactor (`MarkXxx` → `Mark{Status}`) 🚫 Deferred

**Status:** DX-only — ไม่กระทบ user, ไม่กระทบ correctness

**Why deferred:**
ตอนนี้ method naming ข้าม aggregate ไม่ตรงกัน:
- Trip: `MarkVendorStarted`, `MarkVendorCompleted`, `MarkVendorFailed`
- Job: `MarkExecuting`, `MarkCompleted`, `MarkFailed`
- Item: `MarkPicked`, `MarkDroppedOff`, `ConfirmDelivered`
- Order: `MarkAsValidated`, `MarkPlanned`, `MarkDispatched`

Refactor เป็น `Mark{Status}` ทั้งหมด = touch ทุก test suite + consumer
+ command handler. Cost สูง, payoff ต่ำ (engineer อ่านโค้ดสะดวกขึ้น
นิดหน่อย — user ไม่เห็น)

**Trigger to revisit:**
ทำพร้อมกับ refactor ใหญ่อื่นๆ (เช่น Stateless library #7 ลง พร้อมกัน
จะคุ้มกว่าทำคู่). ถ้าไม่มี refactor ใหญ่มาคู่ — defer ตลอดกาล

**Effort:** L (~1 สัปดาห์ — ต้อง update test ทุก suite)

---

#### #7. Formal state machine library (Stateless) 🚫 Deferred

**Status:** scattered `if (Status != X) throw` ตอนนี้ทำงานได้ + ทดสอบครอบทุก case

**Why deferred:**
- Aggregates ปัจจุบัน (Order/Job/Trip/Item) มี state machine ที่กระจาย
  ใน method แต่ทดสอบครอบครบ — ไม่ buggy
- ใช้ [Stateless](https://github.com/dotnet-state-machine/stateless) = library dependency ใหม่ + เรียนรู้ DSL ใหม่
- Payoff = aggregate ใหม่ในอนาคตเขียน state machine ได้สะอาดกว่า
  แต่ตอนนี้ไม่มี aggregate ใหม่จะเขียน

**Trigger to revisit:**
- เริ่ม aggregate ใหม่ที่มี > 6 transitions (state explosion จริง) →
  ลองใช้ Stateless กับ aggregate นั้นก่อน
- หรือมี bug การ transition ที่ unit test ไม่จับ → indication ว่า
  scattered guards ไม่เพียงพอ

**Effort:** L (~1 สัปดาห์ — pilot 1 aggregate + decide rollout)

---

### 🟣 Polish จากงานที่เพิ่งทำ

#### #8. Recharts `width(-1) height(-1)` warning — kill จริงๆ 🚫 Deferred

**Status:** Charts render ถูกต้อง — warning เป็น cosmetic ของ Recharts internal first paint

**Why deferred:**
- `<ChartMount>` (commit `cb20d046`) กั้น render จนกว่า ResizeObserver
  จะ fire → ป้องกัน real layout race แล้ว
- Warning ที่เหลือมาจาก **Recharts ResponsiveContainer ภายใน** —
  log จากการ measure ครั้งแรกของตัวเอง ก่อน parent ส่ง dimensions มา
- 3 ทางแก้: เปลี่ยน ResponsiveContainer → fixed-px BarChart (per-chart
  refactor, M effort), patch Recharts (ไม่คุ้ม), console.warn override
  filter (hacky + อาจ hide warning อื่นที่สำคัญ)

**Trigger to revisit:**
- Recharts release upstream fix (track [recharts/recharts#xxx](https://github.com/recharts/recharts))
- หรือ user complaint ว่า console พอง (= dev experience issue ที่ user
  มอง F12) — มีโอกาสน้อย

**Effort เมื่อ trigger:** M (~half day per chart × 6 charts)

---

### 🔵 Gated on external triggers

#### #11. Event Projection P6 — Compliance 🚫 Deferred

ดูรายละเอียดเต็มใน [event-projection-plan.md](event-projection-plan.md)
section "P6 — Compliance"

**Why deferred:**
- ไม่มี regulatory requirement ใน scope ปัจจุบัน
- design ของ compliance flow specific ต่อ regulation (SOX ≠ HIPAA ≠
  PDPA) — สร้างเก็งล่วงหน้า = ผิด spec ตอนใช้จริง
- Event Projection foundation ปัจจุบันให้ 80% ของสิ่งที่ regulator
  ส่วนใหญ่อยาก (immutable inbox, ordered status history, audit timeline)
  — เหลือแค่ crypto + archival layer

**Trigger:** stakeholder ตั้งชื่อ regulation จริง

**Effort:** L (1-2 สัปดาห์)

---

#### #12. Monitoring/Reporting bounded context extraction 🚫 Deferred

**Status:** projection (bi.*, dashboards) อยู่ใน owning modules ตาม
aggregate — boundary ปัจจุบันถูกต้องตาม DDD

**Why deferred:**
- ทุก projection ปัจจุบันมี **natural module owner** (OrderFacts → DeliveryOrder, TripFacts → Dispatch, JobFacts → Planning)
- การสร้าง Monitoring module ตอนนี้ = +4 csproj (Domain/App/Infra/Presentation) + DI wiring + migration ที่ "moving rearranging only" — ไม่ได้ feature ใหม่
- ถ้าจะคุ้มต้องมี **cross-cutting aggregate ≥ 2 ตัว** ที่ไม่มี natural module owner (e.g. "SLA breach rate" join Order × Trip × Fleet)

**Trigger:** projection ที่ 12-13 (ตัวต่อไป) ต้อง JOIN ข้าม 2+ modules
แบบ first-class. Plus discussion ในเซสชั่นวันที่ 2026-06-13 — ตัดสินใจ
ใช้ per-aggregate ownership ไปก่อน

**Effort:** L (~1 สัปดาห์ — extract + migrate existing projections)

---

## ลำดับที่แนะนำสำหรับ phase ต่อไป

**ปัจจุบัน:** ไม่มีงานในรายการนี้ที่ควรทำตอนนี้.

```
ทุก backlog item = 🚫 Deferred (มีเหตุผล + trigger ระบุไว้ในแต่ละ section ด้านบน)

ทางเลือกที่ดีกว่าการ mine backlog:
  └─ Deploy ขึ้น staging → ใช้งาน 1-2 สัปดาห์ → กลับมาเลือก batch
     ถัดไปจาก concrete pain points แทน guessing
```

---

## วิธีปลด defer ของแต่ละ item

หลักการ: **อย่าพลิก defer status ของ item ใดโดยไม่มี trigger ของ
item นั้น** (ลิสต์ใน "Trigger to revisit" ของแต่ละ section)

ถ้า trigger เกิดขึ้น:
1. อ่าน "Why deferred" + "Trigger to revisit" ของ item นั้นซ้ำ — ยัง
   ตรงกับสถานการณ์จริงไหม
2. ถ้ายังตรง → เริ่ม implement ตาม "Effort เมื่อ trigger"
3. ถ้าไม่ตรง (e.g. user request ต่างจากที่คาด) → re-scope ก่อน
4. **อัพเดตไฟล์นี้** — ย้าย item ขึ้นไป "ที่ทำเสร็จแล้ว" table

---

## Cross-reference

- [event-projection-plan.md](event-projection-plan.md) — Event Projection phase doc (~95% done)
- [projection-conventions.md](projection-conventions.md) — Projection coding conventions
- Phase b8/b9 design notes → `~/.claude/projects/d--DTMS/memory/`
- Full review session log → conversation 2026-06-11 + 2026-06-13/14

อัพเดตไฟล์นี้เมื่อ phase ใดเข้า main แล้ว — เพื่อให้เป็น single source of truth ของงาน Planning module
