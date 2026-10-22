# Edit & resend hardening (P0) — implementation plan (rev 2)

> **Status**: 🟡 Phases 1–4 committed (7e29fa67, 3bc677db, ccdcab41, 45c05fc8) + 5.1 EF harness PASS on dev Postgres (rolled back) — 2026-10-22. **Not deployed** (user: deploy later, at a quiet time); 5.3 live test by the user
> **Ask**: ทำให้ order ที่ Failed แก้ไขแล้วส่งซ้ำได้อย่างปลอดภัยบนระบบจริง — ปิดช่องโหว่, เช็ค route (template + resolve) ก่อนส่งซ้ำ, แยก idempotency ตามผู้เรียก, แล้ว commit + deploy
> **Kind / size**: feature + hardening · M, ~11.5h, 5 phases
> **Parent**: งาน Edit & resend ใน working tree (ยังไม่ commit)
> **Assumptions** (คำตอบผู้ใช้ 2026-10-22): ขอบเขต = P0 · เช็ค "มี template + resolve ผ่าน" · แก้ idempotency ในรอบนี้ · item Cancelled ไม่ส่งซ้ำโดย default (→ ดู E1: เคสนี้เกิดไม่ได้ จึงไม่ต้องสร้าง UI)

## Context

OMS ส่ง order → DTMS ตอบ 201 → Planning หา OrderTemplate ไม่เจอ → order FAILED เงียบ ๆ และไม่มีทางแก้ข้อมูลแล้วส่งใหม่ (`UpdateDraft` ใช้ได้แค่ Draft — `DeliveryOrder.cs:156-160`). Session นี้เขียน Edit & resend + แก้บั๊ก re-plan ไว้แล้วแต่ยังไม่ commit

## The change in one sentence

ผู้ใช้กด "Edit & resend" บน order FAILED แก้ item/สถานี/priority/window ได้; route ที่ไม่มี template ที่ใช้ได้ถูกปฏิเสธทันทีพร้อมชื่อ route; ถ้าผ่าน order วิ่งใหม่และ OMS ได้ callback ด้วย shipmentId เดิม

## Goal / non-goal

**Goal**: ฟีเจอร์อยู่บน `main` + รันใน container; ทดสอบ order FAILED จริง 1 ตัว → trip ใหม่ `…-G1-A2` → `shipment.started.v1` = Notified

**Non-goal** (เลื่อนไป "Later"): P1 422 ตอนรับ order · P2 GET สถานะ order · P3 `shipment.failed.v1` (OMS ลบ `/failed` แล้ว — `docs/oms-shipment-cancel-contract.md:15-17`)

---

## Plan review (rev 1 → rev 2) — สิ่งที่ผิด/เสี่ยงและที่ควรจะเป็น

### ข้อผิดพลาดใน plan rev 1 (ถ้าทำตามจะเกิดบั๊กหรือเสียเวลา)

