# คนออนไลน์ + รายงานไปยัง session service กลาง + สถิติการใช้งาน — แผนการพัฒนา

> **สถานะ**: 🔵 ยังไม่เริ่ม — ร่างเมื่อ 2026-10-01
> **สิ่งที่ต้องการ**: (ก) ผู้ใช้ DTMS เห็นว่าตอนนี้มีกี่คนออนไลน์ ผู้ที่มีสิทธิ์เห็นรายชื่อว่าใครออนไลน์อยู่ (ข) DTMS ส่ง heartbeat ของผู้ใช้ที่ active ไปที่ session service กลาง (ค) ทีม ops เปิด Reports แล้วเห็นสถิติการใช้งานย้อนหลังรายคน/รายวัน
> **ประเภท / ขนาด**: feature + integration · L, ประมาณ 21 ชม. (5 เฟส)
> **แผนแม่**: ไม่มี ต่อจากการรีวิวโค้ด heartbeat ฝั่ง browser (TMS/Inventory) ในแชทวันที่ 2026-10-01
> **สมมติฐาน**:
> 1. "ออนไลน์" = มี WebSocket เปิดอยู่กับ DTMS และ "active" = มีการกด/พิมพ์/แตะ/เลื่อนในแท็บที่มองเห็นอยู่ภายใน 2 นาทีล่าสุด แสดงทั้งสองค่า
> 2. ครอบคลุมทั้ง console และ operator PWA (`/m/*`) แต่ตัวเลขแสดงแค่ใน TopNav ของ console
> 3. สัญญาของ session service กลาง (เฟส 5) อนุมานจากโค้ดตัวอย่างที่ผู้ใช้ส่งมาเท่านั้น คือ `POST /session/heartbeat` + `Authorization: Bearer <JWT ของผู้ใช้>` + body `{ "service": "<ชื่อ>" }` และ 401 = token ใช้ไม่ได้ **ไม่มีอะไรใน repo ยืนยัน** (ดู คำถามที่ค้างอยู่)
> 4. วันในรายงานสถิติตัดตามเวลาไทย (UTC+7) ยังไม่มีรายงานไหนใน repo ที่ตัดวันตาม timezone (grep `AT TIME ZONE` / `TimeZoneInfo` ใน src ไม่เจอนอก migration) จึงเป็นการตัดสินใจใหม่

## ที่มา

โค้ดตัวอย่างที่ระบบอื่นใช้ (TMS/Inventory) ยิง HTTP polling จาก browser ทุก 10 วินาที และเก็บ JWT ใน `localStorage` ซึ่งใช้กับ DTMS ไม่ได้ เพราะ:

