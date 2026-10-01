# Tiered cache fail-open — แผนการพัฒนา

> **สถานะ**: 🟢 implement แล้ว 2026-10-01 **ตามฉบับแก้ไขหลังรีวิว** (ดู "ผลรีวิว" ด้านล่าง ซึ่ง supersede ส่วนที่ขัดกันในเนื้อหาเดิม)

## ผลรีวิว (2026-10-01) — สิ่งที่เปลี่ยนจากร่างเดิม

พบ 8 ช่องโหว่ในร่างเดิม แก้แล้วในโค้ดดังนี้

- **H1 `SetAsync` ยังทำให้ผู้เรียกรอ Redis**: ร่างเดิม await ทั้ง set และ publish ตอน Redis ล่มจึงเสียเวลา ~15 วินาที ไม่ใช่ 5 วินาที → เปลี่ยนเป็นเขียน L1 แล้ว return ทันที ส่วน L2 + publish ทำในงานเบื้องหลัง (`WriteL2AndPublishAsync`) และคงลำดับ set → publish ไว้
- **H2 รอ 5 วินาทีแม้ Redis ล่มสนิท**: เพราะ `AbortOnConnectFail=false` (`Program.cs:550`) คำสั่งจะถูกพักไว้จน timeout → เช็ก `_redis.IsConnected` ก่อน ถ้าไม่ได้เชื่อมต่อให้ถือว่า miss ทันที
- **H3 callback ไป OMS ไม่ได้รอดทุกกรณี**: การ refresh token เมื่อ OMS ตอบ 401 ต้องจอง lock ใน Redis (`CallbackTokenRefresher.cs:63-69`) โดยไม่มี catch ระหว่าง Redis ล่ม callback จึงทำงานได้แค่จนกว่า session ของ OMS จะหมดอายุ แผนนี้**ไม่แก้** แค่บันทึกไว้
- **H4 ค่าเก่าค้างหลัง Redis กลับมา**: Redis เก็บข้อมูลลงดิสก์ → `InvalidateAsync` ที่ล้มจะจำ key ไว้ แล้ว `ConnectionRestored` จะลบและ publish ซ้ำ (ยัง throw ให้ผู้เรียกรู้เหมือนเดิม)
- **H5 ผลกระทบของการทดสอบหยุด Redis ใหญ่กว่าที่เขียนไว้**: นอกจาก OMS เรียกเข้าได้ 401 แล้ว การสร้าง order (`CachedStationLookup`), `IdempotencyKeyFilter` และการ refresh token ก็พัง outbox ช้าลงสูงสุด 30 วินาที และ**ห้าม restart container** ระหว่างหยุด Redis (`MultiPartitionOutboxProcessor.cs:100-103` subscribe ตอนเริ่มโดยไม่มี try)
- **H6 กับดักใน test**: เจอ bug จริงจาก test คือ **`RedisTimeoutException` สืบทอดจาก `TimeoutException` ไม่ใช่ `RedisException`** `catch (RedisException)` ของร่างเดิมจะจับ timeout ที่เกิดเมื่อเช้าไม่ได้ → ใช้ `IsRedisFailure()` ที่รวม `RedisException`, `RedisTimeoutException` และ `ObjectDisposedException`
- **H7 log ท่วม**: log การเปลี่ยนสถานะครั้งเดียวผ่าน `ConnectionFailed` / `ConnectionRestored` ส่วน warning ราย key log แค่ชนิดและข้อความของ exception ไม่พิมพ์ stack
- **H8 ส่วนที่ยังพึ่ง Redis**: revocation (ตั้งใจล้มแบบปิด), `CachedStationLookup` / `IdempotencyKeyFilter`, lock ของ refresher, subscribe ตอน outbox เริ่มทำงาน และ health check `"redis"` ที่ต่อแบบ sync ยังพังตอน Redis ล่ม ทั้งหมดอยู่ใน "ไว้ทีหลัง"
- **เพิ่มเติม**: `GetOrSetWithLockAsync` (ไม่มีผู้เรียก) ยัง await การเขียน L2 เหมือนเดิม เพราะผู้ที่รออยู่ใน pod อื่นตีความ "lock ถูกปล่อย" ว่า "ค่าอยู่ใน Redis แล้ว"
- **Test**: `tests/DTMS.Api.UnitTests/RedisBackedTieredCacheTests.cs` 6 กรณี ส่วนกรณี "cache คืน null → อ่าน repo" ฝั่งผู้ใช้ มี test อยู่แล้ว (`CachedCredentialReaderTests.CacheMiss_StoresCiphertext_ButReturnsPlaintext`) test ทั้ง solution ผ่าน 1,317 ตัว
> **สิ่งที่ต้องการ**: เมื่อ Redis error (timeout, ล่ม, restart) การอ่าน credential, system client และ subscription ต้องตกไปอ่าน DB แทนที่จะพัง
> **ประเภท / ขนาด**: bug fix · S, ~2.5 ชม. (เฟสเดียว)
> **แผนแม่**: [masstransit-85-and-poller-startup.md](masstransit-85-and-poller-startup.md) แผนนี้**มาแทน Phase 2** ของแผนนั้น (ตัดสินใจ 2026-10-01)
> **สมมติฐาน**: ไม่มี

