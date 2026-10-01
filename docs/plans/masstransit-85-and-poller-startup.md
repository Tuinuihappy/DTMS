# อัปเกรด MassTransit 8.5 + ให้ poller รอ ApplicationStarted — แผนการพัฒนา

> **สถานะ**: 🟡 Phase 1 ✅ เสร็จ 2026-10-01 · Phase 2 ยังไม่เริ่ม
>
> **ผล Phase 1**:
> - build พังแค่จุดเดียวตามคาด (health check `CreateConnection`) ส่วน `IBusObserver` และ saga ไม่ต้องแก้
> - test ผ่านทั้ง 1,311 ตัว
> - **ปิด SetMinThreads แล้ว bus เปิดได้ใน ≤ 1 วินาที** ทั้งตอน deploy ใหม่และ restart 2 รอบ (เดิม 20–28 วินาที) ยืนยันว่า RabbitMQ.Client 6 คือคนที่ block thread
> - smoke test ช่วง 06:26–06:35 UTC: outbox ส่ง event ออกไป 29 ตัว, projection รับไป 329 ตัว, ไม่มี message ค้าง, ไม่มี `R-FAULT`, error queue ว่าง และ `/health/ready` ได้ 200 ทุกครั้ง
> **สิ่งที่ต้องการ**: ตอน API เปิดเครื่อง ต้องไม่มีอะไร block worker thread ระหว่างรอ RabbitMQ และ poller ทุกตัวต้องไม่เริ่มงานแรกก่อนที่ host จะพร้อม
> **ประเภท / ขนาด**: refactor (อัปเกรด dependency) + ops · M, ~7–9 ชม. (2 เฟส)
> **แผนแม่**: ต่อจาก `6f5f0a23 fix(api): stop startup starving the thread pool…` (ใส่ SetMinThreads 64 และ StartTimeout 60s) ดูบันทึกการวัดใน memory `project_api_startup_threadpool_starvation`
> **สมมติฐาน**: คำขอเขียนว่า "MassTransit 8.3" แต่**แผนนี้ใช้ 8.5.11** เพราะเช็กจาก NuGet แล้วพบว่า **8.3.0 ยังใช้ RabbitMQ.Client `>= 6.8.1`** (sync เหมือนเดิม) จึงไม่แก้ปัญหา RabbitMQ.Client 7 (async) เริ่มใช้ที่ **8.4.0** (`>= 7.1.2`, 2025-03-18) ส่วน **8.5.11** (2026-09-30) ใช้ `>= 7.2.2` มี build สำหรับ net10.0 และยังเป็น **Apache-2.0** ห้ามขึ้น 9.x เพราะเป็น license เชิงพาณิชย์

## สรุปการเปลี่ยนแปลงในประโยคเดียว

API คุยกับ RabbitMQ แบบ async ทั้งหมด จึงไม่ต้องพึ่ง thread ขั้นต่ำ 64 ตัวอีกเพื่อให้เปิดเครื่องได้เร็ว และ poller ทั้ง 8 ตัวเริ่มงานแรกหลังสัญญาณ `ApplicationStarted` ไม่ใช่หลังเวลาหน่วงตายตัว

## เป้าหมาย / สิ่งที่ไม่ทำ

**เป้าหมาย** (วัดเวลาจาก `Riot3ReconciliationService started` ถึง `Bus started` ใน log ของ `dtms-api`):
- หลัง Phase 1 **ถ้าปิด** SetMinThreads ชั่วคราว ช่วงนี้ต้อง ≤ 3 วินาที (ค่าเดิมคือ 20–28 วินาที) เพื่อพิสูจน์ว่าต้นเหตุหายจริง
- หลัง Phase 2 ไม่มี poller ตัวไหนเขียน log ก่อนบรรทัด `Application started`

