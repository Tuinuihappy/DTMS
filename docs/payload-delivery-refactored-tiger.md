# Delivery Order Payload Review — Logistics Standards & Design Patterns

## Context

DTMS เป็น AMR Delivery Planning System สำหรับ in-plant logistics (Phase 1–3 + Hardening เสร็จ). โมดูล DeliveryOrder เป็น entry point ของ pipeline ทั้งหมด — รับ payload จาก WMS/ERP/MES แล้วส่งต่อให้ Planning/Dispatch/VendorAdapter

ปัจจุบัน payload มี gap ระหว่าง:
- **Design doc** (`AMR_Delivery_Planning_System_Design.md` §1) ที่ระบุ schema เต็ม (serviceWindow, slaTier, hazmatClass, temperatureRange, handlingInstructions, PickupInstruction/DropInstruction, deliveryProof) — implementation ครอบคลุมแค่บางส่วน
- **Logistics standards** (GS1 lot/SKU, EDI 940/945 patterns, ASN) — มี foundation แต่ขาด identifier convention, time windows, party blocks
- **Design patterns** (DDD, CQRS, idempotency, event versioning) — บางจุดยังไม่ consistent

Review นี้จัดเป็น **4 sections** (อ่านเข้าใจ) + **ranked backlog P0/P1/P2** (พร้อม execute)

## Execution Status (อัปเดต 2026-05-25)

| Item | Status | Commit |
|---|---|---|
| Sync ReadyToPlan→Confirmed terminology + repair unit tests | ✅ done | `3fc95e07` |
| **P0-1** ลบ LotNo unique rule | ✅ done | `0786a94d` |
| **P0-3** Verify outbox processor — มีอยู่ครบ pipeline | ✅ verified | — |
| **P0-3 extension** Retry mechanism (exponential backoff, max 5) | ✅ done | `552c13be` |
| **P0-2** LocationRef VO (Option A: tagged union + user input preserved) | ✅ done | `e544e5ae` |
| **P0-2 reverted** — code-only (YAGNI: no upstream client submits via stationId) | ✅ done | `cea473e` |
| **P0-4** Idempotency-Key required on all 8 mutation endpoints (endpoint filter, body-hash strict, 422 on conflict, 24h Redis) | ✅ done | `2ce55796` |
| **P0-4 relaxed** — header changed to **optional / best-effort** (filter passes through when missing; replay/conflict logic intact when present); OpenAPI marks header `required: false` | ✅ done | `cea473e` |
| **P0-5** WeightKg policy — Option C (warn but allow): WeightWarningEvaluator + configurable fallback (500 kg default) + warnings in Submit/Confirm/Upstream/Bulk responses + audit events | ✅ done | `194d3064` |
| **Decisions 1–5** resolved (path-based versioning / keep `Confirmed` / no multi-tenant / UOM hybrid / Hazmat Option C) | ✅ done | `cea473e` |
| **P1-7** Path versioning — prefix `/api/v1/` ทุก business module (DeliveryOrder/Items/Fleet/Dispatch/Facility/Planning); Auth/Webhook/Health unchanged | ✅ done | `cea473e` |
| **P1-2** SlaTier (Bronze/Silver/Gold) + SubmittedAt (SLA clock) — entity, commands, query DTO, integration event (additive), migration | ✅ done | `8982a1e` |
| **P1-6** `:hold` / `:release` endpoints — expose existing entity `Hold()`/`Release()` via REST; idempotent; audit + warning surfaces | ✅ done | `826c4ea` |
| **P1-1** ServiceWindow VO `{earliest, latest}` replaces `RequestedDeliveryDate`; entity, commands, query DTO, integration event additive (`Deadline` retained as alias for `Latest`, slated for removal in P1-8), AmendmentType rename `RequestedTimeChange`→`ServiceWindowChange`, migration | ✅ done | `d9c4e93` + `f210ce5` |
| **Smoke test** P1-1/P1-2/P1-6/P1-7 end-to-end ผ่าน docker: 6 old paths→404, full create→submit→confirm→hold→release lifecycle, outbox payload carries `Earliest`/`Latest`/`Deadline`/`SlaTier`/`SubmittedAt` | ✅ verified | 2026-05-25 |
| **P1-9** Quantity VO + closed `UnitOfMeasure` enum (KG/G/LB/EA/BOX/PALLET/CASE) + `UomNormalizer` + alias map in `appsettings.json` + migration backfill | ✅ done | _this commit_ |
| P1 (remaining) / P2 | ⏳ pending | execution order ใน Next Steps |

---

## Section 1 — Logistics Standards Compliance

### 1.1 Identifier & Traceability (GS1-leaning)

**ที่มี:**
- `OrderRef` (string, max 200) + unique index `(SourceSystem, OrderRef)` — รองรับ multi-source idempotency
- `Sku` (string, max 100) per item
- `CargoSpecific.LotNo`, `TraceId`, `PartNo`, `Po`, `Wo` — manufacturing context

