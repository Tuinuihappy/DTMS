# รายงาน Manual operators — แผนการพัฒนา

> **สถานะ**: 🔵 ยังไม่เริ่ม — ร่างเมื่อ 2026-10-01
> **สิ่งที่ต้องการ**: ทีม ops เปิด Reports → "Manual operators" แล้วเห็นแยกรายคนว่าแต่ละคนรับทริป manual ไปกี่ทริป และทริปเหล่านั้นจบอย่างไร โดยใช้คอลัมน์ชุดเดียวกับรายงาน Vehicle performance
> **ประเภท / ขนาด**: feature · S, ประมาณ 3.5 ชม. (เฟสเดียว)
> **แผนแม่**: ไม่มี ต่อจากงานแก้ Vehicle performance เมื่อ 2026-10-01 (คอลัมน์ In progress, กลุ่ม (manual), Median, ชื่อหุ่น) ซึ่ง**ยังไม่ได้ commit**
> **สมมติฐาน**: "operator" หมายถึง `Trips.AcknowledgedBy` คือ username ใน OMS ของคนแรกที่กดรับงาน ซึ่งอาจไม่ใช่คนที่ขนของจริงเสมอไป และมี 2 ชื่อที่เป็น account ใช้ร่วมกัน คือ `all new` (153 ทริป) กับ `stf` (120 ทริป) หน้ารายงานจะเขียนอธิบายเรื่องนี้ไว้ในคำอธิบายใต้หัวข้อ
> **หมายเหตุ**: plan mode ให้แก้ได้แค่ไฟล์นี้ไฟล์เดียว พอเริ่ม Phase 1 ให้ copy ไปไว้ที่ `docs/plans/manual-operators-report.md` ซึ่งเป็นที่เก็บแผนของโปรเจกต์

## ที่มา

ตอนนี้รายงาน Vehicle performance รวมทริปที่คนทำเองไว้ในแถว `(manual)` แถวเดียว ในช่วง 7 วันล่าสุดมี 1,068 ทริป ทำให้ไม่รู้ว่าใครทำงานไปเท่าไร ข้อมูลนี้มีอยู่ใน DB แล้ว คือ `dispatch.Trips.AcknowledgedBy` ซึ่งมีค่าอยู่ใน 2,523 จาก 2,526 ทริป manual รวม 40 username และไม่มีชื่อไหนซ้ำกันแบบต่างกันแค่ตัวพิมพ์เล็ก-ใหญ่ จึงจัดกลุ่มตามค่าที่เก็บไว้ได้เลย ที่เลือกทำเป็นแท็บแยก ไม่แตกแถวใน Vehicle performance เพราะถ้าเอา 40 คนไปปน หุ่นยนต์ 5 ตัวจะหลุดจากกราฟ top 10

## เป้าหมาย / สิ่งที่ไม่ทำ

**เป้าหมาย**:
- มีแท็บใหม่ "Manual operators" ที่แสดง 1 แถวต่อ `AcknowledgedBy`
- คอลัมน์: Trips / Completed / Failed / Rejected / Cancelled / In progress / Success / Median / P95 / SLA breach
- มีกราฟแท่งซ้อนแสดง top 10 คน
- แต่ละแถวรวมกันได้พอดี (Completed + Failed + Rejected + Cancelled + In progress = Trips)
- ผลรวม Trips ทุกแถวเท่ากับแถว `(manual)` ในรายงาน Vehicle performance ในช่วงเวลาเดียวกัน