**สิ่งที่ไม่ทำ**:
- ไม่ขึ้น MassTransit 9.x (license เชิงพาณิชย์)
- ไม่แก้ sync-over-async ใน `SystemJwtValidator.cs:122,163,236` ซึ่งเป็นแบบ per-request ไม่ใช่ตอนเปิดเครื่อง ย้ายไปอยู่ใน "ไว้ทีหลัง"
- ไม่ลบ `SetMinThreads` ทิ้งโดยอัตโนมัติ จะตัดสินใจหลังวัดผลใน 1.4
- ไม่แตะ saga (`Workflow:UseSaga`) นอกเหนือจากให้ compile และ test ผ่าน

---

## เตรียมตัวก่อนเริ่ม (~20 นาที)

ไฟล์ที่ต้องอ่าน:
- [ModuleServiceRegistration.cs:656-796](../../src/DTMS.Api/Modules/ModuleServiceRegistration.cs): config ของ bus ทั้งหมด (มีที่เดียว) ได้แก่ `MassTransitHostOptions` (663-671), `AddBusObserver<BusShutdownTimingObserver>` (702), `AddConsumers` 6 assembly (706-720), saga (727-739) และ `UsingRabbitMq` (741-796): retry, delayed redelivery, `UseInMemoryOutbox`, `UseKillSwitch`, `PrefetchCount=16`, `ConfigureEndpoints`
- [Program.cs:679-697](../../src/DTMS.Api/Program.cs): health check `"rabbitmq"` ใช้ `new RabbitMQ.Client.ConnectionFactory` + `factory.CreateConnection()` แบบ sync **จุดนี้จะ compile ไม่ผ่านบน Client 7** และยังเป็นตัว block thread ทุกครั้งที่มีการเรียก `/health/ready`
- [Program.cs:43-51](../../src/DTMS.Api/Program.cs): `ThreadPool.SetMinThreads(Math.Max(minWorkers, 64), minIo)`
- [BusShutdownTimingObserver.cs](../../src/DTMS.Api/Infrastructure/Diagnostics/BusShutdownTimingObserver.cs): implement `IBusObserver` ครบทุกเมธอด ถ้า 8.5 เปลี่ยน signature จะพังตอน compile
- [WmsLocationSyncService.cs:36-42](../../src/Modules/Wms/DTMS.Wms.Infrastructure/Services/WmsLocationSyncService.cs): ตัวอย่างรูปแบบหน่วงเวลาตายตัวที่จะเปลี่ยน
- [ShutdownPhaseRecorder.cs:29-39](../../src/DTMS.Api/Infrastructure/Diagnostics/ShutdownPhaseRecorder.cs): ที่เดียวในโค้ดที่ใช้ `IHostApplicationLifetime` อยู่แล้ว ใช้เป็นต้นแบบการ inject

ทางเลือกที่ตัดสินไว้แล้ว:

| เรื่อง | ค่าที่เลือก | เปลี่ยนไปใช้อีกทางเมื่อ |
|---|---|---|
| เวอร์ชันเป้าหมาย | **8.5.11** (8.x ตัวล่าสุด, Client 7.2.2, มี net10.0) | ถ้า 8.5.11 พังเยอะเกิน 2 ชม. ให้ลอง **8.4.x** (Client 7 เหมือนกัน แต่เปลี่ยนน้อยกว่า) |
| RabbitMQ.Client | ไม่ pin เอง ปล่อยให้ MassTransit.RabbitMQ ดึงมา (ตอนนี้ก็ไม่มี `PackageVersion` ของมัน) | pin ใน `Directory.Packages.props` เมื่อ health check ต้องใช้ API ที่ไม่มีใน 7.2.2 |
| health check `"rabbitmq"` | เปลี่ยนเป็น async (`CreateConnectionAsync` + `await using`) ผ่าน `AddAsyncCheck` | ถ้า v7 API ไม่ตรงกับที่คาด ให้ลบ check ตัวนี้แล้วพึ่ง MassTransit bus health แทน (ต้องเช็ก `/health/ready` ว่ายังมีตัวบอกสถานะ broker) |
| SetMinThreads หลังอัปเกรด | **เก็บไว้** ถ้าวัดแล้วไม่เกิน 3 วินาที ให้แก้ comment ว่าเป็นประกันเพิ่ม ไม่ใช่ตัวแก้ปัญหา | ลบทิ้งได้ถ้าวัดหลายรอบแล้วไม่มีผลเลย แต่ไม่คุ้มเสี่ยง เพราะค่านี้ไม่มีต้นทุน |
| ที่อยู่ของ helper รอ started | `DTMS.SharedKernel/Hosting/HostLifetimeExtensions.cs` เพราะ Wms.Infrastructure และ Facility.Infrastructure ไม่ได้อ้าง `DTMS.Infrastructure` (เช็ก csproj แล้ว) แต่ทุกโปรเจกต์อ้าง SharedKernel อยู่ | ถ้า SharedKernel ไม่มี `IHostApplicationLifetime` ใช้ ให้เพิ่ม `<FrameworkReference Include="Microsoft.AspNetCore.App" />` แบบเดียวกับ `DTMS.Infrastructure.csproj` |
| poller ใช้ delay เดิมต่อไหม | รอ `ApplicationStarted` **แล้วค่อย** หน่วงเวลาเดิม (เพิ่มเข้าไป ไม่แทนที่) ทำให้ระยะห่างของ poller แต่ละตัวยังเหมือนเดิม | ลบ delay เดิมทิ้งก็ต่อเมื่อต้องการให้ข้อมูลแรกมาเร็วขึ้น ซึ่งไม่อยู่ในขอบเขตนี้ |

คำถามที่ค้างอยู่ (ยังไม่ได้ verify ต้องเช็กตอนลงมือ):
- [ ] signature ของ `IBusObserver` ใน 8.5.11 เหมือน 8.2.1 หรือไม่ → **กระทบ 1.2** ถ้าเปลี่ยน ให้แก้ตาม compiler บอก
- [ ] MassTransit.EntityFrameworkCore 8.5.11 รองรับ EF Core 10.0.0 (`Directory.Packages.props:31`) หรือไม่ → **กระทบ 1.1** ถ้า restore เตือน downgrade ให้ดู release notes
- [ ] `ConsumeContext<T>` / `IPublishEndpoint` มี member ใหม่ที่ทำให้ NSubstitute fake ใน 23 ไฟล์ test เปลี่ยนพฤติกรรมหรือไม่ → **กระทบ 1.3** NSubstitute ไม่พังเพราะ member ใหม่ แต่ test ที่ assert `Received()` อาจเปลี่ยน

**ฐานในการประเมินเวลา**: `03bd27b3 chore(deps): patch vulnerable packages, audit the whole graph` เป็นการอัปเกรดหลายแพ็กเกจในเวอร์ชันหลักเดิม รอบนี้ใหญ่กว่า เพราะ RabbitMQ.Client ข้ามเวอร์ชันหลัก (6 → 7) มีโค้ดที่ต้องแก้แน่นอน 1 จุด (health check) และต้องวัดผลบนระบบจริง

---

## Phase 1 — อัปเกรด MassTransit เป็น 8.5.11 (~5–6 ชม.)

### 1.1 เปลี่ยนเวอร์ชัน + restore (~30 นาที)
- แก้ `Directory.Packages.props:46-48`: `MassTransit`, `MassTransit.RabbitMQ` และ `MassTransit.EntityFrameworkCore` เป็น `8.5.11` ทั้งสามตัวพร้อมกัน (แพ็กเกจในตระกูลเดียวกันต้องเวอร์ชันตรงกัน)
- `dotnet restore DTMS.slnx` แล้วดู warning NU1605 (downgrade) และ NU1903 (vulnerability) เพราะ `NuGetAuditMode=all` (จาก 03bd27b3)