**Gap:**
- ไม่มี **GTIN/SSCC** field แยก — `Sku` ทำหน้าที่ทับซ้อนกัน (internal item code vs GTIN); ไม่มี validation pattern ว่า Sku เป็น GS1 AI หรือ proprietary
- ไม่มี **GLN** (Global Location Number) สำหรับ pickup/drop — `PickupLocationCode`/`DropLocationCode` เก็บเป็น string opaque
- LineNumber per item คือ `ItemSeq` (int) — OK สำหรับ in-plant แต่ EDI 940/945 ใช้ LIN/SLN ที่ string ได้
- `LotNo` validate uniqueness within order ([CreateDraftDeliveryOrderCommandValidator](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/CreateDraftDeliveryOrder/CreateDraftDeliveryOrderCommandValidator.cs)) — **น่าจะผิด**: ในธุรกิจจริง 1 order มี items หลายตัวที่ lot เดียวกันได้ (เช่น 10 ลังของ lot ABC)

### 1.2 Time Windows & Service Level

**ที่มี:**
- `RequestedDeliveryDate` (DateTime?) — เวลาเดียว
- `Priority` enum 4 ค่า

**Gap (design doc §1 line 88-91 ระบุไว้ แต่ยังไม่ implement):**
- `serviceWindow{earliest, latest}` — time window แทน single datetime (EDI 940 ใช้ DTM 002 ship-not-before / DTM 010 ship-not-after)
- `slaTier` — Bronze/Silver/Gold หรือ duration-based SLA สำหรับ Planning ตัดสินใจ optimize
- `PickupInstruction.{earliestPick, latestPick}` + `DropInstruction.{earliestDrop, latestDrop}` — pickup/drop windows แยกกัน (เป็น input หลักของ CVRPTW solver)
- ไม่มี `submittedAt` ที่บันทึก SLA clock start (design doc §1 line 123 บอก "SLA clock starts at SUBMITTED")

### 1.3 Cargo Attributes (Hazmat / Cold chain / Handling)

**ที่มี:**
- `CargoType` enum 5 ค่า: `RawMaterial`, `PackingMaterial`, `FinishedGood`, `FixtureTool`, `WasteScrap`
- `CargoSpecific` VO มี 10 fields manufacturing context
- `Dimensions` VO (mm) + `WeightKg` (nullable)

**Gap (design doc §1 line 89 ระบุไว้):**
- ❌ **`hazmatClass`** — UN class (1-9) + UN number + packing group; สำคัญถ้าจะรองรับ chemical/battery transport ในอนาคต
- ❌ **`temperatureRange`** — cold chain (min/max °C); สำคัญสำหรับ food/pharma
- ❌ **`handlingInstructions`** — free text หรือ enum (fragile, this-side-up, do-not-stack)
- ไม่มี **GrossWeight vs NetWeight** distinction — `WeightKg` เดียว
- `Dimensions` ไม่มี `volume` ที่ submit ได้โดยตรง (มี computed `VolumeCBM` ใน VO แต่ DTO ไม่เปิด); ใช้ mm ทุกที่ — OK แต่ส่ง dimensional weight ไม่ได้

### 1.4 Party & Carrier Blocks (B2B-readiness)

**ที่มี:**
- `PickupLocationCode` / `DropLocationCode` ที่ Item-level — Station-centric (in-plant)
- `SourceSystem` enum (Manual/Sap/Erp/Oms) — ไม่ใช่ party

**Gap (จำเป็นถ้าจะรองรับ B2B/EDI 940/945):**
- ไม่มี **ship-from / ship-to / bill-to party block** (party name, GLN, address) — EDI 940 บังคับ
- ไม่มี **carrier instruction** (carrier code, service level, account number) — สำหรับ outbound shipping
- ไม่มี **customer reference** per item (customer PO line, original order ref) — สำคัญสำหรับ ASN
- ไม่มี **package/SSCC** layer — Item ปัจจุบันคือ "line" ไม่ใช่ "package"; ASN ต้อง pack hierarchy (Order → Pack → Item)
- ไม่มี **deliveryProof** structure (design doc §1 line 91 ระบุ signature/scan/photo) — ปัจจุบันมีแค่ POD barcode scan ใน Dispatch consumer

### 1.5 Event Naming & Versioning

**ที่มี:**
- Integration events: `DeliveryOrderConfirmedIntegrationEvent`, `DeliveryOrderCancelledIntegrationEvent`, ...

**Gap (design doc §1 line 112-117 ระบุ versioned):**
- Design กำหนด `delivery-order.submitted.v1`, `.validated.v1`, `.amended.v1` — แต่ implementation ไม่มี `v1` suffix หรือ schema version field
- ไม่มี `schemaVersion` ใน event payload — break change ในอนาคตจะแยกไม่ออก
- ชื่อ class ใช้ PascalCase `Confirmed` แทน kebab-case `submitted` (subjective — แต่ EDI/AMQP convention ใช้ dot-separated lowercase)

---

## Section 2 — DDD / Domain Review

### 2.1 Aggregate Boundary

**ดีอยู่แล้ว:**
- `DeliveryOrder` เป็น aggregate root, `Item` เป็น child entity (collection encapsulated, exposed อ่านอย่างเดียวผ่าน `IReadOnlyCollection`)
- Mutation route ผ่าน root: `AddItem()`, `UpdateAllItemStatuses()`, `MarkItemsDelivered()` — invariants คุมที่ root
- `OrderAmendment`, `OrderAuditEvent` เป็น aggregate แยก (correct — lifecycle ต่าง)