- DTMS เก็บ JWT ไว้ใน cookie `httpOnly` ([session.ts:121](../../frontend/lib/auth/session.ts#L121))
- ทุก request ฝั่ง browser วิ่งผ่าน IP ของ Next และแชร์ rate-limit bucket เดียวกัน polling 8 ครั้ง/นาที/แท็บ จะกิน quota ของงานจริง

DTMS มี SignalR hub อยู่แล้ว 7 ตัว ([Program.cs:1122-1133](../../src/DTMS.Api/Program.cs#L1122-L1133)) และ path `/hubs` ถูกจัดเป็น traffic class `Infra` ที่ไม่นับ quota ผู้ใช้ ([RateLimitPartitioner.cs:48-53](../../src/DTMS.Api/Infrastructure/RateLimiting/RateLimitPartitioner.cs#L48-L53)) presence จึงทำเป็น hub ตัวที่ 8 ได้โดยไม่มี HTTP polling เลย

ประวัติ: presence ของ operator เคยถูก**ออกแบบไว้ในเอกสาร**เท่านั้น ([phase-4-transport-manual.md:255](../multi-mode-transport/phases/phase-4-transport-manual.md), `/presence/heartbeat`) ไม่เคยถูกสร้างในโค้ด (`git log -S"OperatorPresence"` เจอแค่ 2 commit ที่เป็น docs) จึง**ไม่ใช่**ของที่เคยสร้างแล้วถูกถอดออก แผนนี้ไม่ใช้แบบ HTTP heartbeat ในเอกสารนั้น

## เป้าหมาย / สิ่งที่ไม่ทำ

**เป้าหมาย**:
- (ก) TopNav แสดง pill "N online" (tooltip: X active) อัปเดตภายใน ≤ 3 วินาทีหลังมีคนเข้า/ออก โดยไม่มี HTTP request เพิ่มเลย
- (ก) ผู้ที่มี `dtms:iam:presence:read` เปิด `/admin/online-users` เห็นรายชื่อ: ชื่อ, รหัสพนักงาน, console/operator, จำนวนแท็บ, active ล่าสุด, เชื่อมต่อตั้งแต่
- (ค) ทุกการเชื่อมต่อที่จบ (ปิดแท็บ, logout, เน็ตหลุด, API restart แบบปกติ) ได้ 1 แถวใน `iam.UserSessionLog` และ Reports มีแท็บ "Usage" แสดง DAU รายวัน + ตารางรายคน
- (ข) เมื่อเปิด `CentralSession__Enabled=true` ผู้ใช้ที่ active ทุกคนถูกส่ง heartbeat ไปที่ service กลางไม่เกิน 1 ครั้งต่อ interval ต่อคน (ไม่ใช่ต่อแท็บ)

**สิ่งที่ไม่ทำ**:
- ไม่ดึง `/session/online-users` จาก service กลางมาแสดง DTMS แสดงแค่ตัวเลขของตัวเอง ถ้าต้องการตัวเลขรวมข้ามแอปค่อยทำแผนแยก
- ไม่เก็บ presence ใน Redis ในรอบนี้ (ดูตารางทางเลือก) เพราะ API รันแค่ replica เดียว
- ไม่เก็บ heartbeat ทุกครั้งลง Postgres เก็บแค่ 1 แถวต่อการเชื่อมต่อ ตอนที่จบ
- ไม่บังคับ logout เมื่อ idle และไม่ทำ "ใครกำลังดูหน้านี้อยู่"
- ไม่แก้ ADR-018 / การตรวจ JWT ใดๆ

---

## เตรียมตัวก่อนเริ่ม (~20 นาที)

ไฟล์ที่ต้องอ่าน:

- [OperatorPoolHub.cs](../../src/DTMS.Api/Realtime/Hubs/OperatorPoolHub.cs) (47 บรรทัด): **ต้นแบบของ hub** ทั้ง group, auto-join ใน `OnConnectedAsync` และ `Subscribe`/`Unsubscribe` แบบ no-op ที่ hook ฝั่ง frontend ต้องเรียก
- [DashboardCounterBatcher.cs](../../src/DTMS.Api/Realtime/Pipeline/DashboardCounterBatcher.cs) (104 บรรทัด): **ต้นแบบของ broadcaster** เป็น `BackgroundService` + `PeriodicTimer` + `IHubContext<THub, TClient>` ลงทะเบียนเป็นทั้ง singleton และ hosted service ([Program.cs:482-485](../../src/DTMS.Api/Program.cs#L482-L485))
- [ClaimsPrincipalExtensions.cs:34-48](../../src/DTMS.Api/Auth/ClaimsPrincipalExtensions.cs#L34-L48): `ResolveUserId()` ซึ่งเป็น**ตัวเดียวที่ใช้ระบุตัวผู้ใช้** ห้ามใช้ `Context.UserIdentifier` เพราะไม่มี `IUserIdProvider` แบบ custom และ `MapInboundClaims = false` ([Program.cs:169](../../src/DTMS.Api/Program.cs#L169)) ทำให้ token ของ External Auth ไม่มี `NameIdentifier` ค่าจึงเป็น null
- [Program.cs:392-395](../../src/DTMS.Api/Program.cs#L392-L395): ลำดับ claim ของชื่อที่แสดง (`displayName` → `ClaimTypes.Name` → `unique_name`)
- [use-hub-subscription.ts](../../frontend/lib/hooks/use-hub-subscription.ts) (209 บรรทัด) และ [use-pool-trips.ts:83-100](../../frontend/lib/hooks/use-pool-trips.ts#L83-L100): วิธีใช้ hook
- [SystemRequestLogSink.cs](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Logging/SystemRequestLogSink.cs) + [Program.cs:557-577](../../src/DTMS.Api/Program.cs#L557-L577): **ต้นแบบของตาราง log แบบ partition** ทั้งการเขียนแบบ batch และการลบของเก่า
- [AddIamSystemClientsPhaseS2.cs:114-150](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Migrations/20260629000000_AddIamSystemClientsPhaseS2.cs#L114-L150): DDL ของตาราง partition ที่จะ copy

ทางเลือกที่ตัดสินไว้แล้ว:

| เรื่อง | ค่าที่เลือก | เปลี่ยนไปใช้อีกทางเมื่อ |
|---|---|---|
| ช่องทางส่งสัญญาณ | SignalR hub ใหม่ `/hubs/presence` | — HTTP polling ใช้ไม่ได้เพราะกิน rate-limit bucket ที่แชร์กัน |
| ที่เก็บ presence | in-memory (`ConcurrentDictionary` เหมือน bucket ใน [RateLimitedHubFilter.cs:29](../../src/DTMS.Api/Realtime/Filters/RateLimitedHubFilter.cs#L29)) อยู่หลัง interface `IPresenceStore` | **เมื่อรัน API มากกว่า 1 replica** (มีแค่ [docker-compose.scale.yml](../../docker-compose.scale.yml) สำหรับทดสอบ และ backplane ปิดอยู่ที่ [docker-compose.yml:411](../../docker-compose.yml)) ให้ทำ `RedisPresenceStore` ตอนนั้น ตอนนี้ API restart = ทุก socket หลุดและ reconnect ใหม่ state in-memory จึงตรงกับความจริงเสมอ ส่วน Redis ต้องมีตัวกวาด conn ของ pod ที่ตาย |
| ตัวเลขอัปเดตยังไง | broadcaster คำนวณ summary ทุก 2 วินาที แล้วส่งเฉพาะเมื่อค่าเปลี่ยน | การเปลี่ยนจาก active → idle เกิดจากเวลาที่ผ่านไป ไม่ได้มี event ให้จับ ถ้าใช้ event-only จะพลาดกรณีนี้ |
| client ส่ง `Active()` บ่อยแค่ไหน | ≤ 1 ครั้ง/60 วินาที เฉพาะตอนแท็บมองเห็นอยู่และมีกิจกรรม + ส่งทันทีเมื่อเปลี่ยนจาก idle → active | ลดถี่ลงถ้า `HubMetrics` แสดงภาระ แต่ filter อนุญาต 20/วินาที/conn ([RateLimitedHubFilter.cs:26-27](../../src/DTMS.Api/Realtime/Filters/RateLimitedHubFilter.cs#L26-L27)) ห่างไกลมาก |
| endpoint รายชื่อคนออนไลน์อยู่ที่ไหน | `src/DTMS.Api/Modules/AdminPresenceEndpoints.cs` → `GET /api/v1/admin/presence` (copy [AdminPoolEndpoints.cs](../../src/DTMS.Api/Modules/AdminPoolEndpoints.cs)) | — อยู่ใน Iam.Presentation ไม่ได้ เพราะ store อยู่ใน DTMS.Api (Iam ไม่ได้อ้างถึง DTMS.Api) |
| สถิติเก็บที่ไหน | ตารางใหม่ `iam.UserSessionLog` แบบ partition รายเดือน 1 แถว/การเชื่อมต่อ เขียนผ่าน `IBatchedLogWriter<T>` | ถ้าต้องการรายละเอียดระดับนาที ค่อยเพิ่มตารางนับรายนาที อย่าเขียนทุก heartbeat ลง DB (dev DB อยู่บน HDD ที่ fsync ช้า) |
| เก็บย้อนหลังนานแค่ไหน | `RetainMonths: 13` (เทียบปีต่อปีได้) | ผู้ใช้กำหนดอื่น — แก้ตัวเลขเดียวใน Program.cs |
| ส่ง heartbeat ไป service กลางจากไหน | **ฝั่ง server** (`CentralSessionHeartbeatService` ใน DTMS.Api) ใช้ JWT ที่ผู้ใช้ใช้ต่อ hub ถือไว้ใน memory เท่านั้น | ถ้า service กลางมี endpoint แบบ batch ที่ใช้ credential ของ DTMS เอง ให้เลิกถือ JWT ของผู้ใช้ แล้วส่ง batch เดียว — ดีกว่าทุกด้าน |

คำถามที่ค้างอยู่:

- [ ] **สัญญาของ session service กลาง**: base URL (น่าจะเป็นตัวเดียวกับ `DTMS_API_BASE_URL` = `http://10.204.212.28:15000` ที่ [docker-compose.yml:758](../../docker-compose.yml) ใช้สำหรับ `/auth/login` แต่**ยังไม่ยืนยัน**), ชื่อ `service` ของ DTMS, TTL ฝั่งนั้น (กำหนด interval), response เมื่อ token หมดอายุ, มี endpoint แบบ batch / service credential / `/session/end` หรือไม่ — **บล็อกเฟส 5** ถ้าต้องเดา: ใช้สัญญาตามสมมติฐาน 3, `service = "DTMS"`, interval 30 วินาที, และปิดไว้โดย default
- [ ] เก็บสถิติ 13 เดือนพอไหม — ไม่บล็อก
- [ ] ใครควรได้ `dtms:iam:presence:read` (บัญชีของผู้ใช้เองมี wildcard ใช้ได้ทันที คนอื่นต้อง grant บน auth service) — ไม่บล็อก

**ฐานในการประเมินเวลา**:
- `df93120b` (OperatorPoolHub + หน้า `/m/pool`) แก้ 15 ไฟล์ +966 บรรทัด ≈ ขนาดเฟส 1+2
- `65ab4705` (แท็บรายงาน Manual operators) 10 ไฟล์ +659 บรรทัด ≈ ส่วนรายงานของเฟส 4
- เฟส 3 มี migration ตาราง partition เพิ่ม ซึ่งรายงานนั้นไม่มี

---

## Phase 1 — Presence hub ฝั่ง backend (~4.5 ชม.)

### 1.1 Store แบบ in-memory (~1.5 ชม.)

ไฟล์:
- ใหม่: `src/DTMS.Api/Realtime/Presence/IPresenceStore.cs`, `InMemoryPresenceStore.cs`, `PresenceModels.cs`
- เทสต์: `tests/DTMS.Api.UnitTests/InMemoryPresenceStoreTests.cs`
  - 2 conn ของ user เดียวกัน → `OnlineUsers == 1`
  - `MarkActive` เกิน 2 นาที → นับเป็น online แต่ไม่ active
  - `Disconnect` คืน session ที่จบพร้อม `ActiveMinutes` ที่ถูก
  - `Disconnect` ซ้ำ → null
  - `DrainAll` แล้ว `Disconnect` → null

```csharp
public sealed record PresenceIdentity(string UserId, string DisplayName);

public sealed record PresenceSummary(int OnlineUsers, int ActiveUsers);

public sealed record PresenceUserView(string UserId, string DisplayName, IReadOnlyList<string> Clients,
    int Connections, DateTime ConnectedSinceUtc, DateTime? LastActiveUtc, bool IsActive);

public sealed record FinishedSession(PresenceIdentity User, string Client,
    DateTime StartedUtc, DateTime EndedUtc, int ActiveMinutes);

public interface IPresenceStore
{
    void Connect(string connectionId, PresenceIdentity user, DateTime nowUtc);
    void SetClient(string connectionId, string client);            // "console" | "operator"
    void MarkActive(string connectionId, DateTime nowUtc);
    FinishedSession? Disconnect(string connectionId, DateTime nowUtc);
    IReadOnlyList<FinishedSession> DrainAll(DateTime nowUtc);      // graceful shutdown
    PresenceSummary Summary(DateTime nowUtc);
    IReadOnlyList<PresenceUserView> Users(DateTime nowUtc);
}
```

- รับ `nowUtc` เป็น parameter เพื่อให้เทสต์ได้โดยไม่ต้องมีนาฬิกาปลอม
- `ActiveWindow = 2 นาที` เป็นค่าคงที่
- `ActiveMinutes` นับนาทีที่ไม่ซ้ำกันที่มี `MarkActive` (เก็บ `lastActiveMinute` แล้ว +1 เมื่อนาทีเปลี่ยน)

### 1.2 Hub + client interface (~1.5 ชม.)

ไฟล์:
- ใหม่: `src/DTMS.Api/Realtime/Hubs/PresenceHub.cs`, `src/DTMS.Api/Realtime/Hubs/Clients/IPresenceClient.cs`
- แก้: [ClaimsPrincipalExtensions.cs](../../src/DTMS.Api/Auth/ClaimsPrincipalExtensions.cs) — เพิ่ม `ResolveDisplayName()` ตามลำดับที่ [Program.cs:392-395](../../src/DTMS.Api/Program.cs#L392-L395) แล้วให้ Program.cs เรียกตัวนี้แทน (กันสองที่เพี้ยนกัน)
- แก้: [Program.cs](../../src/DTMS.Api/Program.cs)
  - ลงทะเบียน `IPresenceStore` เป็น singleton ข้าง hub singletons (:465-485)
  - `app.MapHub<PresenceHub>("/hubs/presence");` ต่อจาก :1133
- เทสต์: `tests/DTMS.Api.UnitTests/ClaimsPrincipalResolveUserIdTests.cs` — เพิ่มเคสของ `ResolveDisplayName` (มีไฟล์อยู่แล้ว)

```csharp
public interface IPresenceClient { Task PresenceChanged(PresenceSummary summary); }

[Authorize]
public sealed class PresenceHub(IPresenceStore store) : Hub<IPresenceClient>
{
    public const string WatchGroup = "presence";
    public override async Task OnConnectedAsync()    // ResolveUserId() null → Context.Abort(); return
    public override async Task OnDisconnectedAsync(Exception? ex)   // store.Disconnect (เฟส 3 ส่งต่อให้ writer)
    public async Task Subscribe(string client)        // SetClient + join group + Clients.Caller.PresenceChanged(summary)
    public Task Unsubscribe() => Groups.RemoveFromGroupAsync(Context.ConnectionId, WatchGroup);
    public Task Active() { store.MarkActive(Context.ConnectionId, DateTime.UtcNow); return Task.CompletedTask; }
}
```

- ใช้ `[Authorize]` เปล่า แบบเดียวกับ [ManualBoardHub.cs:16](../../src/DTMS.Api/Realtime/Hubs/ManualBoardHub.cs#L16) ไม่ใช้ `OperatorOnly` เพราะ console ทุกคนต้องเชื่อมได้
- ส่ง summary ให้คนที่เพิ่งต่อจากใน `Subscribe` ไม่ใช่ใน `OnConnectedAsync` เพราะ `useHubSubscription` ต่อ handler ก่อนเรียก `Subscribe` ([use-hub-subscription.ts:121-140](../../frontend/lib/hooks/use-hub-subscription.ts#L121-L140)) ลำดับจึงแน่นอน

### 1.3 Broadcaster (~1.5 ชม.)

ไฟล์:
- ใหม่: `src/DTMS.Api/Realtime/Presence/PresenceBroadcaster.cs` — copy `DashboardCounterBatcher`
  - `PeriodicTimer(2s)` คำนวณ `store.Summary(now)` แล้วส่ง `Clients.Group(WatchGroup).PresenceChanged` เฉพาะเมื่อต่างจากค่าล่าสุด
  - error → `LogWarning` แล้วทำงานต่อ
- แก้: Program.cs — ลงทะเบียนเป็น singleton + hosted service แบบ :482-485
- เทสต์: ไม่มีต้นแบบเทสต์ hub/batcher ใน repo (grep `Hub` ใน tests เจอแค่ `RateLimitPartitionerTests`) ตรรกะ "ส่งเมื่อเปลี่ยน" แยกเป็น method `internal static bool ShouldPush(PresenceSummary? last, PresenceSummary now)` แล้วเทสต์ใน `InMemoryPresenceStoreTests.cs`

### Phase 1 acceptance

- `dotnet build DTMS.slnx -m:1` ผ่าน
- `dotnet test tests/DTMS.Api.UnitTests -m:1` ผ่าน รวมเทสต์ใหม่
- `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api` แล้วเปิด console → DevTools > Network > WS เห็น `/hubs/presence` สถานะ 101 (หน้า frontend ยังไม่เรียก — ทดสอบจาก console ของ browser ด้วย `getHub` หรือข้ามไปตรวจในเฟส 2)

**Commit**: `feat(api): track who is connected to dtms in a presence hub`

---

## Phase 2 — ตัวเลขคนออนไลน์ + หน้ารายชื่อ (~5 ชม.)

### 2.1 Provider + ตัวตรวจจับกิจกรรม (~2 ชม.)

ไฟล์:
- ใหม่: `frontend/lib/realtime/presence-provider.tsx`
  - `PresenceProvider` + `usePresence()` คืน `{ summary, connected }`
  - ใช้ `useHubSubscription({ hubPath: "/hubs/presence", subscribeMethod: "Subscribe", unsubscribeMethod: "Unsubscribe", subscribeArgs: [client], eventHandlers: { PresenceChanged: setSummary }, enabled: !!user })`
  - `client = pathname.startsWith("/m") ? "operator" : "console"`
- แก้: [app/providers.tsx](../../frontend/app/providers.tsx) — ครอบใน `AuthProvider` (:22) เพื่อให้ทำงานทุกหน้า รวมถึง `/m/*` และ `/dispatch/manual` ที่ไม่มี TopNav

ตัวตรวจจับกิจกรรม (ใน provider เดียวกัน):
- listener `pointerdown`, `keydown`, `touchstart`, `wheel` แบบ `{ passive: true, capture: true }` บน `window` เขียนแค่ `lastActivityRef.current = Date.now()`
- `setInterval(15s)`: ถ้า `document.visibilityState === "visible"` และมีกิจกรรมภายใน 60 วินาที และ (ส่งครั้งล่าสุดเกิน 60 วินาที หรือเพิ่งเปลี่ยนจาก idle) → `getHub("/hubs/presence").invoke("Active").catch(() => {})`
- `visibilitychange` → visible: ส่งทันทีถ้ามีกิจกรรม (ต้นแบบการฟัง: [use-poll-schedule.ts:107-119](../../frontend/lib/hooks/use-poll-schedule.ts#L107-L119))

> **กับดัก**:
> - connection เป็น singleton ที่อยู่ข้ามการเปลี่ยนหน้า ([signalr-client.ts:37-41](../../frontend/lib/realtime/signalr-client.ts#L37-L41)) และ logout ใช้ `router.push("/login")` ([auth-provider.tsx:157-171](../../frontend/components/auth/auth-provider.tsx#L157-L171)) socket เดิมจึงยังเปิดอยู่ ผู้ใช้ที่ logout แล้วจะยัง "ออนไลน์" ต่อ ต้องเรียก `disposeHub("/hubs/presence")` เมื่อ `user` เปลี่ยนเป็น null
> - ไม่มี eslint ต้องเช็ค dependency array ของทุก `useEffect` ด้วยตาเอง

### 2.2 Pill ใน TopNav (~45 นาที)

ไฟล์:
- ใหม่: `frontend/components/shell/online-pill.tsx` — แสดง "N online" + tooltip "X active"
  - ถ้า `hasPermission(Permissions.Iam.PresenceRead)` เป็น link ไป `/admin/online-users`
  - ซ่อนเมื่อ `!connected`
- แก้: [top-nav.tsx](../../frontend/components/shell/top-nav.tsx) — แทรกระหว่าง `<ThemeToggleBtn/>` (:119) กับ `<AccountMenu/>` (:121)

### 2.3 Endpoint รายชื่อ + permission (~1 ชม.)

ไฟล์:
- แก้: [Permissions.cs](../../src/Modules/Iam/DTMS.Iam.Application/Authorization/Permissions.cs) — ใน `class Iam` (:136) เพิ่ม
  ```csharp
  public static readonly PermissionDefinition PresenceRead = new("dtms:iam:presence:read", "See who is online", "Iam");
  ```
  - ไม่ต้องมี migration เพราะตาราง permission ถูกลบไปแล้ว ([PermissionCatalogTests.cs:90-95](../../tests/DTMS.ArchitectureTests/PermissionCatalogTests.cs))
- ใหม่: `src/DTMS.Api/Modules/AdminPresenceEndpoints.cs` — `MapGroup("/api/v1/admin/presence").WithTags("Admin").RequireAuthorization()` แล้ว `MapGet("", (IPresenceStore s) => Results.Ok(s.Users(DateTime.UtcNow))).RequirePermission(Permissions.Iam.PresenceRead)`
- แก้: [ModuleEndpointRegistration.cs](../../src/DTMS.Api/Modules/ModuleEndpointRegistration.cs) — เพิ่ม `app.MapAdminPresenceEndpoints();` ข้าง :45
- แก้: [frontend/lib/auth/permissions.ts](../../frontend/lib/auth/permissions.ts) — `Iam.PresenceRead` (:6-9)
- เทสต์: `dotnet test tests/DTMS.ArchitectureTests -m:1` ซึ่งตรวจไวยากรณ์ของ code และกันการใส่ string ตรงๆ

### 2.4 หน้า `/admin/online-users` (~1.25 ชม.)

ไฟล์:
- ใหม่: `frontend/app/api/admin/presence/route.ts` (copy [geofence-overrides/route.ts](../../frontend/app/api/admin/manual/geofence-overrides/route.ts))
- ใหม่: `frontend/app/admin/online-users/page.tsx` — `<TopNav/>`, `<LeftRail/>` และ `<PermissionGuard requires={Permissions.Iam.PresenceRead}>` แบบ [app/admin/systems/page.tsx:18](../../frontend/app/admin/systems/page.tsx#L18)
- ใหม่: `frontend/components/admin/online-users-experience.tsx`
  - fetch ตอน mount และ refetch เมื่อ `usePresence().summary` เปลี่ยน (ไม่ poll)
  - ตารางรายชื่อ; ถ้าไม่มีใครออนไลน์ให้แสดงข้อความว่าง
- แก้: [left-rail.tsx](../../frontend/components/shell/left-rail.tsx) — เพิ่มเมนูในกลุ่ม admin (:200-237) พร้อม `requires`
- `proxy.ts` ไม่ต้องแก้ เพราะ `/admin/:path*` อยู่ใน matcher แล้ว ([proxy.ts:80-93](../../frontend/proxy.ts#L80-L93))

### Phase 2 acceptance

- `cd frontend && npm run typecheck && npm run check:routes` ผ่าน
- `ops\podman-rebuild.ps1 -Only frontend` (และ `-Only api`) แล้ว:
  1. เปิด console 2 แท็บ → pill = "1 online"
  2. login อีกบัญชีใน InPrivate → ทั้งสองหน้าเป็น "2 online" ภายใน ≤ 3 วินาที
  3. ปิด InPrivate → กลับเป็น 1
  4. ปล่อยไว้เฉยๆ 2 นาที → tooltip active ลดลง
  5. logout → อีกฝั่งลดลงทันที
- DevTools > Network: ไม่มี HTTP request ซ้ำเป็นระยะจาก presence

**Commit**: `feat(frontend): show how many people are online and who they are`

---

## Phase 3 — บันทึก session ลง DB (~4 ชม.)

### 3.1 Entity + EF config + migration (~2 ชม.)

ไฟล์:
- ใหม่: `src/Modules/Iam/DTMS.Iam.Domain/Entities/UserSessionLogEntry.cs` — รูปแบบเดียวกับ `SystemRequestLogEntry.cs` (มี ctor ว่างแบบ private + ctor เต็ม)
  ```csharp
  Guid Id; DateTime StartedAt; DateTime EndedAt; string UserId /*100*/; string DisplayName /*200*/;
  string Client /*20*/; int ActiveMinutes; string EndReason /*20: "disconnect" | "shutdown"*/
  ```
- แก้: [IamDbContext.cs](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Data/IamDbContext.cs) — `DbSet<UserSessionLogEntry> UserSessionLog` (:11-23) + config ต่อจาก :179-194
  - PK `(StartedAt, Id)` เพราะ Postgres บังคับให้ partition key อยู่ใน PK
  - ใส่ `HasMaxLength` ให้ครบ
- ใหม่: `src/Modules/Iam/DTMS.Iam.Infrastructure/Migrations/20261001100000_AddUserSessionLog.cs` — **เขียนด้วยมือ** ตามรูปแบบ [20260904100000_AddTokenSourceKey.cs](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Migrations/20260904100000_AddTokenSourceKey.cs)
  - `[DbContext(typeof(IamDbContext))]`, `[Migration("20261001100000_AddUserSessionLog")]`
  - ไม่มีไฟล์ Designer เพราะ migration ของ Iam ไม่มีสักตัว
  - Up = raw SQL `CREATE TABLE iam."UserSessionLog" (...) PARTITION BY RANGE ("StartedAt")` + index `("UserId", "StartedAt" DESC)` + pre-seed partition ของเดือนนี้และอีก 2 เดือน (copy loop จาก [AddIamSystemClientsPhaseS2.cs:138-149](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Migrations/20260629000000_AddIamSystemClientsPhaseS2.cs#L138-L149))
  - **เพิ่ม partition ของเดือนที่แล้วด้วย**: session ที่เริ่มก่อนเที่ยงคืนวันที่ 1 แต่ไปจบหลัง migration รัน ต้องมี partition รองรับ
  - Down = `DROP TABLE IF EXISTS iam."UserSessionLog" CASCADE;`
  - timestamp `20261001100000` ใหม่กว่าตัวล่าสุดของทั้ง repo (`20260911100000_DropCarrierBarcode` ใน Fleet) จึงไม่ชนใน `__EFMigrationsHistory` ที่ใช้ร่วมกัน

### 3.2 การเขียนแบบ batch + การลบของเก่า (~1 ชม.)

ไฟล์:
- ใหม่: `src/Modules/Iam/DTMS.Iam.Infrastructure/Logging/UserSessionLogSink.cs` — copy `SystemRequestLogSink` (AddRange + SaveChanges ครั้งเดียว)
- แก้: [ModuleServiceRegistration.cs:168-172](../../src/DTMS.Api/Modules/ModuleServiceRegistration.cs#L168-L172) — ลงทะเบียน sink ข้างของเดิม
- แก้: Program.cs
  - `AddDtmsBatchedLog<UserSessionLogEntry>()` ต่อจาก :560
  - เพิ่ม `PartitionTarget("iam.UserSessionLog", "StartedAt", AdvanceMonths: 2, RetainMonths: 13)` ใน array ที่ :567-575

### 3.3 เขียนแถวเมื่อการเชื่อมต่อจบ (~1 ชม.)

ไฟล์:
- แก้: `PresenceHub.OnDisconnectedAsync` — ถ้า `store.Disconnect(...)` ไม่เป็น null ให้ `writer.Enqueue(new UserSessionLogEntry(..., EndReason: "disconnect"))`
  - `Enqueue` คืน `bool` ([IBatchedLogWriter.cs:25](../../src/DTMS.SharedKernel/Logging/IBatchedLogWriter.cs#L25)) ถ้าได้ false ให้ log ระดับ debug
- แก้: `PresenceBroadcaster` — override `StopAsync`: `store.DrainAll(now)` → enqueue ทุกแถวด้วย `EndReason: "shutdown"` แล้วค่อยเรียก `base.StopAsync`
  - hosted service ถูกหยุดในลำดับย้อนกลับจากตอนลงทะเบียน ดังนั้น `AddDtmsBatchedLog` ต้องถูกเรียก**ก่อน**ลงทะเบียน broadcaster เพื่อให้ drain service หยุดทีหลังและ flush ทัน — ให้ยืนยันลำดับใน Program.cs ตอนทำ
- เทสต์: เคส `DrainAll` ที่เพิ่มไว้ใน 1.1 ครอบคลุมเรื่องห้ามมีแถวซ้ำแล้ว

### Phase 3 acceptance

- `dotnet test tests/DTMS.Api.UnitTests -m:1` ผ่าน
- `ops\podman-rebuild.ps1 -Only api` (migration รันผ่าน `dtms-migrator` ถ้า migration พัง จะเห็น exit 139)
- เปิด/ปิดแท็บ 2 ครั้ง รอ 5 วินาที (MaxWait ของ writer) แล้ว:
  ```
  psql -h localhost -p 5434 -U <user> -d <db> -c 'SELECT "UserId","Client","StartedAt","EndedAt","ActiveMinutes","EndReason" FROM iam."UserSessionLog" ORDER BY "EndedAt" DESC LIMIT 5'
  ```
  ต้องได้ 2 แถว `disconnect`
  - ⚠️ PowerShell ตัด quote ใน `psql -c` ทิ้ง ให้รันจาก Git Bash
- restart เฉพาะ API (เปิดแท็บค้างไว้) → มีแถว `shutdown` และ**ไม่มี**แถว `disconnect` ซ้ำของ connection เดียวกัน
- `SELECT tablename FROM pg_tables WHERE schemaname='iam' AND tablename LIKE 'UserSessionLog_%'` ได้ 4 partition

**Commit**: `feat(api): keep a history of who used dtms and for how long`

---

## Phase 4 — แท็บรายงาน Usage (~4 ชม.)

### 4.1 Query + aggregator (~2 ชม.)

ไฟล์:
- ใหม่: `src/Modules/Iam/DTMS.Iam.Application/Reports/UserActivityReport.cs`
  - DTO + `public static UserActivityReport Aggregate(IReadOnlyList<UserSessionLogEntry> rows, DateTime fromUtc, DateTime toUtc)` เป็น pure function
  - แถวรายคน: UserId, DisplayName (ค่าล่าสุด), DaysActive, Sessions, ConnectedHours, ActiveHours (= ActiveMinutes/60), LastSeenUtc, Clients
  - ซีรีส์รายวัน: วันที่ (UTC+7), DAU, ActiveHours
  - session ที่คร่อมเที่ยงคืน: นับเข้าวันที่**เริ่ม**ทั้งก้อน (เรียบง่าย; ระบุไว้ในคำอธิบายใต้หัวข้อของรายงาน)
- ใหม่: `IUserSessionLogReader` ใน `DTMS.Iam.Application/Repositories/` + `UserSessionLogReader` ใน `DTMS.Iam.Infrastructure/Repositories/` (copy [AuditLogRepository.cs](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Repositories/AuditLogRepository.cs)) — `AsNoTracking().Where(StartedAt in window)` แล้วรวมใน memory (ประเมินไว้ที่หลักหมื่นแถวต่อ 90 วัน)
  - ลงทะเบียนข้าง [ModuleServiceRegistration.cs:131](../../src/DTMS.Api/Modules/ModuleServiceRegistration.cs#L131)
- เทสต์: `tests/DTMS.Api.UnitTests/UserActivityReportTests.cs` (ไม่มีโปรเจกต์เทสต์ของ Iam; Api.UnitTests เทสต์คลาสของ Iam อยู่แล้ว เช่น `CallbackTokenProtectorTests`)
  - user คนเดียว 2 แท็บ → DAU = 1
  - session 23:30–00:30 เวลาไทย → ลงวันแรก
  - ช่วงเวลาว่าง → ได้ rows ว่าง ไม่ throw

### 4.2 Endpoint (~45 นาที)

- ใหม่: `src/Modules/Iam/DTMS.Iam.Presentation/IamReportsEndpoints.cs`
  - `MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization()` (DeliveryOrder และ Planning ก็ map group นี้ซ้ำแบบนี้)
  - `GET /user-activity?fromUtc&toUtc` + `.RequirePermission(Permissions.Reporting.ReportRead)`
  - copy `ResolveWindow` (8 บรรทัด, [DispatchReportsEndpoints.cs:67-74](../../src/Modules/Dispatch/DTMS.Dispatch.Presentation/DispatchReportsEndpoints.cs#L67-L74)) และจำกัดช่วงไม่เกิน 90 วันเหมือนรายงานหุ่น
- แก้: ModuleEndpointRegistration.cs — `app.MapIamReportsEndpoints();` ข้าง :51

### 4.3 Frontend (~1.25 ชม.)

- ใหม่: `frontend/app/api/reports/user-activity/route.ts` (copy `app/api/reports/manual-operators/route.ts`)
- แก้: [lib/api/reports.ts](../../frontend/lib/api/reports.ts) — `getUserActivityReport` (ต้นแบบ :197-203)
- ใหม่: `frontend/components/reports/user-activity-report.tsx` (copy [manual-operators-report.tsx](../../frontend/components/reports/manual-operators-report.tsx)) — กราฟเส้น DAU รายวัน + ตารางรายคน
- แก้: [reports-experience.tsx](../../frontend/components/reports/reports-experience.tsx)
  - เพิ่ม `"usage"` ใน `TabKey` (:23)
  - เพิ่มรายการใน `TABS` (:25-33)
  - เพิ่ม branch การ render (:75-81)

### Phase 4 acceptance

- `dotnet test tests/DTMS.Api.UnitTests -m:1` และ `cd frontend && npm run typecheck` ผ่าน
- หลัง rebuild: Reports → Usage แสดงแถวของตัวเอง โดย Sessions ตรงกับ `SELECT count(*) FROM iam."UserSessionLog" WHERE "UserId"='<id>'` ในช่วงเดียวกัน

**Commit**: `feat(reports): see daily active users and per-person usage in a new usage tab`

---

## Phase 5 — ส่ง heartbeat ไป session service กลาง (~3.5 ชม.) — ⛔ บล็อกที่สัญญาของ service กลาง

### 5.1 เก็บ JWT ของ connection ใน memory (~45 นาที)

- แก้: `IPresenceStore.Connect` — รับ `string? accessToken, DateTime? tokenExpiresUtc`
  - token มาจาก `Context.GetHttpContext()?.Request.Query["access_token"]` ซึ่งเป็นค่าเดียวกับที่ JwtBearer ใช้ยืนยันตัว ([Program.cs:226-231](../../src/DTMS.Api/Program.cs#L226-L231))
  - exp อ่านจาก claim `exp` ของ `Context.User`
- เพิ่ม `IReadOnlyList<(string UserId, string Token)> ActiveUserTokens(DateTime nowUtc)` — 1 token ต่อ user (ตัวที่ exp ไกลที่สุด ไม่หมดอายุ และไม่ถูกทำเครื่องหมายว่าใช้ไม่ได้) เฉพาะ user ที่ active
- เพิ่ม `MarkTokenRejected(string token)`
- **ห้าม log หรือเก็บ token ลง DB/Redis** และ `PresenceUserView` ต้องไม่มี token
- เทสต์: เพิ่มใน `InMemoryPresenceStoreTests.cs`
  - 2 แท็บ → ได้ 1 token
  - token หมดอายุ → ไม่ถูกเลือก
  - idle → ไม่ถูกเลือก
  - rejected → ไม่ถูกเลือก

### 5.2 Heartbeat service (~2 ชม.)

- ใหม่: `src/DTMS.Api/Realtime/Presence/CentralSessionOptions.cs`
  - bind section `CentralSession` (env: `CentralSession__Enabled` (false), `CentralSession__BaseUrl`, `CentralSession__ServiceName` ("DTMS"), `CentralSession__IntervalSeconds` (30))
  - รูปแบบ binding เหมือน `RateLimitOptions` ([RateLimitOptions.cs:5](../../src/DTMS.Api/Infrastructure/RateLimiting/RateLimitOptions.cs))
- ใหม่: `src/DTMS.Api/Realtime/Presence/CentralSessionHeartbeatService.cs` (`BackgroundService`)
  - ทุก interval ดึง `ActiveUserTokens`
  - ยิง `POST {BaseUrl}/session/heartbeat` พร้อม `Authorization: Bearer` และ body `{ service }` ผ่าน named `HttpClient` (timeout 5 วินาที) ทำพร้อมกันได้สูงสุด 4 ตัวด้วย `Parallel.ForEachAsync`
  - 401 → `MarkTokenRejected` โดยไม่ตัดผู้ใช้ออกจาก DTMS
  - error อื่นนับ metric แล้ว log warning ไม่เกิน 1 ครั้ง/นาที
  - ถ้า `Enabled=false` ไม่ลงทะเบียน service เลย (รูปแบบ flag `RunInThisProcess` ที่ [Program.cs:611-615](../../src/DTMS.Api/Program.cs#L611-L615))
- แก้: Program.cs — ลงทะเบียนตาม flag
- แก้: `.env.example` — เพิ่ม 4 ตัวแปรพร้อมคอมเมนต์
- เทสต์: `tests/DTMS.Api.UnitTests/CentralSessionHeartbeatServiceTests.cs` ใช้ `HttpMessageHandler` ปลอม (มีต้นแบบใน `WmsBearerTokenHandlerTests.cs` และ `TokenRefreshTests.cs`)
  - body และ header ถูก
  - 401 → token ถูก mark
  - 2 แท็บ → ยิง 1 ครั้ง

### 5.3 การต่อเครือข่าย (~30 นาที)

- API container ต้องออกไปถึง base URL ได้: frontend เรียก `10.204.212.28:15000` ได้อยู่แล้ว (login) แต่**ยังไม่ได้ยืนยัน**ว่า API container ออกไปถึง host นั้นได้
- ตรวจด้วย `podman exec dtms-api curl -s -o /dev/null -w "%{http_code}" <BaseUrl>/session/heartbeat` (ต้องได้ 401/405 ไม่ใช่ timeout)

### Phase 5 acceptance

- เทสต์ผ่าน
- ตั้ง `CentralSession__Enabled=true` แล้ว restart → log ไม่มี warning ของ heartbeat
- ฝั่ง service กลาง `/session/online-users?service=DTMS` เห็นชื่อตัวเองภายใน 1 interval
- ปล่อยไว้ idle 2 นาที → หายไปตาม TTL ของฝั่งนั้น

**Commit**: `feat(api): report active dtms users to the central session service`

---

## การตรวจสอบ

| อะไร | ทำยังไง | ที่ไหน |
|---|---|---|
| ตรรกะของ store/รายงาน/heartbeat | `dotnet test tests/DTMS.Api.UnitTests -m:1` | DTMS.Api.UnitTests |
| permission code ถูกไวยากรณ์ | `dotnet test tests/DTMS.ArchitectureTests -m:1` | ArchitectureTests |
| frontend | `cd frontend && npm run typecheck && npm run check:routes` | — |
| ไม่กิน rate-limit | หลังเฟส 2: ไม่มี request ใหม่ที่ไม่ใช่ `/hubs` ใน `WindowUsageTracker` (partition peak ไม่ขยับเทียบก่อน deploy) | metric ของ rate limiter / `ops/prometheus/rules/rate-limit.yml` |
| production สัปดาห์หน้า | `SELECT date_trunc('day', "StartedAt" + interval '7 hours'), count(DISTINCT "UserId") FROM iam."UserSessionLog" GROUP BY 1 ORDER BY 1 DESC LIMIT 7` ได้ตัวเลขที่สมเหตุสมผลทุกวัน และมีแถว `shutdown` ทุกครั้งที่ deploy | psql :5434 |

## เอกสารที่ต้องอัปเดตเมื่อเสร็จ

- `.env.example` — ตัวแปร `CentralSession__*` (เฟส 5)
- ADR ใหม่? **ไม่** — ไม่มีการตัดสินใจระดับสถาปัตยกรรมใหม่ (ใช้ hub/batched-log/partition ที่มีอยู่) ยกเว้นเฟส 5 ถ้าตกลงให้ DTMS ถือ JWT ของผู้ใช้ไว้ใน memory เพื่อส่งต่อ ซึ่งควรเขียนเป็น ADR-020 สั้นๆ

## Definition of done

- [ ] pill ตรงกับจำนวนคนจริงในการทดสอบ 2 บัญชี และลดลงทันทีเมื่อ logout
- [ ] ไม่มี HTTP polling ใหม่จาก presence (ตรวจใน DevTools)
- [ ] แต่ละการเชื่อมต่อได้ 1 แถวใน `iam.UserSessionLog` รวมกรณี restart (ไม่มีซ้ำ)
- [ ] แท็บ Usage ตรงกับ SQL count
- [ ] (เฟส 5) service กลางเห็นผู้ใช้ DTMS
- [ ] เวลารวมไม่เกิน ~21 ชม. (เกิน 50% = หยุดแล้ว re-scope)

## ทางลง (off-ramps)

- **ถ้า 2.1 (activity detector) ยุ่งเกิน 3 ชม.**: ส่งแค่ "online" (นับ connection) ไม่มี "active" ตัด `Active()` และ `ActiveMinutes` ออก ยังได้ (ก) 80%
- **ถ้า `StopAsync` flush ไม่ทันจริงตอน restart**: ยอมเสีย session ที่เปิดอยู่ตอน deploy (ระบุใน description ของรายงาน) ไม่ต้องสร้างระบบ recovery
- **ถ้าเฟส 5 ยังไม่ได้คำตอบจาก service กลาง**: ship เฟส 1–4 ได้เลย เฟส 5 ไม่มีอะไรมาพึ่งพา และส่วนที่เหลือได้คุณค่า ~85% ของคำขอ
- **ถ้าต้องรันหลาย replica ก่อนเสร็จ**: ทำ `RedisPresenceStore` เป็นแผนแยก (ต้องมีตัวกวาด conn ของ pod ที่ตาย) อย่าแทรกกลางทาง

## ความเสี่ยง & การย้อนกลับ

| ความเสี่ยง | โอกาส | ป้องกัน / ย้อนกลับ |
|---|---|---|
| ผู้ใช้ที่ logout ยังค้างเป็นออนไลน์ (socket singleton) | กลาง | `disposeHub` ใน 2.1; acceptance ข้อ logout |
| `OnDisconnectedAsync` ไม่ถูกเรียกตอน restart → แถว `shutdown` หาย | กลาง | `DrainAll` ใน `StopAsync`; ถ้าล้มเหลวใช้ off-ramp |
| migration พังกลางทาง | ต่ำ | Down = `DROP TABLE ... CASCADE`; ตารางใหม่ ไม่กระทบข้อมูลเดิม |
| JWT ของผู้ใช้ถูกถือใน memory ของ API (เฟส 5) | กลาง | ไม่ log ไม่ persist ไม่ส่งออกใน view; ปิดโดย default; เลิกใช้เมื่อ service กลางมี service credential |
| service กลางช้า/ล่ม | กลาง | timeout 5 วินาที, ทำพร้อมกันได้ 4 ตัว, ไม่ retry ภายใน tick, ไม่กระทบผู้ใช้ DTMS |
| รายชื่อคนออนไลน์หลุดถึงคนไม่มีสิทธิ์ | ต่ำ | broadcast ส่งแค่ตัวเลข รายชื่อต้องผ่าน `PresenceRead` |

ย้อนกลับทั้งหมด: revert commit ของเฟสนั้นๆ (เฟส 3 ต้อง rollback migration ด้วย Down)

## สิ่งที่จะทำให้แผนนี้ใช้ไม่ได้

- API ต้องรันหลาย replica ในระยะสั้น (ย้าย Linux server) → store ต้องเป็น Redis ตั้งแต่เฟส 1
- service กลางต้องการให้ browser เรียกตรงด้วย token ของตัวเอง (ไม่รับ call จาก server) → เฟส 5 ต้องออกแบบใหม่

## Later, not now

- `RedisPresenceStore` สำหรับหลาย replica
- แสดงคนออนไลน์รวมข้ามแอปจาก `/session/online-users`
- ตัวบ่งชี้ presence ใน OperatorShellHeader ของ PWA

## เมื่อหยิบแผนนี้มาทำ

**First move**: สร้าง `src/DTMS.Api/Realtime/Presence/IPresenceStore.cs` ด้วย record และ interface ตาม 1.1 แล้วเขียน `InMemoryPresenceStoreTests` เคส "2 conn ของ user เดียวกัน → OnlineUsers == 1" ก่อนทำ store

ส่วนเตรียมตัวครอบคลุมทุกไฟล์ที่ต้องใช้ จากเริ่มเย็นจนเสร็จเฟส 1–4 ประมาณ 17.5 ชม. (3 วันทำงาน) เฟส 5 อีก ~3.5 ชม. เมื่อได้สัญญาของ service กลาง