**สิ่งที่ไม่ทำ**:
- ไม่เพิ่มตัววัดเวลาแบบใหม่ Median และ P95 ใช้ `TimeToCompleteSec` ตัวเดียวกับรายงานหุ่น (เป็น STORED generated column, [DispatchDbContext.cs:303-323](src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/Data/DispatchDbContext.cs#L303-L323)) เพื่อให้สองรายงานเทียบกันได้ ถ้าจะวัดเวลา "ตั้งแต่กดรับ → เสร็จ" ต้องเพิ่มคอลัมน์ใหม่ ควรทำเป็นแผนแยก
- ไม่แปลง username เป็นชื่อจริง และไม่แยก account ที่ใช้ร่วมกัน เพราะ DTMS ไม่มีข้อมูลว่า `all new` คือใคร
- ไม่แก้รายงาน Vehicle performance ยังคงมีแถว `(manual)` แถวเดียวเหมือนเดิม
- ไม่มีปุ่ม CSV ในแท็บนี้ เพราะ CSV `trips-export` ที่มีอยู่แล้วให้ข้อมูลดิบครบ

---

## เตรียมตัวก่อนเริ่ม (~10 นาที)

ไฟล์ที่ต้องอ่าน:

- [GetVehiclePerformanceReportQuery.cs](src/Modules/Dispatch/DTMS.Dispatch.Application/Queries/GetVehiclePerformanceReport/GetVehiclePerformanceReportQuery.cs): **ต้นแบบที่จะ copy** มีครบทั้งการจัดกลุ่ม การนับสถานะจบ, `InProgress = total - terminal`, Median/P95 ผ่าน `Percentile()` และการตรวจช่วงเวลาไม่เกิน 90 วัน
- [ITripFactsReadRepository.cs](src/Modules/Dispatch/DTMS.Dispatch.Application/Projections/ITripFactsReadRepository.cs) และ [TripFactsReadRepository.cs](src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/Projections/TripFactsReadRepository.cs): ตอนนี้ `TripFactsEntry` ได้ `IsManual` และ `VendorVehicleName` มาจาก correlated subquery บน `DispatchDbContext` ตัวเดียวกันอยู่แล้ว จะเพิ่ม `AcknowledgedBy` ด้วยวิธีเดียวกัน
- [DispatchReportsEndpoints.cs:30-36](src/Modules/Dispatch/DTMS.Dispatch.Presentation/DispatchReportsEndpoints.cs#L30-L36): รูปแบบ endpoint ที่จะ copy (`ResolveWindow`, `Permissions.Reporting.ReportRead`)
- [vehicle-performance-report.tsx](frontend/components/reports/vehicle-performance-report.tsx): component ต้นแบบ
- [reports-experience.tsx](frontend/components/reports/reports-experience.tsx): array `TABS` และ union `TabKey`
- [app/api/reports/vehicle-performance/route.ts](frontend/app/api/reports/vehicle-performance/route.ts): Next proxy route ต้นแบบ (`proxyToBackend`)

ทางเลือกที่ตัดสินไว้แล้ว:

| เรื่อง | ค่าที่เลือก | เปลี่ยนไปใช้อีกทางเมื่อ |
|---|---|---|
| เอา `AcknowledgedBy` มาจากไหน | เพิ่ม `string? AcknowledgedBy` ใน `TripFactsEntry` แล้วอ่านผ่าน correlated subquery บน `Trips` (วิธีเดียวกับ `VendorVehicleName`) | ถ้า subquery ช้าจนเห็นได้ ค่อยเพิ่มคอลัมน์ใน `bi.TripFacts` ซึ่งต้องทำ migration และ backfill ด้วย ยังไม่คุ้มที่ข้อมูลประมาณ 6 พันแถว |
| Endpoint | สร้างใหม่ `GET /api/v1/reports/manual-operators` | ทางเลือกอื่นคือเพิ่ม query param ใน vehicle-performance แต่โครงสร้างแถวไม่เหมือนกัน (ไม่มี deviceKey/ชื่อหุ่น) |
| ทริปที่ไม่มี `AcknowledgedBy` | รวมไว้ในกลุ่ม `(not acknowledged)` (มี 3 ทริปตลอดทั้งหมด) | ตัดทิ้งก็ต่อเมื่อ ops รู้สึกว่าแถวนี้รก |
| ทริปไหนนับเป็น "manual" | `IsManual == true` (ไม่มีแถวใน `AmrTripExtension`) นิยามเดียวกับกลุ่ม `(manual)` | — |

คำถามที่ค้างอยู่: ไม่มีข้อไหนที่ทำให้เริ่มงานไม่ได้

**ฐานในการประเมินเวลา**: commit `94103e39` (แท็บรายงาน Job failures) แก้ 23 ไฟล์ เพิ่ม 1,207 บรรทัด แต่ส่วนใหญ่เป็น migration ไฟล์ Designer และการแก้ projector ส่วนที่เป็นตัวรายงานจริง (query 96 + endpoint 39 + route 11 + component 189 + api 29 + tabs 16 ≈ 380 บรรทัด) มีรูปแบบเดียวกับแผนนี้ แต่แผนนี้ไม่มี migration

---

## Phase 1 — แท็บ Manual operators (~3.5 ชม.)

### 1.1 Repository: ส่ง AcknowledgedBy ออกมา (~20 นาที)

- แก้ `ITripFactsReadRepository.cs`: เพิ่ม `string? AcknowledgedBy = null` ต่อท้าย `TripFactsEntry` หลัง `VendorVehicleName`
- แก้ `TripFactsReadRepository.cs` ใน `Select`: เพิ่มต่อท้าย
  ```csharp
  _db.Trips.Where(t => t.Id == r.TripId).Select(t => t.AcknowledgedBy).FirstOrDefault()
  ```
  (`DbSet<Trip> Trips` อยู่ที่ [DispatchDbContext.cs:18](src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/Data/DispatchDbContext.cs#L18))
- record แบบ positional นี้ถูกสร้างที่ repository ที่เดียว การเพิ่ม parameter ที่มีค่า default จึงไม่ทำให้ที่อื่นพัง

### 1.2 Query + handler (~1 ชม.)

- ไฟล์ใหม่: `src/Modules/Dispatch/DTMS.Dispatch.Application/Queries/GetManualOperatorsReport/GetManualOperatorsReportQuery.cs`
  ```csharp
  public record GetManualOperatorsReportQuery(DateTime FromUtc, DateTime ToUtc) : IQuery<ManualOperatorsResponse>;
  public record ManualOperatorRow(string Operator, int TotalTrips, int Completed, int Failed,
      int Rejected, int Cancelled, int InProgress, double SuccessRate,
      double? MedianTimeToCompleteSec, double? P95TimeToCompleteSec, int SlaBreached);
  public record ManualOperatorsResponse(DateTime FromUtc, DateTime ToUtc, int TotalTrips,
      IReadOnlyList<ManualOperatorRow> Rows);
  ```
- handler copy มาจาก handler ของรายงานหุ่น แล้วเปลี่ยนเป็น:
  - กรอง `trips.Where(t => t.IsManual)`
  - จัดกลุ่ม `GroupBy(t => string.IsNullOrWhiteSpace(t.AcknowledgedBy) ? "(not acknowledged)" : t.AcknowledgedBy.Trim())`
  - ตัวนับ, `Percentile` และการเรียงลำดับใช้ของเดิม
  - `TotalTrips` นับเฉพาะทริป manual
- ข้อควรระวัง: `Percentile` เป็น `private static` อยู่ใน handler ของรายงานหุ่น ให้ copy มาใช้ ไม่ต้องแยกออกเป็นฟังก์ชันกลาง จะได้ไม่ต้องแตะโค้ดที่ขึ้นระบบไปแล้ว ฟังก์ชัน 4 บรรทัดมีซ้ำสองที่ได้
- Test: ไฟล์ใหม่ `tests/Modules/Dispatch.UnitTests/GetManualOperatorsReportQueryTests.cs` ใช้ xunit + NSubstitute + FluentAssertions (ชุดเดียวกับ `TripFactsProjectorTests.cs`) และ substitute `ITripFactsReadRepository` กรณีที่ต้อง test:
  1. ทริป AMR (`IsManual=false`) ไม่ถูกนับ
  2. operator 2 คนได้ 2 แถว และแต่ละแถวรวมกันได้พอดี (C + F + R + X + InProgress = Total)
  3. `AcknowledgedBy` ที่เป็น null หรือว่างไปอยู่ในกลุ่ม `(not acknowledged)`
  4. median ของ [60, 120, 600] คือ 120
  5. ช่วงเวลาเกิน 90 วันต้องได้ Failure

### 1.3 Endpoint (~15 นาที)

- แก้ `DispatchReportsEndpoints.cs`: เพิ่มข้างๆ `/vehicle-performance`
  ```csharp
  group.MapGet("/manual-operators", async (DateTime? fromUtc, DateTime? toUtc, ISender sender) => { … })
       .RequirePermission(Permissions.Reporting.ReportRead);
  ```
  ใช้ `ResolveWindow(…, defaultDays: 7)` เหมือนเดิม ไม่ต้องสร้าง permission code ใหม่

### 1.4 Frontend (~1.5 ชม.)

- ไฟล์ใหม่ `frontend/app/api/reports/manual-operators/route.ts`: copy จาก route ของ vehicle-performance แล้วเปลี่ยน path เป็น `/api/v1/reports/manual-operators` ไม่ต้องแก้ `proxy.ts` เพราะ matcher ครอบ `/reports/:path*` สำหรับหน้าเว็บอยู่แล้ว ส่วน `/api/reports/*` เป็น Next route handler
- แก้ `frontend/lib/api/reports.ts`: เพิ่ม `ManualOperatorRow`, `ManualOperatorsResponse` และ `getManualOperatorsReport(w, signal)` โดย copy จาก `getVehiclePerformanceReport`
- ไฟล์ใหม่ `frontend/components/reports/manual-operators-report.tsx`: copy จาก `vehicle-performance-report.tsx` แล้วเปลี่ยนเป็น:
  - ตัดส่วน deviceKey, ชื่อหุ่น และ BUCKET_KEYS ออก
  - คอลัมน์แรกชื่อ `Operator`
  - ช่องสรุปด้านบน: Total manual trips / Operators / Most trips (operator แถวแรก + จำนวนทริป)
  - คำอธิบายใต้หัวข้อ: "Grouped by the OMS user who accepted the trip (first acknowledge). Shared accounts such as `all new` / `stf` appear as one operator."
  - กราฟ: top 10 แท่งซ้อนแบบเดิม
- แก้ `reports-experience.tsx`:
  - เพิ่ม `"operators"` ใน `TabKey`
  - เพิ่มรายการใน TABS ต่อจาก `vehicles`: label "Manual operators", ไอคอน `Users` จาก lucide, hint "Manual trips per accepting OMS user from TripFacts"
  - เพิ่มบรรทัด render ของแท็บใหม่

### 1.5 ตรวจสอบ + deploy (~30 นาที)

- `dotnet test tests/Modules/Dispatch.UnitTests -m:1`
- `cd frontend && npx tsc --noEmit -p .` (ไม่มี eslint ต้องตรวจ dep array ของ `useCallback` เองว่าเป็น `[window]`)
- `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api` แล้วตามด้วย `-Only frontend` ทีละตัว
- เทียบตัวเลขในหน้ากับ SQL นี้ (อ่านอย่างเดียว) ในช่วง 7 วันเดียวกัน:
  ```sql
  SELECT coalesce(t."AcknowledgedBy",'(not acknowledged)'), count(*)
  FROM bi."TripFacts" f JOIN dispatch."Trips" t ON t."Id"=f."TripId"
  WHERE NOT EXISTS (SELECT 1 FROM dispatch."AmrTripExtensions" a WHERE a."TripId"=f."TripId")
    AND f."CreatedAt" >= now() - interval '7 days'
  GROUP BY 1 ORDER BY 2 DESC;
  ```

### เกณฑ์ผ่านของ Phase 1

- unit test ผ่านทั้งหมด
- แท็บ Manual operators แสดงผลได้ ผลรวมคอลัมน์ Trips เท่ากับ Trips ของแถว `(manual)` ในหน้า Vehicle performance และจำนวนของแต่ละคนตรงกับ SQL ข้างบน
- `podman logs --since 10m dtms-api | grep -i exception` ไม่มี error ใหม่

**Commit**: `feat(reports): see manual trips per accepting operator in a new Manual operators tab`

ให้ commit งาน Vehicle performance ที่ค้างอยู่**ก่อน แยกเป็นอีก commit** (`feat(reports): show robot names, an in-progress column and median time on vehicle performance`) เพื่อให้สองงานนี้แยกจากกันชัดเจน

---

## ผลกระทบต่อ workflow ของระบบ

**ไม่กระทบการทำงานหลัก** งานนี้อ่านข้อมูลอย่างเดียว
- ไม่มีการเขียน DB ไม่มี migration ไม่มี event/outbox และไม่แก้ projector ทำให้การรับ order, การสร้าง trip, การสั่ง RIOT3, callback ไป OMS และ reconciler ทำงานเหมือนเดิมทั้งหมด
- ไม่แก้ entity `Trip` จึงไม่เกี่ยวกับ xmin concurrency

**จุดเดียวที่ใช้โค้ดร่วมกัน** คือ `TripFactsReadRepository.QueryAsync` ซึ่งมีผู้ใช้ 2 ที่ (ตรวจด้วย grep แล้ว):
1. รายงาน Vehicle performance ([GetVehiclePerformanceReportQuery.cs:56](src/Modules/Dispatch/DTMS.Dispatch.Application/Queries/GetVehiclePerformanceReport/GetVehiclePerformanceReportQuery.cs#L56)): ได้ฟิลด์ใหม่มาแต่ไม่ได้ใช้ ผลลัพธ์ไม่เปลี่ยน
2. CSV `trips-export` ([DispatchReportsEndpoints.cs:40-53](src/Modules/Dispatch/DTMS.Dispatch.Presentation/DispatchReportsEndpoints.cs#L40-L53)): คอลัมน์ถูกเขียนระบุไว้ทีละตัวใน `WriteCsvAsync` ไฟล์ CSV จึงหน้าตาเหมือนเดิมทุกคอลัมน์

ผลข้างเคียงอย่างเดียวคือ query ของสองที่นี้มี subquery เพิ่มอีกหนึ่งตัว (ค้นด้วย PK บน `Trips`) อาจช้าลงเล็กน้อยระดับมิลลิวินาทีที่ข้อมูลประมาณ 6 พันแถว ระหว่าง rebuild ระบบจะหยุดสั้นๆ ไม่กี่วินาทีต่อ container เหมือนทุกครั้ง

## การตรวจสอบ

| ตรวจอะไร | วิธี | ที่ไหน |
|---|---|---|
| logic ของ handler | `dotnet test tests/Modules/Dispatch.UnitTests -m:1` | Dispatch.UnitTests |
| Type | `cd frontend && npx tsc --noEmit -p .` | frontend |
| ตัวเลขจริง | รัน SQL ข้างบนผ่าน `podman exec -i dtms-postgres psql -U postgres -d amr_delivery_planning` แล้วเทียบกับแท็บ | dev DB (อ่านอย่างเดียว) |
| ยอดรวมตรงกัน | Trips ของ `(manual)` ใน Vehicle performance เท่ากับผลรวมคอลัมน์ Trips ใน Manual operators | หน้าเว็บ |

## ถือว่าเสร็จเมื่อ

- [ ] แท็บขึ้นระบบหลัง rebuild ทั้งสองตัว และตัวเลขตรงกับ SQL ในช่วง 7 วัน
- [ ] handler test ผ่านทั้ง 5 กรณี
- [ ] มี 2 commit บน `main` (งาน vehicle ที่ค้างอยู่ แล้วตามด้วยงานนี้)
- [ ] ใช้เวลาไม่เกินประมาณ 5 ชม.

## ความเสี่ยงและการย้อนกลับ

| ความเสี่ยง | โอกาส | วิธีรับมือ / ย้อนกลับ |
|---|---|---|
| subquery ที่เพิ่มเข้ามาทำให้ `trips-export` หรือ vehicle-performance ช้าลง | ต่ำ (~6 พันแถว, ค้นด้วย PK) | ถ้ารายงานช้าจนรู้สึกได้ ให้รวม subquery เป็น LEFT JOIN เดียว ถ้าต้องย้อนกลับก็ revert commit |
| ops เข้าใจว่า `AcknowledgedBy` คือ "คนที่ขนของจริง" | กลาง | คำอธิบายใต้หัวข้อบอกความหมายไว้ และระบุชื่อ account ที่ใช้ร่วมกัน |

## สิ่งที่จะทำให้แผนนี้ใช้ไม่ได้

- ถ้า OMS เริ่มส่งข้อมูลคนที่ขนของจริง (เช่น driver id) มาใน event อื่น ควรจัดกลุ่มตามค่านั้นแทน `AcknowledgedBy`

## เมื่อเริ่มทำตามแผนนี้

**ขั้นแรก**: เปิด `src/Modules/Dispatch/DTMS.Dispatch.Application/Projections/ITripFactsReadRepository.cs` แล้วเพิ่ม `string? AcknowledgedBy = null` ต่อท้าย `TripFactsEntry` ตั้งแต่เริ่มจนขึ้นระบบใช้เวลาประมาณ 3.5–4 ชม. รวม rebuild สองรอบ รอบละประมาณ 3–5 นาที