**Gap:**
- `_items.Clear()` ใน `UpdateDraft()` ทำให้ Item identity (Guid Id) หายทั้งหมด — ถ้ามี downstream ที่อ้าง Item.Id (เช่น dispatch task) ก่อนหน้านี้ จะ orphan ทันที (ปัจจุบัน UpdateDraft จำกัด Draft เท่านั้น — OK สำหรับตอนนี้, แต่ amendment flow ในอนาคตต้องระวัง)
- ไม่มี **soft-delete** หรือ **revision** สำหรับ Item — pure replace
- ~~`Item.PickupLocationCode` / `DropLocationCode` ทำตัวเป็น **polymorphic type** (รับทั้ง Guid และ Code) — ทำใน LocationCodeNormalizer — ซ่อนความหมายใน string, ไม่ใช่ DDD value object pattern~~ → P0-2 แก้เป็น LocationRef VO tagged union, ภายหลัง revert กลับเป็น code-only (YAGNI). resolved `PickupStationId`/`DropStationId` (Guid) เก็บแยกใน entity สำหรับ Planning consumer

### 2.2 Value Objects

**ดีอยู่แล้ว:**
- `Dimensions` extends `ValueObject` + factory + invariant (`> 0`)
- `CargoSpecific` factory pattern (แม้จะไม่ extends ValueObject — น่าจะ extend ให้ structural equality)

**Gap:**
- `Quantity{Value, Uom}` ใน DTO เป็น VO โดยรูป แต่ entity เก็บแยกสอง field (`Quantity` double + `Uom` string) — ไม่ใช่ VO ใน domain
- ไม่มี **UOM whitelist** — รับ "moo" ก็ผ่าน validator (max 20 char)
- `WeightKg` เป็น primitive — ไม่มี `Weight VO` ที่ encapsulate unit (kg/lb conversion, gross vs net)
- ไม่มี **Money/Currency** — ถ้าจะ track cost/freight ในอนาคตต้องเพิ่ม
- `CargoSpecific` มี 10 nullable string — โครงสร้าง flat, ไม่ embed sub-VO (เช่น `Manufacturing{Wo, Line}`, `Procurement{Po, Vendor}`)

### 2.3 State Machine

**ดีอยู่แล้ว:**
- Transition rule บังคับใน entity, throw `InvalidOperationException` ถ้าผิด
- Domain events ออกทุก transition