### 1.2 แก้จุดที่ compile ไม่ผ่าน (~1–2 ชม.)
- `dotnet build DTMS.slnx -m:1`
- **ที่รู้แน่ว่าพัง**: health check ที่ `Program.cs:679-697` ให้เปลี่ยนเป็น
  ```csharp
  .AddAsyncCheck("rabbitmq", async ct =>
  {
      try
      {
          var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rabbitHost, UserName = rabbitUser,
              Password = rabbitPass, RequestedConnectionTimeout = TimeSpan.FromSeconds(3) };
          await using var conn = await factory.CreateConnectionAsync(ct);
          return HealthCheckResult.Healthy();
      }
      catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message); }
  }, tags: ["ready"])
  ```
- **ที่อาจพัง**: `BusShutdownTimingObserver` (IBusObserver) และ saga EF repository (`ExistingDbContext`/`UsePostgres`, MSR:727-739) ให้แก้ตาม compiler บอก
- ไม่มีโค้ดอื่นใช้ RabbitMQ.Client ตรงๆ (ไม่มี `IModel`/`IChannel`/`BasicPublish` ใน src และ tests ผล Explore 2026-10-01)

### 1.3 Test ทั้งชุด (~1 ชม.)
- `dotnet test DTMS.slnx -m:1` (~1,140 test, ไม่ต้องใช้ infra จริง)
- ไฟล์ที่ต้องดูเป็นพิเศษ:
  - `tests/Modules/Planning.UnitTests/T2_DeliveryOrderSagaStateMachineTests.cs:30-44`: ตัวเดียวที่ใช้ `AddMassTransitTestHarness`, `ITestHarness` และ saga harness ถ้า API ของ harness เปลี่ยนจะพังที่นี่
  - `tests/DTMS.Api.UnitTests/CentralOutboxDrainTests.cs:82,119,142`: fake `IPublishEndpoint`
  - consumer test ประมาณ 23 ไฟล์ที่ fake `ConsumeContext<T>`
- ห้ามลบ test ที่ fail ทิ้ง ให้แก้ให้ตรงกับ API ใหม่ หรือถ้าพฤติกรรมเปลี่ยนจริง ให้หยุดแล้วบันทึกไว้

### 1.4 Deploy + วัดผล (~1.5 ชม.)
1. `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api` (rebuild ทั้ง api และ outbox-worker)
2. **วัดแบบปิด floor**: comment บรรทัด `ThreadPool.SetMinThreads` ใน `Program.cs` ชั่วคราว แล้ว rebuild อีกรอบ และ restart 2 ครั้งด้วย `podman restart dtms-api` แต่ละรอบวัดด้วย
   ```bash
   podman logs --since 3m dtms-api 2>&1 | grep -E "Riot3ReconciliationService started|Bus started|RedisTimeout|401 \(Unauthorized\)"
   ```
   - ถ้าได้ ≤ 3 วินาทีทั้ง 2 รอบ แปลว่าต้นเหตุหายแล้ว
   - ถ้ายัง 20 วินาทีขึ้นไป แปลว่าคนที่ block thread **ไม่ใช่ RabbitMQ.Client** (ดู "สิ่งที่จะทำให้แผนนี้ใช้ไม่ได้")
3. เอา `SetMinThreads` กลับมา แก้ comment ให้ตรงกับผลที่วัดได้ แล้ว rebuild
4. Smoke test ของ flow ที่ใช้ message จริง ภายใน 30 นาทีหลัง deploy:
   - มี order จาก OMS ไหลเข้ามาตามปกติ consumer ทำงาน (`grep -c "R-FAULT"` ใน log ต้องได้ 0)
   - error queue ว่าง: `podman exec dtms-rabbitmq rabbitmqctl list_queues -q name messages | grep -i error | awk '$2>0'` ต้องไม่มี output
   - outbox drain ทำงาน: `rabbitmqctl` ไม่มี queue ที่ messages ค้างสะสมขึ้นเรื่อยๆ
   - `/health/ready` ได้ 200 (เห็นใน log `HTTP GET /health/ready responded 200`)