| # | ส่วนของ rev 1 | ปัญหา (มีหลักฐาน) | ผล | ที่ควรจะเป็น |
|---|---|---|---|---|
| E1 | Phase 3.2 UI กัน item Cancelled (2h) | Item กลายเป็น Cancelled ได้เฉพาะตอน order เป็น Cancelled/Rejected (`TripCancelledConsumer.cs:95-96`, `CancelDeliveryOrderCommandHandler.cs:43`, `AbandonStuckDeliveryOrderCommandHandler.cs:56-59`) และ `Reopen` คืนค่าทุกตัวเป็น Pending (`DeliveryOrder.Reopen`) → **order Failed ไม่มีทางมี item Cancelled** | เสีย 2h + สร้างบั๊กใหม่: item ที่ถูกตัดออกถ้ามี POD จะโดน error "has scan history" ที่ผู้ใช้ไม่ได้ทำ | **ตัดทิ้ง** — R4 ไม่ใช่ปัญหาจริง |
| E2 | Phase 4 ใช้ `http.User.Identity!.Name` เป็น scope ผู้ใช้ | ผู้ใช้อยู่ใน claim `EmployeeId`; `NameClaimType` = WS-Fed URI (`Program.cs:194`, `MapInboundClaims=false` `:169`) → `Identity.Name` อาจเป็น null ทุกคน (`HttpContextCurrentUserAccessor.cs:35-45`) | ผู้ใช้ทุกคนรวมเป็น scope `usr:` เดียว → ไม่ได้แก้อะไร, `!` ซ่อนปัญหา | อ่าน `EmployeeId` ?? `Identity.Name` จาก `HttpContext` ต่อ request (filter เป็น **Singleton** — `ModuleServiceRegistration.cs:939` — inject service scoped ไม่ได้: captive dependency) |
| E3 | Phase 2.2 เพิ่ม abstract member ใน `IDispatchStrategy` | มี implementers: `AmrDispatchStrategy`, `ManualDispatchStrategy`, fake ใน `AmrDispatchStrategyTests.cs`, `StrategyRegistryAndRouterTests.cs` | build พังหลายไฟล์ | **default interface method** คืน `Result.Success()`; override เฉพาะ AMR |
| E4 | Phase 2.1 refactor `DispatchByRouteAsync` เพื่อแชร์ helper | เป็น hot path ของ dispatch ทุก order | เสี่ยง regression กับ order ปกติทั้งหมดเพื่อ feature ที่ใช้นาน ๆ ครั้ง | ไม่แตะ `DispatchByRouteAsync`; เพิ่ม `CheckRouteAsync` ที่เรียก dependency เดิม 2 ตัว (`FindByRouteAsync` + `ResolveAsync`) และใช้ข้อความ error เดียวกัน |
| E5 | Phase 5.1 "ห่อ transaction ด้วย execution strategy" | harness สร้าง context เองได้โดยไม่เปิด retry (`DeliveryOrderDbContext(options)` `:38`) | ซับซ้อนเกินจำเป็น | harness ไม่เปิด `EnableRetryOnFailure`; ใช้ `DeliveryOrderRepository` จริง (ไม่ใช่เขียน RemoveRange เอง) เพื่อไม่ให้ผ่านแบบหลอก |
| E6 | R5 ให้ความรุนแรงสูงเกินจริง | replay ข้ามระบบต้อง key **และ** body เหมือนกัน; ผลที่เกิดได้จริงคือ 422 conflict หลอกเมื่อ key ชนกัน | จัดลำดับงานผิด | คงไว้ แต่ severity Low–Med |

### ปัญหาประสิทธิภาพในโค้ดที่เขียนไปแล้ว (ไม่อยู่ใน rev 1)

| # | ปัญหา | หลักฐาน | ผล | ที่ควรจะเป็น |
|---|---|---|---|---|
| P1 | `NextAttempt` เรียก `GetByDeliveryOrderIdAsync` ใน planning consumer — **ทุก order ใหม่** | query เป็น tracked + `Include(t => t.Events)` (`TripRepository.cs:225-235`); `dispatch.Trips` **ไม่มี index บน DeliveryOrderId** (snapshot มี index แค่ JobId/PreviousAttemptId/UpperKey — `DispatchDbContextModelSnapshot.cs:272-278`) | seq scan ทั้งตาราง + โหลด execution events ต่อทุก order; โตตามจำนวน trip; entity ถูก track ใน scope ของ consumer | (1) repo method ใหม่ `AsNoTracking` + projection เฉพาะ `Id, UpperKey, AttemptNumber, Status, PreviousAttemptId` (2) migration `IX_Trips_DeliveryOrderId` — ได้ประโยชน์กับ Reopen/Redispatch/drawer/cancel dialog ที่เรียกอยู่แล้วด้วย |

### ช่องโหว่ของ Edit & resend (คงไว้)

| # | ช่องโหว่ | แก้ใน |
|---|---|---|
| R1 | re-plan ใช้ upperKey ซ้ำ → หุ่นวิ่งแต่ผูกกับ trip ตาย (กระทบปุ่ม Redispatch เดิมด้วย) — `EnvelopeUpperKey.cs:39-41`, `CreateEnvelopeTripCommandHandler.cs:26-30` | ✅ เขียนแล้ว + P1 → Phase 1 |
| R2 | ItemId ซ้ำชน unique `(DeliveryOrderId, ItemId)` → 409 ข้อความ generic | Phase 3 |
| R3 | ส่งซ้ำไป route ไม่มี template → FAILED อีกรอบเงียบ ๆ | Phase 2 |
| R5 | idempotency key ไม่ผูกผู้เรียก (`IdempotencyKeyFilter.cs:24,80`) | Phase 4 |
| R6 | owned-type replace + ลบ/เพิ่ม item ใน SaveChanges เดียว ไม่เคยรันกับ Postgres | Phase 5.1 |
| R7 | 422 idempotency conflict กับ 422 route check แยกกันด้วย `title` เท่านั้น (`IdempotencyKeyFilter.cs:98-104`) | Phase 2 |

### ผลกระทบประสิทธิภาพรวม (หลังแก้ตาม rev 2)

