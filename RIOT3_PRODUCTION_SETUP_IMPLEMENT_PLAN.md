# แผน Implement: RIOT3 Production Setup

> อ้างอิงจาก `PRODUCTION_READINESS.md` อัปเดตล่าสุดวันที่ 2026-04-30  
> สถานะปัจจุบัน: Phase 3 เสร็จแล้ว, ทดสอบ RIOT3 auth/connectivity ผ่านแล้ว, แต่ production setup ยังไม่ครบ  
> เป้าหมาย: ทำให้ app dispatch งานไป RIOT3 กับ robot จริงได้ครบ end-to-end พร้อม callback และหลักฐานสำหรับ Phase 4/5

## สรุปภาพรวม

การเชื่อมต่อ RIOT3 โดยตรงผ่านแล้วจาก real integration tests จำนวน 11 รายการ สิ่งที่ยังเป็น production blocker คือความถูกต้องของการใช้งานกับ robot จริง:

1. Vehicle ใน app ต้อง map กับ RIOT3 robot `deviceKey`
2. RIOT3 ต้อง POST callback กลับมาที่ app webhook ได้
3. ต้อง dispatch งานจริงแล้ว robot เคลื่อนที่ และ trip ถูก complete ผ่าน webhook ได้

งานแรกที่ควรเริ่มคือเพิ่ม mapping `deviceKey` ของ RIOT3 ใน Fleet และใช้ mapping นี้ตอนส่ง RIOT3 order การใช้ `Vehicle.Id.ToString()` เป็น `appointVehicleKey` ยังไม่ปลอดภัยสำหรับ production เพราะ RIOT3 ระบุ robot ด้วย `deviceKey`

## Workstreams

| ID | งาน | Owner | Estimate | Output |
|---|---|---:|---:|---|
| R3-1 | Vehicle `deviceKey` mapping | Backend | 0.5-1 วัน | Fleet เก็บ RIOT3 robot key และ dispatch ส่ง `appointVehicleKey` ถูกต้อง |
| R3-2 | เก็บ robot inventory | Ops + Backend | 0.5 วัน | รายการ robot ที่ approve แล้วพร้อม app vehicle records |
| R3-3 | ตั้งค่า webhook callback | Ops | 0.5 วัน | RIOT3 admin ชี้ callback ไปยัง app URL ที่เข้าถึงได้ |
| R3-4 | ตรวจ network path | Ops | 0.5 วัน | มีหลักฐาน RIOT3 -> App POST และ App -> RIOT3 calls |
| R3-5 | Real E2E dry run | Backend + Ops | 0.5-1 วัน | Dispatch -> robot movement -> webhook -> trip completed evidence |
| R3-6 | อัปเดต production evidence | Backend | 0.5 วัน | อัปเดต `PRODUCTION_READINESS.md` พร้อม owner/date/evidence |

## R3-1: เพิ่ม RIOT3 Device Key Mapping แบบชัดเจน

### ปัญหา

โค้ดปัจจุบันส่งค่า:

```csharp
AppointVehicleKey = vehicleId.ToString()
```

แต่ RIOT3 ต้องการ `deviceKey` ของ robot จริง ถ้าค่านี้ไม่ตรงกัน RIOT3 อาจตอบ `E320003` หรือรับ request แต่ไม่ส่งงานไปยัง robot ที่ต้องการ

### Design ที่เสนอ

เพิ่ม vendor identity field ใน Fleet vehicle records:

```csharp
public string? VendorVehicleKey { get; private set; }
```

ใช้ field นี้สำหรับ RIOT3 calls:

- `AdapterKey = "riot3"`
- `VendorVehicleKey = RIOT3 deviceKey`
- fallback ไปใช้ `Vehicle.Id.ToString()` เฉพาะ test/dev simulator path เท่านั้น ไม่ใช้กับ production RIOT3 dispatch

### Code Changes

| Area | Files | Change |
|---|---|---|
| Fleet domain | `src/Modules/Fleet/.../Entities/Vehicle.cs` | เพิ่ม `VendorVehicleKey`; validate เมื่อ adapter เป็น `riot3` |
| Register command | `RegisterVehicleCommand.cs`, handler | รับ optional `VendorVehicleKey` |
| Fleet DB | `FleetDbContext.cs`, migration | เพิ่ม column `VendorVehicleKey`, max length 100, index บน `(TenantId, AdapterKey, VendorVehicleKey)` |
| Fleet queries | `VehicleDto.cs`, query handler | return vendor key เพื่อให้ ops ตรวจสอบได้ |
| Vendor adapter dispatch | `Riot3CommandService.cs` และ call path | resolve vendor key ก่อน build `Riot3OrderRequest.AppointVehicleKey` |
| Tests | integration + RIOT3 real tests | ทดสอบว่า RIOT3 order payload ใช้ `VendorVehicleKey` ไม่ใช่ app `VehicleId` |