### เกณฑ์ผ่านของ Phase 1
- build ผ่าน, `dotnet test DTMS.slnx -m:1` เขียวทั้งหมด
- ช่วง `Riot3ReconciliationService started` → `Bus started` ≤ 3 วินาที**ตอนปิด SetMinThreads** (2 รอบ)
- smoke test 4 ข้อผ่าน และไม่มี Redis timeout หรือ WMS 401 ตอนเปิด

**Commit**: `fix(api): stop RabbitMQ blocking threads while the bus starts by moving to MassTransit 8.5`

---

## Phase 2 — poller รอ ApplicationStarted (~2–3 ชม.)

### 2.1 Helper (~30 นาที)
- ไฟล์ใหม่ `src/DTMS.SharedKernel/Hosting/HostLifetimeExtensions.cs`
  ```csharp
  public static class HostLifetimeExtensions
  {
      // Completes when the host has finished starting (or throws OCE when ct fires).
      public static Task WaitForApplicationStartedAsync(this IHostApplicationLifetime lifetime, CancellationToken ct)
      {
          if (lifetime.ApplicationStarted.IsCancellationRequested) return Task.CompletedTask;
          var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
          var r1 = lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
          var r2 = ct.Register(() => tcs.TrySetCanceled(ct));
          return tcs.Task.ContinueWith(t => { r1.Dispose(); r2.Dispose(); return t; }, TaskScheduler.Default).Unwrap();
      }
  }
  ```
- Test: ไฟล์ใหม่ `tests/DTMS.Api.UnitTests/HostLifetimeExtensionsTests.cs` (โปรเจกต์นี้อ้าง SharedKernel ผ่าน Api) กรณีที่ต้อง test:
  1. ยังไม่ started ต้องยังไม่ complete
  2. trigger started แล้วต้อง complete
  3. started ไปแล้วตั้งแต่ก่อนเรียก ต้อง complete ทันที
  4. ct ถูก cancel ต้องได้ `OperationCanceledException`
  - ใช้ `CancellationTokenSource` แทน `ApplicationStarted` ผ่าน NSubstitute `IHostApplicationLifetime`

### 2.2 ใช้กับ poller ทั้ง 8 ตัว (~1.5 ชม.)
รูปแบบเดียวกันทุกตัว: inject `IHostApplicationLifetime` (ต้นแบบ: `ShutdownPhaseRecorder.cs:29-39`) แล้วใส่ `await _lifetime.WaitForApplicationStartedAsync(stoppingToken);` **ก่อน** delay เดิม (try/catch `OperationCanceledException` → `return` เหมือนเดิม)

| Service | ไฟล์:บรรทัดของ delay เดิม |
|---|---|
| WmsLocationSyncService | `src/Modules/Wms/DTMS.Wms.Infrastructure/Services/WmsLocationSyncService.cs:41` (15s) |
| MapStationSyncService | `src/Modules/Facility/DTMS.Facility.Infrastructure/Services/MapStationSyncService.cs:35` (15s) |
| Riot3PositionPollerService | `src/DTMS.Api/RobotPositions/Riot3PositionPollerService.cs:44` (10s) |
| PoolDepthPollingService | `StartupDelay` 15s, delay อยู่ที่บรรทัด 42 (ใช้ grep หาไฟล์) |
| FleetUtilizationSnapshotService | `src/DTMS.Api/Infrastructure/FleetUtilizationSnapshotService.cs:39` (`WarmupDelay` 20s, บรรทัด 17) |
| CallbackTokenRefreshService | บรรทัด 46 (`StartupDelaySeconds` = 30, `CallbackTokenRefreshOptions.cs:23`) |
| InfraHealthPollerService | บรรทัด 49 (`StartupDelaySeconds`, `VendorHealthOptions.cs`) |
| Riot3HealthPollerService | บรรทัด 41 (`StartupDelaySeconds`, `VendorHealthOptions.cs`) |