| จุด | ต้นทุน | ความถี่ | สรุป |
|---|---|---|---|
| Route check | 1 query template + ≤N query action-template ต่อ route | เฉพาะตอน Edit & resend | ไม่มีนัยสำคัญ |
| NextAttempt (หลังแก้ P1) | 1 index seek คืน 0 แถว (order ใหม่) | ทุก order | ~ไม่มี |
| Index ใหม่ | ต้นทุน insert trip +1 index | ทุก trip | เล็กมาก, ได้คืนจาก query ที่มีอยู่ 4–5 จุด |
| Idempotency scope | ต่อ string | ทุก mutation | ไม่มี |

### ผลกระทบต่อการทำงานหลัก (order ปกติที่ไม่ fail)

หลักการ: ทุกการเปลี่ยนต้อง **no-op สำหรับ order ปกติ** — ตารางนี้ยืนยันทีละจุดว่าแตะเส้นทางหลักหรือไม่

| จุดที่เปลี่ยน | อยู่บนเส้นทางหลัก? | พฤติกรรมกับ order ปกติ | ความเสี่ยง / การป้องกัน |
|---|---|---|---|
| `NextAttempt` ใน planning consumer | **ใช่ — ทุก order** | order ใหม่ไม่มี trip → attempt 1, upperKey `…-G1` เหมือนเดิมทุกตัวอักษร | ต้นทุน query: แก้ด้วย P1 (index + projection) — **ห้าม deploy โค้ดใน working tree ตอนนี้โดยไม่มี Phase 1.1–1.2**; DB error → consumer throw → MassTransit retry เหมือน query อื่น |
| `ApplyLocationMaps` (ย้าย loop ออกจาก `MarkAsValidated`) | **ใช่** — intake OMS (`CreateUpstream…:173`), Submit (`:92`), BulkSubmit (`:153`) | refactor แบบคงพฤติกรรม: ลำดับ check สถานะ → map เหมือนเดิม | ครอบด้วย test เดิมของ `MarkAsValidated` (402 ผ่าน) |
| `MarkJobDispatched` rebind | ใช่ (ทุก group) | job ใหม่เป็น Created → เข้า `MarkDispatched` เหมือนเดิม; สาขาใหม่ทำงานเฉพาะ job Failed/Cancelled | ไม่มี |
| `CreateJobAnchor` re-anchor | ใช่ (ทุก group) | order ใหม่ไม่มี job → สร้างใหม่เหมือนเดิม | ไม่มี |
| Idempotency filter | **ใช่ — mutation ทุกตัว** (19 endpoint UI + source create) | UI สร้าง key สุ่มต่อครั้ง → ไม่ต่าง; OMS อยู่ scope เดียว | cutover v2→v3 (ดูตาราง external) |
| `IDispatchStrategy.CheckRoutesAsync` (default method) | ไม่ — consumer ไม่เรียก | — | ไม่มี |
| middleware arm 422 ใหม่ | ไม่ | — | ไม่มี |
| index `IX_Trips_DeliveryOrderId` | ทุก insert trip | +1 index write | เล็กมาก; ทำให้ Reopen/Redispatch/drawer/cancel dialog เร็วขึ้น |
| `create-dialog.tsx` | **ใช่ — ใช้ร่วมกับ New order / Edit draft / Reorder** | `formFromOrder` เพิ่ม `id`/`reason` แต่ `buildPayload` ไม่ส่ง → payload create/edit ไม่เปลี่ยน; field ที่ล็อกทำงานเฉพาะ revise mode | regression check ด้วยมือ 3 dialog (Verification) |
| ปุ่มใน drawer / row menu | ไม่ — แสดงเฉพาะ Failed | — | ไม่มี |

**พบบั๊กเดิมบนเส้นทางหลัก (H1) — Hold → Release ระหว่าง order กำลังวิ่ง**
- `Hold` ทำได้ทุกสถานะที่ยังไม่จบ รวม Dispatched/InProgress (`DeliveryOrder.cs:709-713`); `Release` ยิง Confirmed event ซ้ำ (`:719-726`) → planning dispatch **ทุก group ใหม่**
- **ก่อนแก้ (main ตอนนี้)**: group ที่ trip กำลังวิ่ง/เสร็จแล้ว → ส่ง upperKey เดิมให้ RIOT3 → RIOT3 ไม่ dedupe → **หุ่นตัวที่สองวิ่งโดย DTMS ไม่ติดตาม** (trip เดิมถูกคืนจาก `CreateEnvelopeTrip`)
- **โค้ดใน working tree**: trip ที่วิ่งอยู่ → เหมือนเดิม (หุ่นซ้ำ); trip ที่ Completed → สร้าง trip ใหม่ attempt+1 ที่ไม่มี item (Delivered ถูกข้าม) → หุ่นวิ่งเปล่า + OMS ได้ started ซ้ำ → **ยังผิด**
- **ที่ควรจะเป็น** (Phase 1.3): `NextAttempt` คืน "Skip" เมื่อ tip ยังไม่จบ (Created/InProgress/Hang/Held) หรือ Completed → consumer ไม่ส่ง RIOT3, นับ group เป็นสำเร็จ (item ผูกกับ trip เดิมอยู่แล้ว); ส่งใหม่เฉพาะ tip Failed/Cancelled/Rejected. ครอบทั้ง MassTransit redelivery (ไม่ยิงซ้ำอีก) และ Release
- เคสเดียวที่ยังส่ง upperKey เดิม: RIOT3 รับแต่บันทึก trip ล้ม (orphan, ไม่มีแถวใน DB) → attempt 1 เหมือนเดิม