## สรุปการเปลี่ยนแปลงในประโยคเดียว

ถ้า Redis มีปัญหา ระบบจะช้าลงเล็กน้อยเพราะต้องไปอ่าน DB แทน แต่ callback ไป OMS, WMS sync และการขอ token จะ**ยังทำงานได้** ไม่พังเหมือนตอนนี้

## Root cause

- `RedisBackedTieredCache.GetAsync` ([RedisBackedTieredCache.cs:34-46](../../src/DTMS.Infrastructure/Caching/RedisBackedTieredCache.cs#L34-L46)) เรียก `db.StringGetAsync` โดยไม่มี try/catch exception ของ Redis จึงส่งต่อขึ้นไปถึงผู้เรียก
- ผู้ใช้ cache ทั้ง 3 ตัวเป็นแบบ read-through คือ "ถ้า cache คืนค่า `null` ให้ไปอ่าน DB" แต่**ไม่มีตัวไหนรองรับกรณีที่ cache throw**:
  - `CachedCredentialReader.GetStoredAsync` ([CachedCredentialReader.cs:65-69](../../src/Modules/Iam/DTMS.Iam.Application/Authorization/CachedCredentialReader.cs#L65-L69))
  - `CachedSystemClientReader` ([CachedSystemClientReader.cs:43-52](../../src/Modules/Iam/DTMS.Iam.Application/Authorization/CachedSystemClientReader.cs#L43-L52))
  - `CachedSubscriptionLookup` ([CachedSubscriptionLookup.cs:40-51](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/CachedSubscriptionLookup.cs#L40-L51))
- `SetAsync` ([RedisBackedTieredCache.cs:48-63](../../src/DTMS.Infrastructure/Caching/RedisBackedTieredCache.cs#L48-L63)) เขียน Redis **ก่อน** L1 ถ้า Redis throw ค่าจะไม่ถูกเก็บใน L1 เลย ทำให้ทุก request ต้องรอ Redis timeout ซ้ำทุกครั้ง
- ทั้ง `src` ไม่มีที่ไหนจับ `RedisException` เลย (grep `catch (Redis` ได้ 0 ผล)

**ตัวอย่างที่เกิดจริง** (2026-10-01 03:52 UTC): `GET iam:cred:wms` timeout 5 วินาที (`RedisBackedTieredCache.cs:40`) → `SystemCredentialWmsTokenProvider` จับ exception แล้วใช้ token จาก config ซึ่งว่าง → WMS ตอบ 401 ข้อมูลจริงอยู่ใน `iam.SystemCredentials` และอ่านได้ปกติตลอดเวลา

**ทำไม test ไม่จับได้**: `RedisBackedTieredCache` ไม่มี unit test ของตัวเองเลย (grep ใน `tests/` ไม่พบ) ส่วน test ของผู้ใช้ cache (`CachedCredentialReaderTests.cs`) ใช้ `Substitute.For<ITieredCache>()` ที่คืน `null` หรือคืนค่าเท่านั้น ไม่มีกรณี cache throw

## เป้าหมาย / สิ่งที่ไม่ทำ

**เป้าหมาย**:
- `GetAsync` และ `SetAsync` ไม่ throw `RedisException` อีก
- ผู้ใช้ทั้ง 3 ตัวได้ข้อมูลจาก DB เมื่อ Redis ล่ม
- ระหว่าง Redis ล่ม key เดิมจะต้องรอ Redis timeout ไม่เกิน 1 ครั้งต่อ 60 วินาที (ค่าที่อ่านจาก DB ถูกเก็บใน L1)

**สิ่งที่ไม่ทำ**:
- **ไม่แตะ revocation list** (`RedisSystemJwtRevocationList`) ตัวนี้ตั้งใจล้มแบบปิดไว้แล้ว ([ISystemJwtRevocationList.cs:8-16](../../src/Modules/Iam/DTMS.Iam.Application/Authorization/ISystemJwtRevocationList.cs#L8-L16), [system-onboarding.md:238-243](../system-onboarding.md)) ระหว่าง Redis ล่ม ช่อง `/api/v1/source/*` สำหรับระบบที่ใช้ bearer-jwt จะ**ยังตอบ 401 ตามที่ออกแบบ** แผนนี้ไม่ได้ทำให้ OMS เรียกเข้ามาได้ตอน Redis ล่ม
- **`InvalidateAsync` ยัง throw เหมือนเดิม** ใช้ตอน admin หมุนหรือแก้ credential ถ้าลบใน Redis ไม่สำเร็จแล้วเงียบไว้ pod อื่นจะใช้ค่าเก่าได้นานถึง 5 นาที (`L2Ttl`) admin ต้องเห็น error
- **ไม่แตะ `GetOrSetWithLockAsync`** เพราะไม่มีผู้เรียกเลย (grep นอก `Caching/` ได้ 0 ผล) ถ้ามีคนเริ่มใช้ในอนาคตค่อยว่ากัน
- ไม่ทำ circuit breaker หรือลด timeout ของ Redis (ดู "ไว้ทีหลัง")
- ไม่ทำ Phase 2 เดิม (poller รอ `ApplicationStarted`) ถูกแทนด้วยแผนนี้

---

## เตรียมตัวก่อนเริ่ม (~10 นาที)

ไฟล์ที่ต้องอ่าน:
- [RedisBackedTieredCache.cs](../../src/DTMS.Infrastructure/Caching/RedisBackedTieredCache.cs) (173 บรรทัด) ไฟล์เดียวที่จะแก้
- [ITieredCache.cs](../../src/DTMS.SharedKernel/Caching/ITieredCache.cs) contract ใน doc comment ต้องอัปเดตให้บอกพฤติกรรมใหม่
- [CachedCredentialReader.cs:57-77](../../src/Modules/Iam/DTMS.Iam.Application/Authorization/CachedCredentialReader.cs#L57-L77) ตัวอย่างผู้ใช้แบบ read-through
- [CachedCredentialReaderTests.cs](../../tests/DTMS.Api.UnitTests/CachedCredentialReaderTests.cs) รูปแบบ test ที่จะ copy (xunit + NSubstitute + FluentAssertions)

ทางเลือกที่ตัดสินไว้แล้ว:

| เรื่อง | ค่าที่เลือก | เปลี่ยนไปใช้อีกทางเมื่อ |
|---|---|---|
| แก้ที่ไหน | **ใน `RedisBackedTieredCache`** จุดเดียว ผู้ใช้ทั้ง 3 ตัวได้ผลพร้อมกัน | แก้ที่ผู้ใช้แต่ละตัวก็ต่อเมื่อมีผู้ใช้ที่**ต้องการ**ให้ cache throw ตอนนี้ไม่มี |
| จับ exception แบบไหน | `catch (RedisException)` (base ของ `RedisTimeoutException`, `RedisConnectionException`, `RedisServerException`) และ `ObjectDisposedException` จาก multiplexer | ห้ามจับ `Exception` ทั้งหมด เพราะจะกลืน `OperationCanceledException` ของ `ct` |
| ลำดับใน `SetAsync` | **เขียน L1 ก่อน** แล้วค่อยเขียน Redis และ publish ใน try | — ลำดับเดิม (Redis ก่อน) คือเหตุที่ L1 ว่างตอน Redis ล่ม |
| log | `LogWarning` พร้อม key และชนิด exception (ไม่ log ค่า เพราะ credential เป็นความลับ) | ถ้า log เยอะเกินไปตอน Redis ล่มนาน ค่อยเพิ่ม rate-limit log ซึ่งไม่น่าจำเป็น เพราะ L1 จำกัดไว้ที่ 1 ครั้งต่อ key ต่อ 60 วินาที |
| สัญญาณว่า Redis ล่ม | ใช้ health check `"redis"` ที่มีอยู่แล้ว ([Program.cs:665-678](../../src/DTMS.Api/Program.cs#L665-L678) คืน `Degraded`) ร่วมกับ log warning ใหม่ | — |

คำถามที่ค้างอยู่: ไม่มีข้อไหนที่ทำให้เริ่มงานไม่ได้

**ฐานในการประเมินเวลา**: `b3be4c2f fix(cache): SetAsync publishes invalidation + skip own-pod echoes` แก้ไฟล์เดียวกัน 4 ไฟล์ +72/-16 บรรทัด แผนนี้ขนาดใกล้เคียงกัน แต่มี test ใหม่ทั้งไฟล์

---

## Phase 1 — Cache ล้มแบบปลอดภัย (~2.5 ชม.)

### 1.1 แก้ `RedisBackedTieredCache` (~45 นาที)

**`GetAsync`**: ครอบเฉพาะส่วน L2 ไว้ใน try ส่วน L1 hit ยังทำงานเหมือนเดิม
```csharp
RedisValue raw;
try { raw = await _redis.GetDatabase().StringGetAsync(key); }
catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
{
    _log.LogWarning(ex, "Redis read failed for cache key {Key} — treating as a miss; caller falls back to its store", key);
    return null;
}
```

**`SetAsync`**: ย้าย `_local.Set` ขึ้นไปก่อน แล้วครอบ `StringSetAsync` กับ `PublishInvalidationAsync` ไว้ใน try เดียวกัน
- คงลำดับ "เขียน L2 แล้วค่อย publish" ไว้ เพราะ comment ที่บรรทัด 57-62 อธิบายเหตุผล ห้าม publish ถ้าเขียน L2 ไม่สำเร็จ
- ถ้าล้ม ให้ log warning แล้ว return ปกติ

**`InvalidateAsync`**: ไม่แก้ แค่เพิ่ม comment ว่าทำไมยัง throw

**`ITieredCache.cs`**: อัปเดต doc comment ของ `GetAsync` และ `SetAsync` ว่า "Redis failure = miss / best-effort write" และของ `InvalidateAsync` ว่า "throws on Redis failure, by design"

ข้อควรระวัง:
- `catch ... when` ต้อง**ไม่**จับ `OperationCanceledException` ถ้า `ct` ถูก cancel ให้ throw ต่อไปตามปกติ
- `StringGetAsync` ของ StackExchange.Redis ไม่รับ `ct` อยู่แล้ว (ดูโค้ดเดิมบรรทัด 40) จึงไม่มีปัญหาเรื่องนี้ใน path ของ Redis

### 1.2 Unit test ของ cache (~1 ชม.)
- ไฟล์ใหม่: `tests/DTMS.Api.UnitTests/RedisBackedTieredCacheTests.cs` (โปรเจกต์นี้อ้าง `DTMS.Api` ซึ่งดึง `DTMS.Infrastructure` มาด้วย)
- เตรียม test: `Substitute.For<IConnectionMultiplexer>()` ให้ `GetDatabase()` คืน `Substitute.For<IDatabase>()` และ `GetSubscriber()` คืน `Substitute.For<ISubscriber>()` ใช้ `new MemoryCache(new MemoryCacheOptions())` ตัวจริง, `new PodIdentity()` (ไม่มี parameter, `PodIdentity.cs`) และ `NullLogger<RedisBackedTieredCache>.Instance`
- กรณีที่ต้อง test (ทุกกรณี **fail ก่อนแก้ และผ่านหลังแก้** ยกเว้นข้อ 4 และ 5 ที่ล็อกพฤติกรรมเดิม):
  1. `StringGetAsync` throw `RedisTimeoutException` → `GetAsync` คืน `null` ไม่ throw
  2. `StringSetAsync` throw `RedisConnectionException` → `SetAsync` ไม่ throw, เรียก `GetAsync` ครั้งต่อไปได้ค่าจาก L1, และ**ไม่มีการ publish**
  3. Redis ล่มตลอด: Set แล้ว Get 3 ครั้ง → `StringGetAsync` ไม่ถูกเรียกเลย เพราะ L1 hit
  4. `InvalidateAsync` เมื่อ `KeyDeleteAsync` throw → **ยัง throw** และ L1 ถูกลบไปแล้ว
  5. ทางปกติ: Get hit/miss และ Set ทำงานเหมือนเดิม (ล็อกพฤติกรรมเดิมไว้)
- สร้าง exception ด้วย `new RedisTimeoutException("t", CommandStatus.Sent)` และ `new RedisConnectionException(ConnectionFailureType.SocketFailure, "c")`

### 1.3 Test ระดับผู้ใช้ (~20 นาที)
- เพิ่มใน `tests/DTMS.Api.UnitTests/CachedCredentialReaderTests.cs`: กรณี "cache คืน null เพราะ Redis ล่ม → ได้ค่าจาก repo"
- ตรงนี้ใช้ fake `ITieredCache` ที่คืน `null` ได้ เพราะหลังแก้ contract คือ "Redis failure = null" test นี้จึงบันทึก contract ไว้ฝั่งผู้ใช้ด้วย

### 1.4 Deploy + ตรวจ (~25 นาที)
- `dotnet test DTMS.slnx -m:1` (ต้องเขียวทั้งหมด)
- `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api`
- **ทดสอบบนระบบจริงด้วยการหยุด Redis ชั่วคราว** (~1 นาที ต้องได้รับอนุญาตก่อน เพราะ revocation จะ fail-close ทำให้ OMS เรียกเข้าไม่ได้ช่วงนั้นตามที่ออกแบบไว้):
  ```powershell
  New-Item -ItemType File -Force "$env:LOCALAPPDATA\dtms-podman-autostart.pause" | Out-Null
  podman stop dtms-redis
  # รอ WmsSync cycle ถัดไป (ทุก 5 นาที) หรือดู log ของ callback/subscription ที่เกิดขึ้นจริง
  podman logs --since 6m dtms-api 2>&1 | Select-String "Redis read failed|Redis write failed|\[WmsSync\]|401 \(Unauthorized\)"
  podman start dtms-redis
  Remove-Item "$env:LOCALAPPDATA\dtms-podman-autostart.pause"
  ```
  - **คาดหวัง**: เห็น warning `Redis read failed for cache key iam:cred:wms` และตามด้วย `[WmsSync] pulled=…` (สำเร็จ) **ไม่ใช่** 401
  - **ข้อจำกัด**: WmsSync ทำงานทุก 5 นาที (`opts.SyncIntervalSeconds`, ขั้นต่ำ 30 วินาที, `WmsLocationSyncService.cs:69` และ log วันนี้แสดงรอบ 03:52, 03:57, 04:02) ถ้าไม่อยากรอ ให้ใช้ off-ramp ด้านล่าง
- หลัง start Redis กลับมา: health check `"redis"` ต้องกลับเป็น Healthy และ log warning ต้องหยุด

### เกณฑ์ผ่านของ Phase 1
- test ใหม่ 5 + 1 กรณีผ่าน และ `dotnet test DTMS.slnx -m:1` เขียว
- ทดสอบหยุด Redis แล้ว WmsSync ยังดึงข้อมูลได้ (ไม่มี 401) หรือถ้าข้ามการทดสอบนี้ ต้องบันทึกไว้ชัดเจน

**Commit**: `fix(api): keep credential, client and subscription reads working when Redis fails`

---

## การตรวจสอบ

| ตรวจอะไร | วิธี | ที่ไหน |
|---|---|---|
| พฤติกรรม cache | `dotnet test tests/DTMS.Api.UnitTests -m:1 --filter RedisBackedTieredCacheTests` | DTMS.Api.UnitTests |
| ผู้ใช้ fallback ไป DB | `--filter CachedCredentialReaderTests` | DTMS.Api.UnitTests |
| ทั้ง solution | `dotnet test DTMS.slnx -m:1` | ทั้งหมด |
| ระบบจริง | ทดสอบหยุด Redis ใน 1.4 | log `dtms-api` |
| สัญญาณระยะยาว | `podman logs --since 24h dtms-api 2>&1 \| grep -c "Redis read failed\|Redis write failed"` ช่วงปกติต้องได้ 0 ถ้าไม่ใช่ 0 แปลว่า Redis มีปัญหาที่ก่อนหน้านี้ไม่มีใครเห็น | log |

## เอกสารที่ต้องอัปเดตเมื่อเสร็จ
- [masstransit-85-and-poller-startup.md](masstransit-85-and-poller-startup.md): เปลี่ยน Phase 2 เป็น "⏭️ ถูกแทนด้วย tiered-cache-fail-open.md" พร้อมเหตุผล 1 บรรทัด
- [system-onboarding.md:238-243](../system-onboarding.md): เพิ่ม 1 ย่อหน้าต่อจากเรื่อง fail-close ของ revocation ว่า credential, client และ subscription cache **fail-open ไปอ่าน DB** เพื่อไม่ให้คนอ่านเข้าใจว่าทั้งระบบ fail-close
- memory `project_api_startup_threadpool_starvation`: บันทึกว่า Phase 2 ถูกแทนแล้ว
- ADR ใหม่: **ไม่ต้อง** เพราะไม่ได้เปลี่ยนการตัดสินใจเรื่อง revocation และ cache เป็นรายละเอียดของ infra

## ถือว่าเสร็จเมื่อ
- [ ] `GetAsync` และ `SetAsync` ไม่ throw เมื่อ Redis error (มี test พิสูจน์)
- [ ] `InvalidateAsync` ยัง throw (มี test ล็อกไว้)
- [ ] ทดสอบหยุด Redis แล้ว WmsSync ไม่ได้ 401 หรือบันทึกว่าข้ามการทดสอบนี้
- [ ] 1 commit บน `main` และอัปเดตเอกสาร 2 ไฟล์
- [ ] ใช้เวลาไม่เกินประมาณ 4 ชม.

## ทางออกถ้าติด
- **ถ้า NSubstitute ของ `IConnectionMultiplexer` / `IDatabase` ใช้ยาก** (overload เยอะ): ใน 1.2 ให้ fake แค่ `StringGetAsync(RedisKey, CommandFlags)` และ `StringSetAsync` overload ที่โค้ดเรียกจริง ถ้ายังไม่ได้ ให้แยก `IDatabase` access เป็น seam แบบ `Func<IDatabase>` ภายใน class ไม่ต้องสร้าง interface ใหม่
- **ถ้าไม่สะดวกหยุด Redis บนเครื่องที่มีงานจริง**: ข้าม 1.4 ส่วน live test ใช้ unit test เป็นหลักฐาน แล้วไปทดสอบหยุด Redis บนเซิร์ฟเวอร์ Linux ก่อนเปิดใช้งานจริง

## ความเสี่ยงและการย้อนกลับ

| ความเสี่ยง | โอกาส | วิธีรับมือ / ย้อนกลับ |
|---|---|---|
| Redis **ช้า** (ไม่ล่ม) ทำให้ request แรกของแต่ละ key ต้องรอ timeout 5 วินาที | กลาง | L1 จำกัดไว้ที่ 1 ครั้งต่อ key ต่อ 60 วินาที ถ้ายังไม่พอ ดู "ไว้ทีหลัง" |
| DB รับโหลดเพิ่มตอน Redis ล่ม | ต่ำ | เป็นการอ่าน PK lookup 3 ตาราง และ L1 ช่วยไว้แล้ว ปริมาณเท่ากับตอน cache ว่างตามปกติ |
| pod อื่นใช้ค่าเก่าเพราะ publish invalidation ไม่ออก | ต่ำ | เกิดเฉพาะตอน `SetAsync` ล้ม ซึ่งเป็นการเขียนค่าใหม่จาก DB ไม่ใช่การแก้ข้อมูล ค่าเก่าใน L1 ของ pod อื่นหมดอายุใน ≤ 60 วินาที ส่วนการแก้ข้อมูลจริงใช้ `InvalidateAsync` ที่ยัง throw |
| ซ่อนปัญหา Redis ที่เคยเห็นได้จาก error | ต่ำ | log warning ใหม่ + health check `"redis"` (Degraded) ยังรายงานอยู่ ถ้าต้องย้อนกลับก็ `git revert` commit เดียว |

## สิ่งที่จะทำให้แผนนี้ใช้ไม่ได้
- ถ้าพบว่ามีผู้ใช้ `ITieredCache` ที่**ต้องการ**ให้ cache throw เพื่อ fail-close (เช่น เอาไปใช้เป็น security control) ต้องแยก API เป็น `GetAsync` (fail-open) กับ `GetStrictAsync` ตอนนี้ผู้ใช้ทั้ง 3 ตัวเป็น read-through cache ของ DB ทั้งหมด

## ไว้ทีหลัง (ไม่อยู่ในขอบเขตนี้)
- **health check `"redis"` สร้าง `ConnectionMultiplexer` ใหม่แบบ sync ทุกครั้งที่มีการเรียก** (`Program.cs:665-678`: `Connect` + `Ping` + `Close`) ทั้ง block thread และสิ้นเปลือง ควรใช้ `IConnectionMultiplexer` singleton กับ `PingAsync` เป็นเรื่องเดียวกับที่แก้ health check ของ RabbitMQ ใน 9bad6942
- circuit breaker หรือลด timeout ของ Redis สำหรับกรณี Redis ช้า
- sync-over-async ใน `SystemJwtValidator.cs:122,163,236`

## เมื่อเริ่มทำตามแผนนี้

**ขั้นแรก**: เปิด `src/DTMS.Infrastructure/Caching/RedisBackedTieredCache.cs` แล้วครอบ `db.StringGetAsync(key)` ที่บรรทัด 40 ด้วย try/catch `RedisException or ObjectDisposedException` ที่ return `null` ตั้งแต่เริ่มจนขึ้นระบบประมาณ 2.5 ชม. ถ้าข้าม live test เหลือประมาณ 2 ชม.