- ไม่แตะ `Riot3ReconciliationService` และ `PlanningReconciliationService` เพราะ tick แรกรันทันทีโดยตั้งใจ (delay อยู่ท้าย loop, `Riot3ReconciliationService.cs:75`) ให้ถามก่อนถ้าจะเปลี่ยน
- ไม่แตะตัวที่ใช้ `PeriodicTimer` (DashboardCounterBatcher, FleetPositionThrottler, WindowUsageTracker) เพราะไม่ได้เรียกระบบภายนอก
- Test: ถ้า poller ตัวไหนมี unit test อยู่แล้ว constructor จะเปลี่ยน ให้เพิ่ม `Substitute.For<IHostApplicationLifetime>()` ที่ `ApplicationStarted` ถูก cancel ไว้แล้ว

### 2.3 Deploy + ตรวจ (~30 นาที)
- `dotnet test DTMS.slnx -m:1` แล้ว `ops\podman-rebuild.ps1 -Only api`
- ตรวจด้วย: log บรรทัดแรกของ `[WmsSync]`, `SyncMapStations` และ `[PoolDepthPolling]` ต้องมาหลัง `Application started`:
  ```bash
  podman logs --since 3m dtms-api 2>&1 | grep -nE "Application started|\[WmsSync\]|SyncMapStations|PoolDepthPolling\] started|FleetUtilization" | head
  ```

### เกณฑ์ผ่านของ Phase 2
- helper test 4 กรณีผ่าน และ test ทั้งชุดเขียว
- ใน log ไม่มี poller ภายนอกตัวไหนทำงานก่อน `Application started`

**Commit**: `fix(api): keep pollers from calling WMS, RIOT3 and OMS before the API has finished starting`

---

## การตรวจสอบ

| ตรวจอะไร | วิธี | ที่ไหน |
|---|---|---|
| Compile + test ทั้งหมด | `dotnet build DTMS.slnx -m:1` และ `dotnet test DTMS.slnx -m:1` | ทั้ง solution |
| ต้นเหตุหายจริง | ปิด SetMinThreads ชั่วคราว → `Riot3ReconciliationService started`→`Bus started` ≤ 3 วินาที (2 รอบ) | log `dtms-api` |
| Messaging ปกติ | `rabbitmqctl list_queues -q name messages \| grep -i error \| awk '$2>0'` ว่าง และ `R-FAULT` = 0 | `dtms-rabbitmq`, log |
| Health | `/health/ready` 200 | log `dtms-api` |
| Poller ลำดับถูก | grep ข้างบนใน 2.3 | log `dtms-api` |

## เอกสารที่ต้องอัปเดตเมื่อเสร็จ
- memory `project_api_startup_threadpool_starvation`: บันทึกผลวัดหลังอัปเกรด และสถานะของ SetMinThreads
- comment ใน `Program.cs:43-51`: เขียนให้ตรงกับผลวัดใหม่
- **เฝ้าดู 2–3 วันหลัง Phase 1** เพราะ retry, delayed redelivery และ kill switch จะเกิดเฉพาะตอนมี error จริง ให้เช็กวันละครั้ง:
  - `podman exec dtms-rabbitmq rabbitmqctl list_queues -q name messages consumers | awk 'NR>1 && ($2>0 || $3==0)'` ต้องว่าง
  - log ไม่มี `R-FAULT`, `x-delayed` หรือ kill switch
- **เซิร์ฟเวอร์ Linux แบบ offline**: bundle ที่ export ไว้ก่อนหน้านี้ยังเป็น 8.2.1 ต้องรัน `ops/server-export.ps1` ใหม่ก่อนติดตั้งบนเซิร์ฟเวอร์
- ADR ใหม่: **ไม่ต้อง** เพราะเป็นการอัปเกรดในเวอร์ชันหลักเดิมของ MassTransit ไม่ได้เปลี่ยนการตัดสินใจเชิงสถาปัตยกรรม