**Gap:**
- Design doc §1 ใช้ `READY_TO_PLAN` — implementation ใช้ `Confirmed` (สอดคล้องกันแล้วหลัง fix comment) แต่ design doc ยังเขียน `READY_TO_PLAN` อยู่ — ต้อง sync ฝั่งใดฝั่งหนึ่ง
- `Status = Amended` reserved แต่ไม่มี caller — สถานะค้าง orphan
- `MarkPlanning/Planned/Dispatched/InProgress/Hold/Release/Failed` — entity เปิดให้ทำ แต่ Planning/Dispatch **ไม่ ack กลับ** (gap ที่ระบุไว้ใน [previous review](#))
- ไม่มี `Split`/`Merge` actions ที่ design doc §1 line 106 ระบุ (`POST /api/v1/orders/{id}:split / :merge`)

---

## Section 3 — CQRS / API DTO Review

### 3.1 DTO Duplication & Drift

**ที่มี:**
- `Application.Commands.CreateDraftDeliveryOrder.{ItemDto, DimensionsDto, QuantityDto, CargoSpecificDto}` (write)
- `Application.Queries.GetDeliveryOrder.{ItemDto, DimensionsDto, QuantityDto, CargoSpecificDto}` (read)

**Gap:**
- 4 DTO ที่มีชื่อเดียวกัน, shape เกือบเหมือนกัน (read มี `Id`, `ItemSeq`, `Status`, `VolumeCBM`) — ตั้งใจแยก read/write (DDD/CQRS practice) แต่ไม่มี shared "wire contract" → drift เสี่ยง
- `CreateUpstreamDeliveryOrderCommand` มี `ItemDto` ของตัวเอง? ([Explore agent บอกว่า reuses CreateDraft shape](#)) — ต้อง verify
- `BulkSubmitDeliveryOrdersCommand` wraps `List<CreateDraftDeliveryOrderCommand>` — design doc §1 line 104 บอก "up to N orders in one transaction" แต่ implementation จริงเป็น best-effort (partial success, HTTP 207)

### 3.2 Validation Asymmetry

**ที่มี (จาก Explore report):**
- `DraftItemDtoValidator` — loose (allow null)
- `SubmitItemDtoValidator` — strict (require Sku, Pickup, Drop, Quantity)
- `CreateUpstreamDeliveryOrderCommandValidator` — strictest (require WeightKg, RequestedDeliveryDate, CreatedBy)

**Gap:**
- ไม่มี comment/policy doc อธิบายว่าทำไม Upstream ต้อง `WeightKg.NotNull()` แต่ Submit ไม่ต้อง — manual user submit ก็ต้องการ weight สำหรับ load planning เหมือนกัน (เป็น semantic inconsistency)
- `CargoSpecific.Null() if CargoType is null` — co-null check; แต่ไม่มี **co-required** check (เช่น CargoType=`RawMaterial` ต้องมี PartNo + Vendor หรือไม่?)
- ไม่มี **dimensional sanity** check (เช่น length > 50m หรือ weight > 5000kg ที่ AMR รับไม่ได้) — semantic validation อยู่ใน Planning solver, ไม่ใช่ที่ payload
- `OrderRef` validate แค่ NotEmpty + MaxLength 200 — ไม่มี format pattern (เช่น Sap ใช้ 10 digits, Oms ใช้ "OMS-XXXX") → คน fake source system แล้วทะลุ unique index ได้

### 3.3 Idempotency Inconsistency

**ที่มี:**
- `POST /api/v1/delivery-orders` — รองรับ `Idempotency-Key` header (Redis cache 24h)
- `POST /api/v1/delivery-orders/upstream` — idempotent on `(SourceSystem, OrderRef)` + race-condition retry
- `POST /api/v1/delivery-orders/bulk` — ไม่มี idempotency layer

**Gap:**
- `PUT`, `PATCH`, `POST /{id}/submit|confirm|reject`, `DELETE` — **ไม่มี idempotency key** เลย; retry จะ throw concurrency error แทนที่จะ no-op
- Bulk submit ส่งซ้ำสร้าง duplicate orders ทุกตัว — ต้องมี per-item idempotency key หรือ batch key

### 3.4 Missing Endpoints (เทียบ design doc §1 line 102-110)

| Design doc | Implementation | Status |
|---|---|---|
| `POST /api/v1/orders` | `POST /api/v1/delivery-orders` | ✅ versioned via P1-7 |
| `POST /api/v1/orders:bulk` | `POST /api/v1/delivery-orders/bulk` | ✅ |
| `PATCH /api/v1/orders/{id}` | `PATCH /api/v1/delivery-orders/{id}` | ✅ (แต่ amend แค่ RequestedDeliveryDate) |
| `POST /api/v1/orders/{id}:hold` | `POST /api/v1/delivery-orders/{id}/hold` | ✅ via P1-6 (slash-style to match codebase convention) |
| `POST /api/v1/orders/{id}:release` | `POST /api/v1/delivery-orders/{id}/release` | ✅ via P1-6 |
| `POST /api/v1/orders/{id}:cancel` | `DELETE /api/v1/delivery-orders/{id}` | ⚠️ (DELETE ไม่ใช่ REST idiomatic สำหรับ cancel) |
| `POST /api/v1/orders/{id}:split` | — | ❌ pending P2-5 |
| `POST /api/v1/orders/{id}:merge` | — | ❌ pending P2-5 |
| `POST /api/v1/order-templates` | — | ❌ pending P2-4 (recurring orders — Phase 5) |
| `GET /api/v1/orders/{id}/timeline` | `GET /api/v1/delivery-orders/{id}/timeline` | ✅ |

### 3.5 API Versioning ✅ Resolved via P1-7

- **2026-05-25**: Path-based versioning adopted ([Decision #1](#decision-records))
- All business modules ใช้ prefix `/api/v1/` แล้ว: `delivery-orders`, `items`, `fleet`, `dispatch`, `facility`, `planning/jobs`
- ไม่ versioned: `/api/auth/*` (OAuth convention), `/api/webhooks/riot3` (external contract), `/health/*` (infra)
- Resource naming: `/api/v1/delivery-orders` (Decision #1-C1), ไม่ใช่ generic `/api/v1/orders` ตาม design doc — drop generic เพื่อแยกจาก `order-templates` (P2-4) ในอนาคต
- Outstanding: ไม่มี OpenAPI deprecation header, schema version field — pending P1-8 (event/schema versioning)

---

## Section 4 — Evolution & Schema Discipline

### 4.1 Migration History (36 migrations)

ดูจาก migration timeline — schema เปลี่ยนถี่ในช่วงเริ่มต้น (เม.ย.–พ.ค.):
- `RemoveOrderId`, `RemoveOrderNo`, `RemoveWorkOrderIdItemId`, `RenameOrderLineToOrderItem`, `RenamePackageUnitsToItems`, `RenameRequestedTimeToRequestedDeliveryDate`, `RemoveTenantId`, `MoveCargoTypeToItem` — สะท้อนว่า payload model **ยังไม่ stabilized**

**Implication:**
- Upstream system ที่ integrate เข้ามาต้องตามแก้ทุก rename
- เป็นสาเหตุที่ต้องมี **schemaVersion** หรือ **versioned endpoint** ก่อน production rollout
- `RemoveTenantId` migration น่าสนใจ — design doc §10 line 487, 506 ยังพูดถึง multi-tenant — implementation ตัด tenant ออกเพราะอะไร? (อาจถาม user แยก)

### 4.2 Amendment Snapshot Scope

**ที่มี:**
- `OrderAmendment.OriginalSnapshot` / `NewSnapshot` เป็น `jsonb` column
- ปัจจุบัน [`AmendDeliveryOrderCommandHandler.cs:33-47`](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/AmendDeliveryOrder/AmendDeliveryOrderCommandHandler.cs#L33-L47) serialize แค่ `{RequestedDeliveryDate, OrderStatus}` — 2 field

**Gap:**
- ถ้า Amendment ในอนาคต cover เพิ่ม (Priority, Items, Location) snapshot scope ต้องขยาย
- ไม่มี **JSON schema** ของ snapshot — replay ข้ามเวอร์ชันยาก
- ไม่มี `amendmentVersion` field — diff ระหว่าง v1 snapshot กับ v2 snapshot ต้อง code branch

### 4.3 Outbox Pattern

**ที่มี:**
- `OutboxMessages` table ใน schema `deliveryorder`
- `DeliveryOrderDomainEventMapper` — domain → integration mapping ([file](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/Services/DeliveryOrderDomainEventMapper.cs))

**Gap:**
- ไม่มี outbox processor implementation ที่ผม verify เห็น (memory ระบุว่า "outbox processor: notable gap")
- ถ้า processor ขาด → integration event ไม่ได้ publish จริงไปยัง RabbitMQ → Planning consumer ไม่เห็น event แม้ status เปลี่ยนเป็น Confirmed แล้ว (ต้อง verify ใน follow-up)

---

## Ranked Backlog

### 🔴 P0 — แก้ก่อน production / ก่อนเปิด upstream integration

| # | Issue | File | Action | Status |
|---|---|---|---|---|
| ~~P0-1~~ | ~~**LotNo uniqueness within order ผิด business**~~ | [CreateDraftDeliveryOrderCommandValidator.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/CreateDraftDeliveryOrder/CreateDraftDeliveryOrderCommandValidator.cs) | ลบ unique-lot rule | ✅ `0786a94d` |
| ~~P0-2~~ | ~~**PickupLocationCode/DropLocationCode polymorphic (Guid \| Code)** ซ่อนใน string~~ | LocationRef VO + `OwnsOne` mapping + new EF migration; LocationCodeNormalizer ลบทิ้ง | ทำเป็น `LocationRef` VO (Option A: tagged union, user input preserved, `PickupLocation`/`DropLocation`) | ✅ `e544e5ae` |
| ~~P0-2 reverted~~ | ~~**Tagged union over-engineered**~~ — ไม่มี upstream client ส่ง stationId form จริง (SAP/ERP/Oms/Manual ใช้ code) | ลบ `LocationRef` VO + `LocationRefDto`/`LocationRefMapper`; เปลี่ยน `Item.PickupLocation/DropLocation` → `PickupLocationCode/DropLocationCode` (string); `StationValidationService` key เป็น string; new migration drop `PickupLocationStationId`/`DropLocationStationId` columns + ALTER code columns NOT NULL | YAGNI — เพิ่มกลับเมื่อมี client ที่ต้องการ Guid form จริง (additive change) | ✅ `cea473e` |
| ~~P0-3~~ | ~~**Outbox processor ขาด**~~ — false alarm: มีอยู่ครบ pipeline (interceptor + BackgroundService + MassTransit). พบ nuance: failed messages ไม่ retry → fix ด้วย exponential-backoff policy (30s/2m/10m/30m/2h, max 5) | [OutboxProcessorService.cs](src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxProcessorService.cs), [OutboxMessage.cs](src/AMR.DeliveryPlanning.SharedKernel/Outbox/OutboxMessage.cs), [OutboxRetryPolicy.cs](src/AMR.DeliveryPlanning.SharedKernel/Outbox/OutboxRetryPolicy.cs) | verify + add RetryCount/NextRetryAtUtc + 6 EF migrations | ✅ `552c13be` |
| ~~P0-4~~ | ~~**Update/PATCH/Confirm/Reject ไม่ idempotent**~~ | new `DeliveryOrder.Presentation/Idempotency/` (filter, hasher, cached replay, caching wrapper, extension) + [DeliveryOrderEndpoints.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/DeliveryOrderEndpoints.cs) + [ModuleServiceRegistration.cs](src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs) | `.RequireIdempotencyKey()` ครอบ 8 endpoints; 400 ถ้าไม่มี header, 422 ถ้า key เดิม+body ต่าง, replay พร้อม `Idempotency-Replayed: true` | ✅ `2ce55796` |
| ~~P0-4 relaxed~~ | ~~**Header changed to optional**~~ — frontend ยังพัฒนาอยู่/ยังไม่พร้อมจัดการ key, ปิด hard-block แต่เก็บ replay capability ไว้ให้ clients ที่พร้อมใช้ (เช่น upstream SAP) | filter: missing header → pass-through; OpenAPI: `Required = false` + description "strongly recommended" | best-effort idempotency — duplicate risk เปิด แต่เป็นจุดที่ enable hard-enforcement ภายหลังได้ง่าย (revert filter 1 line) | ✅ `cea473e` |
| ~~P0-5~~ | ~~**Validation asymmetry: WeightKg required upstream แต่ optional manual**~~ | new `Application/QualityIssues/` (OrderQualityIssue, WeightWarningEvaluator) + DeliveryOrderOptions.WeightFallbackKg + 4 handlers (Submit/Confirm/Upstream/Bulk) | Option C — warn but allow: evaluate per item, return warnings in response, persist as audit, fallback at event-publish | ✅ `194d3064` |

### 🟡 P1 — ขยาย payload สอดคล้อง design doc + standards

| # | Issue | Effort | Design ref |
|---|---|---|---|
| ~~P1-1~~ | ~~**`serviceWindow{earliest, latest}`** ที่ order-level (แทน `RequestedDeliveryDate` เดียว)~~ ✅ done (`d9c4e93` + follow-up) — VO with nullable bounds (≥1 required, Earliest≤Latest), replaces RequestedDeliveryDate; Confirmed event additive (`Deadline` retained = Latest, removal deferred to P1-8); AmendmentType `ServiceWindowChange` | M | §1 line 88 |
| ~~P1-2~~ | ~~**`slaTier`** enum (Bronze/Silver/Gold) + `submittedAt` (SLA clock)~~ ✅ done (this commit) — pure enum label; default `Bronze`; clock starts on first `Submit()` (frozen via `??=`); `CreateFromUpstream` sets immediately; integration event extended additively | S | §1 line 88, 123 |
| P1-3 | เพิ่ม **`HazmatInfo` VO** (`classCode` regex `^[1-9](\.[1-6])?$`, optional `PackingGroup` I/II/III) ที่ Item-level — Option C ([Decision #5](#decision-records)) | M | §1 line 89 |
| P1-4 | เพิ่ม **`TemperatureRange` VO** (`minC`, `maxC`) ที่ Item-level | S | §1 line 89 |
| P1-5 | เพิ่ม **`handlingInstructions`** (enum flags หรือ string list) | S | §1 line 89 |
| ~~P1-6~~ | ~~**`:hold` / `:release`** (entity มีแล้ว แค่ขาด API)~~ ✅ done (this commit) — `POST /{id}/hold` (204 + reason/heldBy) + `POST /{id}/release` (200 + warnings); idempotent; audit | S | §1 line 106 |
| ~~P1-7~~ | ~~**API versioning** — path-based~~ ✅ done (this commit) — prefix `/api/v1/` ทั้ง 6 business modules; Auth/Webhook/Health คงเดิม | M | §1 line 102 |
| P1-8 | **Integration event versioning** — เพิ่ม `schemaVersion` field + rename class เป็น `DeliveryOrderConfirmedIntegrationEventV1` (หรือ event-bus topic naming convention) | M | §1 line 113-117 |
| P1-9 | เปลี่ยน `Quantity{Value, Uom}` ใน entity เป็น **VO** + UOM whitelist — Option C Hybrid: closed `UnitOfMeasure` enum (KG/G/LB/EA/BOX/PALLET/CASE) + alias map ใน `appsettings.json` — [Decision #4](#decision-records) | M | — |
| P1-10 | **Amendment snapshot ขยาย scope** + เพิ่ม `amendmentVersion` ในตาราง | M | §1 line 93 |

### 🟢 P2 — B2B-readiness / future patterns

| # | Issue | Effort | Trigger |
|---|---|---|---|
| P2-1 | **Party blocks** (`shipFrom`, `shipTo`, `billTo`) + GLN — เปิดทาง EDI 940 | L | เมื่อจะ integrate outbound shipping |
| P2-2 | **Package/SSCC hierarchy** (Order → Pack → Item) — เปิดทาง ASN/EDI 856 | L | เมื่อจะส่ง ASN ออก |
| P2-3 | **`deliveryProof` structure** (signature/scan/photo) — design doc §1 line 91 | M | เมื่อ POD ปัจจุบัน (barcode-only) ไม่พอ |
| P2-4 | **`OrderTemplate` + recurring orders** — design doc §1 line 92, 109 | L | Phase 5 (Milk Run schedules) |
| P2-5 | **`:split` / `:merge` actions** — design doc §1 line 106 | L | เมื่อมี Consolidation/Cross-dock use case จริง |
| P2-6 | เพิ่ม **GTIN/GLN field** แยก (ไม่ใช่ใช้ Sku/Code ทับซ้อน) | M | GS1 compliance |
| P2-7 | **Multi-tenant กลับมา** (ถ้าจริงจะ multi-customer) — RemoveTenantId migration ทำให้หาย | L | depends on business |
| P2-8 | เพิ่ม **dimensional/weight sanity bounds** ที่ payload validator (เช่น weight ≤ 5000 kg) | S | semantic validation tier ที่ design doc §1 line 122 |
| P2-9 | **CargoSpecific co-required rules** (RawMaterial → require Vendor; WasteScrap → require InventoryNo) | S | business rule discovery |
| P2-10 | **OpenAPI spec + JSON Schema export** สำหรับ upstream integrator | M | onboard SAP/ERP team |
| P2-11 | **Outbox processor: replay terminally-failed messages** — ปัจจุบันต้อง manual SQL update | S | ops tooling (admin endpoint หรือ CLI) |
| P2-12 | **Outbox processor: Postgres LISTEN/NOTIFY** แทน 5s polling — รองรับ high throughput | M | เมื่อ load สูง (latency-sensitive) |

---

## Critical Files (สำหรับ reviewer/implementer)

- [DeliveryOrder.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Domain/Entities/DeliveryOrder.cs) — aggregate root, state machine
- [Item.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Domain/Entities/Item.cs) — child entity, ที่ต้องเพิ่ม Hazmat/Temperature
- [CargoSpecific.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Domain/ValueObjects/CargoSpecific.cs) — manufacturing VO
- [CreateDraftDeliveryOrderCommand.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/CreateDraftDeliveryOrder/CreateDraftDeliveryOrderCommand.cs) — write DTO
- [GetDeliveryOrderQuery.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Queries/GetDeliveryOrder/GetDeliveryOrderQuery.cs) — read DTO + mapper
- [DeliveryOrderEndpoints.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/DeliveryOrderEndpoints.cs) — API surface
- [DeliveryOrderDomainEventMapper.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/Services/DeliveryOrderDomainEventMapper.cs) — domain→integration mapping
- [LocationCodeNormalizer.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Services/LocationCodeNormalizer.cs) — polymorphic Guid|Code handling
- All validators ใน `Application/Commands/*/Validator.cs`
- [AMR_Delivery_Planning_System_Design.md](AMR_Delivery_Planning_System_Design.md) §1 line 87-124 — design doc reference

---

## Verification (เมื่อจะ execute backlog)

ไม่ต้อง verify ใน review เอง — แต่เวลา execute ราย item ใช้แบบนี้:

1. **Unit tests** ใน [tests/Modules/DeliveryOrder.UnitTests](tests/Modules/DeliveryOrder.UnitTests) สำหรับ entity invariants ใหม่ (เช่น Hazmat invariants)
2. **Integration tests** ใน [tests/Modules/DeliveryOrder.IntegrationTests](tests/Modules/DeliveryOrder.IntegrationTests) สำหรับ end-to-end payload (Testcontainers Postgres + RabbitMQ)
3. **`dotnet build`** ทั้ง solution หลังเพิ่ม field — ตรวจ DTO drift (read vs write)
4. **Manual curl** ที่ `/api/v1/delivery-orders` + `/upstream` + `/bulk` ให้ครอบทั้ง 3 path
5. **OpenAPI inspect** ที่ Swagger UI — ดู contract ที่ generate ตรงกับ DTO ใหม่
6. **Outbox processor verify** — ส่ง order ใหม่แล้วเช็คว่า message ลง `OutboxMessages` table และ Planning consumer pick up (ถ้า P0-3 จริง)

---

## Decision Records

ตัดสินใจ 2026-05-25 — ปลดล็อก P1 execution:

| # | Decision | Choice | Rationale (สั้น) | Affects |
|---|---|---|---|---|
| **1** | API versioning strategy | **Path-based** `/api/v1/delivery-orders` | Upstream enterprise (SAP/ERP/MES) friendly; discoverability ใน URL/log; design doc §1 line 102 ระบุไว้แล้ว | P1-7 |
| **2** | `Confirmed` vs `ReadyToPlan` | **คง `Confirmed`** + update design doc | Code เป็น source of truth (commit `3fc95e07` sync แล้ว); rename มีต้นทุนสูง (event names, Planning consumer, migration); business semantic กว้างกว่า | Design doc only — no code change |
| **3** | Multi-tenant reintroduction | **ไม่ดึงกลับ** ตอนนี้ + ปิด gap ใน design doc | No confirmed multi-subsidiary customer; `facilityId` ครอบคลุม multi-plant use case; complexity tax สูง | P2-7 placeholder คงไว้; design doc §7 resolved |
| **4** | UOM whitelist scope | **Option C — Hybrid** (closed enum + alias map) | In-plant unit set แคบ; Planning solver ต้องการ closed set; upstream ส่ง variant ได้ผ่าน alias normalization | P1-9 |
| **5** | Hazmat scope | **Option C** — Class + Subdivision + PackingGroup (no UN number) | Solver ต้องการอย่างน้อย class+subdivision เพื่อทำ segregation matrix; full UN over-engineering สำหรับ Phase 1; schema extend-friendly ภายหลัง | P1-3 |

### Doc updates ที่ทำพร้อมกัน (2026-05-25)
- [AMR_Delivery_Planning_System_Design.md line 97](AMR_Delivery_Planning_System_Design.md#L97): `READY_TO_PLAN` → `CONFIRMED` (Decision #2)
- [AMR_Delivery_Planning_System_Design.md line 576](AMR_Delivery_Planning_System_Design.md#L576): same rename ใน progress table (Decision #2)
- [AMR_Delivery_Planning_System_Design.md §7](AMR_Delivery_Planning_System_Design.md#L538): resolve "Multi-tenant" Open Question (Decision #3)

### Verification status
- ~~**Outbox processor** — verify ก่อนว่ามี/ไม่มี (P0-3 อาจ false alarm)~~ ✅ verified + retry mechanism added (`552c13be`)

## Next Suggested Steps

**P0 ครบหมดแล้ว ✅ + Decisions 1–5 resolved ✅** — พร้อม execute P1:

### Progress (2026-05-25)

**4/10 P1 items done** + smoke-tested end-to-end via docker:

| # | Item | Status | Commit |
|---|---|---|---|
| 1 | P1-7 path versioning | ✅ | `cea473e` |
| 2 | P1-2 SlaTier + SubmittedAt | ✅ | `8982a1e` |
| 3 | P1-6 `:hold` / `:release` | ✅ | `826c4ea` |
| 4 | P1-1 ServiceWindow VO | ✅ | `d9c4e93` + `f210ce5` |
| 5 | P1-9 Quantity VO + UOM enum | ✅ | _this commit_ |
| 6 | P1-3 HazmatInfo VO | ⏳ | — |
| 7 | P1-4 TemperatureRange VO | ⏳ | — |
| 8 | P1-5 handlingInstructions | ⏳ | — |
| 9 | P1-8 event versioning + remove `Deadline` alias | ⏳ (after P1-3/P1-4) | — |
| 10 | P1-10 amendment snapshot scope | ⏳ (last) | — |

### Remaining execution order

1. ~~**P1-9** Quantity VO + UOM enum~~ ✅ done — Option C Hybrid; closed enum + alias map
2. **P1-3** HazmatInfo VO **[M]** — `classCode` (`^[1-9](\.[1-6])?$`) + optional `PackingGroup` (I/II/III) on Item per Decision #5; nullable VO; migration with EF `OwnsOne`
3. **P1-4** TemperatureRange VO **[S]** — `minC`/`maxC` on Item, both nullable independently, invariant `minC ≤ maxC`
4. **P1-5** handlingInstructions **[S]** — flags enum or string-list on Item (decide before implement: enum is type-safe but rigid; flags are extensible)
5. **P1-8** Event versioning + remove `Deadline` alias **[M]** — add `schemaVersion` field to integration events; rename class `…V1`; drop `Deadline` from Confirmed event (announced as backward-compat-only since `f210ce5`); P1-3/P1-4 new fields ride along the same V1→V2 bump
6. **P1-10** Amendment snapshot scope **[M]** — extend `OriginalSnapshot`/`NewSnapshot` to cover Priority, Items, SlaTier, ServiceWindow, Hazmat, etc.; add `amendmentVersion`

### Architecture seams ที่ความตั้งใจคงเดิม
- **Path versioning** = path-based `/api/v1/...` (Decision #1; P1-7 ✅)
- **State naming** = `Confirmed` (Decision #2; doc synced)
- **Multi-tenancy** = single-tenant Phase 1–4 (Decision #3; P2-7 placeholder)
- **UOM** = closed enum + alias map (Decision #4; P1-9 plan)
- **Hazmat** = Class + Subdivision + PackingGroup, no UN number (Decision #5; P1-3 plan)

### Verified ใน docker (2026-05-25)
- 6 old un-versioned paths → 404; 6 new `/api/v1/*` → respond
- Create draft (`ServiceWindow` + `SlaTier`) → `submittedAt: null` ใน DRAFT
- Submit → `submittedAt` populated; status `VALIDATED` (auto-validate inside Submit handler)
- Confirm → `submittedAt` frozen; status `CONFIRMED`; outbox publishes `DeliveryOrderConfirmedIntegrationEvent` carrying `Priority`, `SlaTier`, `Earliest`, `Latest`, `Deadline (= Latest)`, `SubmittedAt`, `Items[]`
- Hold (204) → status `HELD`; outbox publishes `DeliveryOrderHeldIntegrationEvent`
- Release (200 + warnings) → status back to `CONFIRMED`; re-fires Confirmed event (Planning auto-picks up again)
- Timeline: `OrderSubmitted → OrderConfirmed → OrderHeld → OrderReleased`

## P0-4 Known follow-ups (P2 candidate)
- OpenAPI metadata for `Idempotency-Key` header — ปัจจุบัน Swagger UI ไม่แสดง required header (package `Microsoft.AspNetCore.OpenApi` อยู่แค่ Api project, ไม่ได้ reference ใน Presentation). เพิ่มภายหลังโดย register filter ที่ Api หรือเพิ่ม package ใน Presentation
- Cross-module reuse — filter ปัจจุบันอยู่ใน `DeliveryOrder.Presentation/Idempotency/`; ถ้าจะใช้กับ Planning/Dispatch/Fleet/Facility ต้องย้ายไปเป็น shared web project

## P0-5 Known follow-ups (P2 candidate)
- Refine WeightFallback — ปัจจุบัน flat 500 kg. ขยายเป็น per LoadUnitProfile.MaxGrossWeightKg (cross-module Facility lookup) หรือ fleet max (Fleet lookup) ตามที่อภิปรายใน B1b
- `GET /api/v1/delivery-orders/{id}` ยังไม่ surface warnings — ต้องเรียก `/timeline` แล้วกรอง `QualityWarning` events. พิจารณาเพิ่ม `qualityIssues[]` ใน detail response
- Warning catalog versioning — ถ้าเพิ่ม code ใหม่ (เช่น `ITEM_DIMS_MISSING`, `STATION_NEAR_CAPACITY`) ควรมี registry + docs