### ผลกระทบต่อ external system

| ระบบ | เปลี่ยนอะไร | ผล | ความเสี่ยง / สิ่งที่ต้องทำ |
|---|---|---|---|
| **OMS — inbound API** | ไม่มีการเปลี่ยน contract ใน P0 (422 route check อยู่บน endpoint ของ UI เท่านั้น) ยกเว้น Phase 4: cache key ของ idempotency เปลี่ยนเป็น v3 | OMS เป็นผู้เรียกเดียวใน scope `sys:oms` → พฤติกรรมเหมือนเดิม | entry ที่ cache ไว้ก่อน deploy (≤24h) จะไม่ replay → ถ้า OMS retry ด้วย key เก่าหลัง deploy จะได้ **409** แทน 201. deploy ช่วงไม่มี order เข้า |
| **OMS — callback (order fail ก่อนมี trip)** เช่นไม่มี template ซึ่งเป็นเคสของคุณ | resend สร้าง trip แรก (attempt 1) ได้ shipmentId ใหม่ | started → pickedup → droppedoff ไหลปกติ | ไม่มี |
| **OMS — callback (order ที่มี trip แล้วเคย started)** | resend ใช้ shipmentId เดิม (root ของ chain) เหมือน Reopen + retry ในปัจจุบัน | `shipment.started` ซ้ำ → OMS เป็น create-once ตอบ **400** (`HttpCallbackFailureClassifier.cs:24`) → DTMS ปิดเป็น Rejected ทันที **ไม่บล็อก** callback อื่น; pickedup/droppedoff ไปที่ shipment เดิม | ใน OMS panel จะเห็น started = Rejected ในเคสนี้ ซึ่งคาดไว้แล้ว ไม่ใช่ปัญหาใหม่ (Reopen ทำแบบนี้อยู่แล้ว) |
| **OMS — callback เมื่อแก้สถานี** | `locationCode` ใน pickup-arrived/droppedoff เป็นค่าใหม่ | ถ้า OMS ตอบ 400 → terminal ไม่บล็อก. **ถ้า OMS ตอบ 404 → retry 30s…2h ≈ 2h45m และ partition ของ OMS ถูก head-block: callback ของ order อื่น ๆ ค้างทั้งหมด** (`HttpCallbackFailureClassifier.cs:5-16` — 404 ไม่ใช่ permanent) | **เสี่ยงสูงสุด** → ถาม OMS ก่อนเปิดใช้การแก้สถานีกับ order ของ OMS (Open question ข้อ 1) |
| **RIOT3** | resend = ส่ง task จริง (หุ่นวิ่งจริง); upperKey รูปแบบ `…-G1-A2` | รูปแบบ `-A{n}` ถูกใช้อยู่แล้วโดย trip retry → RIOT3 รับได้. R1 fix **ลด** task กำพร้า: Redispatch ปัจจุบันส่ง upperKey ซ้ำ ซึ่ง RIOT3 ไม่ dedupe → หุ่นวิ่งโดยที่ DTMS ติดตามไม่ได้ | live test (5.3) จะทำให้หุ่นวิ่งจริง → นัดหน้างานก่อน |
| **WMS** | ไม่มี — Manual ใช้ snapshot ใน DB (`BuildWmsLocationMapAsync`) | — | — |
| **Auth service / LDAP** | ไม่มี permission ใหม่ (ใช้ `order:write` + `order:reopen`) | — | — |
| **Redis** | prefix idempotency `v3` | key v2 หมดอายุเองภายใน 24h | — |
| **Postgres** | index ใหม่บน `dispatch.Trips` | lock สั้น ๆ ตอน migrator รัน (ช่วงที่ api ปิดอยู่แล้ว) | — |