## ถือว่าเสร็จเมื่อ
- [ ] MassTransit ทั้ง 3 แพ็กเกจเป็น 8.5.11 ขึ้นระบบทั้ง api และ outbox-worker
- [ ] วัดตอนปิด floor แล้วได้ ≤ 3 วินาที (2 รอบ) หรือถ้าไม่ได้ ได้บันทึกไว้ว่าคนที่ block thread ไม่ใช่ RabbitMQ
- [ ] poller 8 ตัวรอ `ApplicationStarted`
- [ ] 2 commit บน `main`
- [ ] ใช้เวลาไม่เกินประมาณ 12 ชม. (เกิน 50% จาก 8 ชม. ให้หยุดแล้วประเมินขอบเขตใหม่)

## ทางออกถ้าติด
- **ถ้า 1.2 + 1.3 ใช้เกิน 2 ชม. แต่ยัง compile หรือ test ไม่ผ่าน**: ลอง 8.4.x แทน (Client 7 เหมือนกัน แต่ห่างจาก 8.2 น้อยกว่า)
- **ถ้า 8.4 ก็ยังไม่ไหว**: revert 1.1 แล้วทำแค่ Phase 2 + แก้ health check ให้ไม่ block (ใส่ `Task.Run` ไม่ได้ช่วย ให้ใช้ cache ผลลัพธ์ 10 วินาทีแทน) ถือว่าได้ 30% ของประโยชน์
- **ถ้าวัดใน 1.4 แล้วยังช้า**: เก็บอัปเกรดไว้ได้ (ไม่ทำให้อะไรแย่ลง) แล้วเปิด spike ใหม่ 2 ชม.เพื่อหาคนที่ block ด้วย `dotnet-stack` (ต้องวาง tool ลงใน image runtime ซึ่งเป็นงานแยก)

## ความเสี่ยงและการย้อนกลับ

| ความเสี่ยง | โอกาส | วิธีรับมือ / ย้อนกลับ |
|---|---|---|
| consumer ทำงานต่างไปจากเดิม (retry, redelivery, kill switch) จน message ตกไปที่ error queue | กลาง | ทำ smoke test ใน 1.4 ถ้าพบ ให้ `git revert` commit ของ Phase 1 แล้ว rebuild ฝั่ง broker ไม่ต้องย้อน เพราะชื่อ queue/exchange ยังใช้ default formatter เหมือนเดิม |
| ช่วงสลับมี message ค้างใน queue เดิม | ต่ำ | MassTransit 8.x ใช้ envelope รูปแบบเดิม consumer รุ่นใหม่จึงอ่าน message ที่ค้างอยู่ได้ |
| delayed redelivery ต้องใช้ delayed-exchange plugin | ต่ำ | ไม่ได้เปลี่ยนฝั่ง broker แต่ให้เช็กใน log ว่าไม่มี error เรื่อง `x-delayed-message` |
| health check ใหม่ทำให้ `/health/ready` เพี้ยน แล้ว supervisor restart วนไม่หยุด | ต่ำ | ดู log ทันทีหลัง deploy ถ้าพบให้ revert |

## สิ่งที่จะทำให้แผนนี้ใช้ไม่ได้
- ถ้าวัดใน 1.4 ตอนปิด floor แล้วยังช้า 20 วินาทีขึ้นไป แปลว่าสมมติฐาน "RabbitMQ.Client 6 แบบ sync คือคนที่ block thread" ผิด ต้องวางแผนใหม่โดยเริ่มจากการเก็บ thread dump

## ไว้ทีหลัง (ไม่อยู่ในขอบเขตนี้)
- sync-over-async ใน `SystemJwtValidator.cs:122,163,236` (per-request บน `/api/v1/source`)

## เมื่อเริ่มทำตามแผนนี้

**ขั้นแรก**: เปิด `Directory.Packages.props` แล้วเปลี่ยนบรรทัด 46-48 จาก `8.2.1` เป็น `8.5.11` แล้วรัน `dotnet restore DTMS.slnx` ตั้งแต่เริ่มจนเสร็จทั้งสองเฟสประมาณ 7–9 ชม. ส่วนใหญ่อยู่ที่ 1.2–1.4 (แก้ compile, test ทั้งชุด และ rebuild 3 รอบเพื่อวัดผล)