### Implementation Notes

ตอนนี้ `IVehicleCommandService.SendTaskAsync(Guid vehicleId, RobotTaskCommand command)` ได้แค่ app vehicle ID มีทางเลือกที่ปลอดภัย 2 แบบ:

1. เพิ่ม `VendorVehicleKey` ใน `RobotTaskCommand` และ populate ก่อนเรียก adapter
2. เพิ่ม Fleet lookup ใน vendor dispatch path เพื่อ resolve app vehicle ID เป็น vendor key

แนะนำเลือก option 1 ถ้า dispatch pipeline มี vehicle context อยู่แล้ว เลือก option 2 เฉพาะกรณี command construction อยู่ไกลจาก Fleet data มากเกินไป

### Acceptance Criteria

- Register RIOT3 vehicle พร้อม `VendorVehicleKey` ได้
- Dispatch ไป RIOT3 ส่ง `appointVehicleKey = VendorVehicleKey`
- Production startup หรือ dispatch reject RIOT3 vehicle ที่ไม่มี `VendorVehicleKey`
- Tests พิสูจน์ว่า app `VehicleId` กับ RIOT3 `deviceKey` เป็นคนละค่าได้

## R3-2: เก็บและ Register Robot Inventory

### ขั้นตอน

1. ดึง robot list จาก RIOT3:

```bash
curl -H "Authorization: app <token>" http://10.204.212.28:12000/api/v4/robots
```

2. สร้าง inventory table:

| RIOT3 deviceKey | Robot name | App VehicleName | VehicleTypeId | AdapterKey | Tenant |
|---|---|---|---|---|---|
| TBD | TBD | TBD | TBD | riot3 | TBD |

3. Register vehicle แต่ละตัวใน app:

```http
POST /api/fleet/vehicles
{
  "vehicleName": "AMR-01",
  "vehicleTypeId": "<vehicle-type-id>",
  "adapterKey": "riot3",
  "vendorVehicleKey": "<riot3-deviceKey>"
}
```

### Acceptance Criteria

- RIOT3 robot ทุกตัวใน production มี app vehicle record อย่างละ 1 record
- ไม่มี mapping ซ้ำใน `(TenantId, AdapterKey, VendorVehicleKey)`
- `/api/fleet/vehicles/available` แสดง vehicles ตามที่คาดหลัง login ด้วย tenant ที่ถูกต้อง

## R3-3: ตั้งค่า RIOT3 Webhook Callback

### Target Callback

```text
http://<app-host>:<port>/api/webhooks/riot3/notify
```

สำหรับ production ให้ใช้ service DNS/load balancer name ที่ RIOT3 server เข้าถึงได้ ห้ามใช้ `localhost`

### ขั้นตอน

1. Login เข้า RIOT3 admin ที่ `http://10.204.212.28:12000`
2. ไปที่ Settings -> Notification/Webhook
3. เพิ่ม callback URL ของ app environment
4. บันทึก screenshot หรือ export settings ไว้เป็นหลักฐาน

### Acceptance Criteria

- RIOT3 admin มี callback URL ที่ถูกต้อง 1 รายการต่อ app environment
- Callback ใช้ `https` ถ้า production network รองรับ TLS termination
- Callback เข้าถึงได้จาก network ของ RIOT3 server

## R3-4: Validate Network Path

### App -> RIOT3

ทดสอบผ่าน `Category=Riot3Real` แล้ว แต่ควรรันซ้ำหลังตั้งค่า production config:

```bash
dotnet test --filter "Category=Riot3Real"
```

### RIOT3 -> App

จาก network ฝั่ง RIOT3 ให้ POST sample notify payload:

```bash
curl -X POST http://<app-host>:<port>/api/webhooks/riot3/notify \
  -H "Content-Type: application/json" \
  -d '{"type":"task","taskEventType":"finished","upperKey":"network-smoke-test"}'
```