### Known gaps (ยอมรับ, บันทึกไว้)
- re-plan ที่ทำให้จำนวน group ลดลง → Job ของ group เก่าค้าง Failed ในแท็บ Jobs (`Job.MarkCancelled` no-op บน Failed — `Job.cs:298`) — cosmetic
- group index เลื่อน (route เปลี่ยน) → chain ต่อกับ trip ของ route เดิม, OMS เห็น shipmentId เดิมแต่ locationCode ใหม่
- `ActionTemplateRepository.GetByNameAsync` ไม่กรอง IsActive — check ต้องเหมือน dispatch จึงไม่กรองเช่นกัน

---

## Pre-flight (~20 min)

- `src/Modules/Planning/DTMS.Planning.Application/Consumers/DeliveryOrderValidatedConsumer.cs` — `NextAttempt` (ท้ายไฟล์) + จุดเรียก `priorTrips`
- `src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/Repositories/TripRepository.cs:225-235`
- `src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Commands/ReviseAndResendDeliveryOrder/ReviseAndResendDeliveryOrderCommandHandler.cs`
- `src/Modules/Planning/DTMS.Planning.Application/Services/DispatchOrderTemplateService.cs:30-91`
- `src/DTMS.Api/Adapters/AmrDispatchStrategy.cs:30-71`, `src/DTMS.Api/Middlewares/ExceptionHandlingMiddleware.cs:120-160`
- `src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Presentation/Idempotency/IdempotencyKeyFilter.cs`

| Decision | Default | When to pick the alternative |
|---|---|---|
| query สำหรับ NextAttempt | repo method ใหม่ projection + AsNoTracking | — |
| index | migration มือ `IX_Trips_DeliveryOrderId` (MigrationId ไม่ชนกับ context อื่น) | ถ้าตารางใหญ่มากจน lock นาน → `CREATE INDEX CONCURRENTLY` (ต้อง `suppressTransaction: true`) |
| route check | default interface method บน `IDispatchStrategy.CheckRoutesAsync`; AMR override → `IDispatchOrderTemplateService.CheckRouteAsync` | — |
| 422 | `RouteNotDispatchableException` + middleware arm title "Route Not Dispatchable" | — |
| idempotency scope | `idempotency:v3:{scope}:{key}` อ่าน claim จาก `HttpContext` (filter เป็น singleton) | — |
| cutover v2→v3 | เปลี่ยนตรง ๆ, deploy ช่วงเงียบ | OMS retry คร่อม deploy บ่อย → dual-read v2 24h |

**Estimate anchor**: `adddbed0` (422 mode gate; ส่วน gate ≈ exception + middleware arm + 3 handlers + 4 test files ~380 บรรทัด) ≈ Phase 2

---

## Phase 1 — Re-plan fix ที่เร็วและถูกต้อง (R1 + P1 + H1) (~3h)

### 1.1 Lightweight query (~0.75h)
- Edit `Dispatch.Domain/Repositories/ITripRepository.cs`: เพิ่ม
  ```csharp
  Task<IReadOnlyList<TripAttemptRef>> GetAttemptRefsByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken ct = default);
  public sealed record TripAttemptRef(Guid Id, string UpperKey, int AttemptNumber, TripStatus Status, Guid? PreviousAttemptId);
  ```
- `TripRepository.cs`: `AsNoTracking().IgnoreQueryFilters().Where(DeliveryOrderId==id).Select(new TripAttemptRef(...))`
- `DeliveryOrderValidatedConsumer`: ใช้ method ใหม่; `NextAttempt` รับ `IReadOnlyList<TripAttemptRef>`
- Tests: ปรับ `ReplanAttemptNumberingTests.cs` ให้สร้าง `TripAttemptRef`; `DeliveryOrderValidatedConsumerModeGuardTests.cs` substitute ยังใช้ได้

### 1.2 Index migration (~0.5h)
- New: `Dispatch.Infrastructure/Migrations/2026102210xxxx_AddTripsDeliveryOrderIdIndex.cs` (เขียนมือ — dotnet-ef ใช้ไม่ได้; ตรวจ MigrationId ไม่ซ้ำ timestamp กับอีก 7 context) + `HasIndex(t => t.DeliveryOrderId)` ใน `DispatchDbContext.cs` + อัปเดต snapshot ด้วยมือ
- SQL: `CREATE INDEX IF NOT EXISTS "IX_Trips_DeliveryOrderId" ON dispatch."Trips" ("DeliveryOrderId");` / Down: `DROP INDEX IF EXISTS dispatch."IX_Trips_DeliveryOrderId";`

### 1.3 ไม่ dispatch ซ้ำ group ที่มี trip วิ่งอยู่หรือเสร็จแล้ว (H1) (~1h)
- `NextAttempt` คืน `(Skip: bool, AttemptNumber, PreviousAttemptId)`; Skip เมื่อ tip เป็น Created/InProgress/Hang/Held/Completed
- Consumer loop: Skip → log `[AutoPlan] Group {G} already has trip {TripId} ({Status}) — not re-dispatching`, `successCount++`, `continue` (ไม่เรียก strategy, ไม่ MarkJobDispatched)
- Tests (`ReplanAttemptNumberingTests.cs`): tip InProgress → Skip; tip Completed → Skip; tip Failed → attempt+1 (แทน test `Redelivery_WithLiveTip_KeepsItsAttempt` เดิม)
- Test consumer: order ที่ group 1 Completed + group 2 Failed → Release → strategy ถูกเรียกครั้งเดียว (group 2)

### 1.4 Commit (~0.25h + ทดสอบ)
`dotnet build DTMS.slnx -m:1` + `dotnet test tests/Modules/Planning.UnitTests -m:1` + `Dispatch.UnitTests`
Files: Planning consumer, `MarkJobDispatched…Handler`, `CreateJobAnchor…Handler`, Planning csproj, ITripRepository/TripRepository, DispatchDbContext + migration, 2 test files

**Commit**: `fix(dispatch): re-planning sends a fresh trip only for groups that failed, and never a second robot for one already running or done`

---

## Phase 2 — Route check ก่อนส่งซ้ำ (R3, R7) (~3h)

### 2.1 `CheckRouteAsync` (~0.75h)
`IDispatchOrderTemplateService` + `DispatchOrderTemplateService`: method ใหม่ใช้ `_templateRepository.FindByRouteAsync` + `_resolver.ResolveAsync` (catch `InvalidOperationException`) คืน `Result`; **ไม่แตะ** `DispatchByRouteAsync`
Tests (`tests/Modules/Planning.UnitTests/`, fake มีที่ `UnitTest1.cs:1313`): ไม่มี template → Failure; action หาย → Failure; ครบ → Success

### 2.2 Strategy + exception + 422 (~1h)
```csharp
// IDispatchStrategy
Task<Result> CheckRoutesAsync(IReadOnlyCollection<(Guid Pickup, Guid Drop)> routes, CancellationToken ct)
    => Task.FromResult(Result.Success());   // default: Manual/others need no check
```
- `AmrDispatchStrategy.cs` override: วน `CheckRouteAsync`, รวม failure ทั้งหมดเป็นข้อความเดียว
- New `RouteNotDispatchableException` ใน `IDispatchStrategyRegistry.cs` ข้าง `TransportModeNotEnabledException`
- `ExceptionHandlingMiddleware.cs` arm → `(422, "Route Not Dispatchable")`
- Tests: `tests/DTMS.Api.UnitTests/ExceptionHandlingMiddlewareTests.cs` (copy arm test ของ mode gate), `AmrDispatchStrategyTests.cs` (route ไม่ผ่าน → Failure ที่มีทุก route)

### 2.3 เสียบใน handler (~1.25h)
`ReviseAndResendDeliveryOrderCommandHandler`: หลัง `BuildStationMapAsync` สำเร็จ (AMR) → distinct `(stationMap[i.PickupLocationCode], stationMap[i.DropLocationCode])` → `strategy.CheckRoutesAsync` → Failure: throw `RouteNotDispatchableException` ข้อความแปลง Guid → location code (`No active OrderTemplate for route SHELF3 → PACK-1`) — **ก่อน** `ResendAfterRevision` และ `SaveChangesAsync`
Tests: new `tests/Modules/DeliveryOrder.UnitTests/ReviseAndResendHandlerTests.cs` (copy `Build()` จาก `SubmitDeliveryOrderModeGateTests.cs:60-76`; stub `IOrderAmendmentRepository`, `ITripRepository`→`[]`, `ICurrentUserAccessor`, `IDispatchStrategyRegistry.Get` → strategy substitute): route ไม่ผ่าน → throws + `DidNotReceive().SaveChangesAsync`; ผ่าน → `Received().SaveChangesAsync` + status Confirmed

**Commit** (รวม Edit & resend ทั้งก้อน — domain, command, endpoint, frontend, route check): `feat(orders): fix a failed order's items, stations or schedule and send it again, refused up front when the route has no usable template`

---

## Phase 3 — ItemId ซ้ำ → 400 ชัดเจน (R2) (~0.5h)