Expected response: `200 OK`

### Acceptance Criteria

- App เรียก `10.204.212.28:12000` ได้
- RIOT3 network เรียก app webhook ได้
- App logs แสดงว่าได้รับ RIOT3 notify payload
- ไม่มี firewall, DNS, ingress หรือ TLS error ค้างอยู่

## R3-5: Real End-To-End Dry Run

### Scenario

1. สร้างหรือเลือก tenant
2. Seed facility map/stations ให้ตรงกับ RIOT3 station IDs
3. Register vehicle ด้วย `AdapterKey = "riot3"` และ `VendorVehicleKey` จริง
4. Submit delivery order
5. Plan และ dispatch trip
6. ยืนยันว่า RIOT3 รับ order แล้ว
7. ยืนยันว่า robot เคลื่อนที่ หรือ RIOT3 report business rejection ที่คาดไว้
8. ยืนยันว่า webhook update trip state
9. ยืนยันว่า outbox และ timeline records ถูกเขียน

### Evidence ที่ต้องเก็บ

| Evidence | Source |
|---|---|
| Request payload ที่มี `appointVehicleKey` | App structured logs |
| RIOT3 order accepted | RIOT3 API/admin |
| Robot movement หรือ business-rule rejection | RIOT3 admin/operator observation |
| Webhook received | App logs |
| Trip final state | Dispatch API/database |
| Outbox processed | Outbox database/logs |

### Acceptance Criteria

- Happy path: `dispatch trip -> robot moves -> RIOT3 finished webhook -> trip Completed`
- Failure path: `RIOT3 failed webhook -> trip failure event` ต้องมองเห็นได้ และมี retry/ops action ชัดเจน
- แนบ evidence เข้า release gate notes

## R3-6: อัปเดต Readiness และ Release Gate

อัปเดต `PRODUCTION_READINESS.md` หลัง dry run:

- RIOT3 Production Setup checklist: mark completed items
- เพิ่ม device key mapping decision และ implementation evidence
- เพิ่ม webhook URL setup evidence
- เพิ่ม real E2E dry run result พร้อม date, owner, environment, robot ID
- ย้าย vendor contract gaps ที่ยังเหลือไป Phase 5

## Dependencies

| Dependency | Needed For | Status |
|---|---|---|
| RIOT3 API token | Robot list, dispatch, cancel | มีอยู่ใน real tests |
| RIOT3 admin access | Webhook callback setup | Required |
| Routable app host | RIOT3 -> App webhook | Required |
| Real robot `deviceKey` list | Vehicle mapping | Required |
| Correct map/station vendor refs | Robot movement | Required |

## Risks And Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `VehicleId` ไม่ตรงกับ `deviceKey` | Dispatch ไม่เจอ robot | เพิ่ม explicit `VendorVehicleKey` |
| RIOT3 เรียก app webhook ไม่ได้ | Trips ไม่ complete | ทำ network smoke test จาก RIOT3 subnet |
| Vendor payload shape ใน production ไม่ตรง | Runtime API failures | คง `Riot3RealIntegrationTests`; verify route/action specs ใน Phase 5 |
| Map/station ID ไม่ align | Order accepted แต่ run ไม่ได้ | Validate facility station vendor refs ก่อน dry run |
| Production secrets หลุดใน config | Security gate fail | ใช้ env/Vault/KMS เท่านั้น |

## ลำดับการทำงานที่แนะนำ

1. Implement และ test `VendorVehicleKey`
2. Fetch RIOT3 robot inventory
3. Register app vehicles ด้วย device keys จริง
4. Configure RIOT3 webhook callback
5. Run network smoke tests ทั้งสองทิศทาง
6. Run controlled real E2E dispatch 1 รอบ
7. Update readiness evidence
8. ไปต่อ Phase 4 k6 load/stress testing

## Definition Of Done

- `appointVehicleKey` ใช้ RIOT3 `deviceKey` จริง
- RIOT3 vehicles ทุกตัว register ด้วย `AdapterKey = "riot3"` และ vendor key ไม่ว่าง
- RIOT3 webhook callback ถูกตั้งค่าไปยัง app URL ที่เข้าถึงได้
- Bidirectional network checks ผ่าน
- Real robot dry run 1 รอบ complete หรือมี documented vendor business rejection
- `PRODUCTION_READINESS.md` มี owner/date/evidence สำหรับ RIOT3 Production Setup