`DeliveryOrder.ReviseFailed`: ก่อน mutate ใด ๆ → `revisions.GroupBy(r => r.ItemId.Trim(), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)` → `InvalidOperationException("Item id '{id}' appears more than once …")` (Ordinal = ตรงกับ unique index ของ Postgres)
Test: `ReviseAndResendTests.Revise_DuplicateItemId_Throws`

**Commit**: `fix(orders): resend names a duplicated item id instead of failing with a generic conflict`

---

## Phase 4 — Idempotency แยกตามผู้เรียก (R5) (~1.5h)

`IdempotencyKeyFilter.cs` — **ห้ามเพิ่ม ctor dep ที่เป็น scoped**: filter ลงทะเบียนเป็น `AddSingleton<IdempotencyKeyFilter>()` (`ModuleServiceRegistration.cs:939`, ใช้ผ่าน `AddEndpointFilter<IdempotencyKeyFilter>()` `RequireIdempotencyKeyExtensions.cs:26`) → อ่านตัวตนจาก `HttpContext` ต่อ request แทน โดยใช้ลำดับ claim เดียวกับ `HttpContextCurrentUserAccessor.cs:35-45`
```csharp
private const string CacheKeyPrefix = "idempotency:v3:";
private static string CallerScope(HttpContext http)
{
    if (http.Items["principal"] is SystemPrincipal sp) return $"sys:{sp.Key}";
    var name = http.User.FindFirst("EmployeeId")?.Value ?? http.User.Identity?.Name;
    return string.IsNullOrWhiteSpace(name) ? "anon" : $"usr:{name}";
}
// :80  var cacheKey = CacheKeyPrefix + CallerScope(http) + ":" + key;
```
Tests: filter test ใน `tests/Modules/DeliveryOrder.UnitTests/` — key เดียวกันต่าง scope → execute ใหม่; scope เดียวกัน → replay; system principal ใช้ `sys:`
Docs: `docs/system-onboarding.md` ส่วน idempotency

**Commit**: `fix(api): an idempotency key only replays a response to the caller that made it`

---

## Phase 5 — ตรวจกับ DB จริง, deploy, ทดสอบ live (~2.5h)

### 5.1 EF harness แบบ rollback (R6) (~1h) — **ขออนุญาตผู้ใช้ก่อนรัน**
Scratchpad project: `DeliveryOrderDbContext` (ไม่เปิด retry, ไม่มี interceptor — domain events ไม่ถูกเขียน ซึ่งไม่ใช่สิ่งที่ทดสอบ) → `localhost:5434` → `BeginTransaction` → `DeliveryOrderRepository.GetByIdAsync` order FAILED จริง → `ReviseFailed` (แก้ 1 + เปลี่ยน Quantity/Dimensions, ลบ 1, เพิ่ม 1) → `ResendAfterRevision` → `RemoveItemsAsync/AddItemsAsync/SaveChangesAsync` ผ่าน repository → อ่านกลับ → **ROLLBACK เสมอ**
ถ้าล้ม → Off-ramp ข้อ 3

### 5.2 Deploy (~0.5h)
`powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api` (migrator รัน index migration — exit 139 = migration ล้ม) แล้วค่อย `-Only frontend`; ทีละตัว, ห้าม `docker-compose build`. Deploy ช่วงไม่มี order เข้า (Phase 4 cutover)

### 5.3 Live test (~1h)
1. Timeline ของ `OD-221026-0191` / `OD-221026-0202`: ยืนยันสาเหตุ fail
2. Edit & resend ไป route ไม่มี template → error 422 "Route Not Dispatchable" ในฟอร์ม, order ยัง FAILED
3. (ผู้ใช้สร้าง template หรือเลือก route ที่มี) → Edit & resend → Confirmed → Dispatched; Trips แสดง `…-G1-A2`
4. OMS panel: `shipment.started` Notified, shipmentId = root trip เดิม
5. Jobs: job เดิมกลับ Dispatched (rebind)

---

## Verification

| What | How | Where |
|---|---|---|
| ทั้งหมด | `dotnet build DTMS.slnx -m:1` แล้ว `dotnet test DTMS.slnx -m:1` | — |
| frontend | `cd frontend; npm run typecheck` | — |
| index ใช้งาน | `EXPLAIN SELECT … FROM dispatch."Trips" WHERE "DeliveryOrderId" = '<id>'` → Index Scan | psql localhost:5434 |
| **เส้นทางหลักไม่เปลี่ยน** (หลัง deploy) | OMS ส่ง order ปกติ 1 ตัว → log `[AutoPlan]` upperKey `…-G1` (ไม่มี `-A`), trip วิ่งจบ, callback ครบ 3 ตัว Notified | api logs / OMS panel |
| **dialog ที่ใช้ร่วมกัน** | ด้วยมือ: New order → สร้างได้; Edit draft → บันทึกได้, ไม่มีช่อง reason; Reorder → orderRef ว่าง, item คัดลอกมา | browser |
| H1 | Hold order ที่ InProgress → Release → log `already has trip … not re-dispatching`, RIOT3 ไม่มี task ใหม่ | api logs / RIOT3 UI |
| EF จริง | harness 5.1 (rollback) | localhost:5434 |
| production | log `[ReviseAndResend] … sent back to Planning`, `[AutoPlan] … UpperKey …-A2`; outbox `shipment.started.v1` processed; ไม่มี 5xx ของ `/revise-and-resend` ใน api log | api logs / DB |

## Docs to update
- `docs/system-onboarding.md` — idempotency scope (Phase 4)
- คัดลอก plan นี้ไป `docs/plans/edit-and-resend-hardening.md` ตอนเริ่ม Phase 1
- ADR: ไม่ต้อง

## Definition of done
- [ ] 4 commits บน `main` (Phase 1, 2, 3, 4)
- [ ] full suite + typecheck ผ่าน; EXPLAIN เป็น Index Scan
- [ ] harness rollback ผ่าน
- [ ] live: order FAILED จริง → resend → `shipment.started` Notified
- [ ] เวลารวม ≤ ~16h

## Off-ramps
- **Phase 2 เกิน 4h**: ลดเป็น "มี template" อย่างเดียว — handler เรียก `IOrderTemplateRepository.FindByRouteAsync` ตรง (DeliveryOrder.Application อ้าง Planning.Domain อยู่แล้ว) ไม่แตะ strategy
- **Phase 4 กังวล retry คร่อม deploy**: dual-read v2 24h แล้วลบ
- **5.1 owned-type ล้ม**: `Item.Revise` สร้าง VO ใหม่อยู่แล้ว — ถ้า EF ไม่ยอม ให้ remove+add แทนแก้ในที่ (เสีย ItemSeq/POD continuity → ถามผู้ใช้ก่อน)

## Risks & rollback

| Risk | Likelihood | Mitigation / rollback |
|---|---|---|
| owned-type update พังบน Postgres | Med | 5.1 ก่อน deploy; rollback `git revert` commit Phase 2 + rebuild api |
| index migration ล้มที่ migrator | Low | `IF NOT EXISTS`; Down drop index |
| redelivery เด้ง attempt | Low | tip non-terminal → ใช้ attempt เดิม (มี test) |
| prefix v3 → OMS retry คร่อม deploy ได้ 409 | Low | deploy ช่วงเงียบ / dual-read |
| OMS ปฏิเสธ `locationCode` ใหม่หลังเปลี่ยนสถานี | Med | Open question ด้านล่าง |

Open questions (ถาม OMS — ไม่ block การ deploy แต่ block การ "แก้สถานี" กับ order ของ OMS บน production):
- [ ] 1. ถ้า `locationCode` ใน pickup-arrived / droppedoff ต่างจาก order เดิม OMS ตอบ status อะไร (ต้องไม่ใช่ 404 — ไม่งั้นบล็อก callback ทั้ง partition ~2h45m). ถ้ายังไม่ได้คำตอบ: แจ้งผู้ใช้ให้แก้เฉพาะ item/priority/window กับ order ของ OMS
- [ ] 2. started ซ้ำสำหรับ shipmentId เดิมยังตอบ 400 อยู่ หรือเปลี่ยนเป็น 409 แล้ว (memory: OMS จะเปลี่ยนเป็น 409)

## What would invalidate this plan
- OMS ถือว่า shipmentId ที่ fail แล้ว "ปิด" และไม่รับ started ซ้ำ → ต้องเริ่ม chain ใหม่ (`PreviousAttemptId = null`)

## Later, not now
P1 422 ตอนรับ order (reuse `CheckRoutesAsync` ใน `CreateUpstreamDeliveryOrderCommandHandler` หลัง `:157`, `SubmitDeliveryOrderCommandHandler` หลัง `:74`, BulkSubmit เป็น `BulkSubmitFailure`) · P2 GET สถานะ · P3 `shipment.failed.v1` · contract doc ของ `POST /api/v1/source/delivery-orders` · cache template lookup ถ้าทำ P1 แล้ววัดได้ว่าช้า

## When this plan is picked up
**First move**: เปิด `ITripRepository.cs` เพิ่ม `TripAttemptRef` + `GetAttemptRefsByDeliveryOrderIdAsync`
Cold start → done ≈ 1.5 วันทำงาน
