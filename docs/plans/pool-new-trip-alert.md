# แผน: เตือนงานใหม่เข้า Pool ภายในแอป (เสียง + สั่น + แถบเตือน + กันจอดับ)

> **สถานะ**: 🟡 Phase 1 ✅ (`c38ff16d`) · Phase 2 ✅ (`2c01dc95`, `f9933044`) · Phase 3–4 ปรับแผนแล้ว (ดู "ฉบับแก้ 2026-10-14" ด้านล่าง) — ร่างแรก 2026-10-08
> **สิ่งที่ขอ**: operator ที่ถือ tablet ที่เปิดแอป PWA ไว้ ได้ยินเสียง รู้สึกถึงการสั่น และเห็นแถบ "งานใหม่" เมื่อมีงานเข้า Pool โดยไม่ต้องใช้อินเทอร์เน็ต
> **ประเภท / ขนาด**: feature · ขนาด M ประมาณ 9.5 ชม. 4 phase แก้เฉพาะ frontend
> **ที่มา**: tablet อยู่บน Wi-Fi "Delta-IoT" ที่ไม่มีอินเทอร์เน็ต Web Push (FCM ของ Google) จึงใช้ไม่ได้ (`AbortError: push service error`)
> **ตกลงกับผู้ใช้แล้ว (2026-10-08)**:
> - **เสียง:** ดังครั้งเดียวต่อชุดงาน เป็นเสียง alert ดังชัดยาวประมาณ 1.5 วินาที
> - **เตือนเสมอ:** แม้ operator กำลังทำ trip ของตัวเองอยู่
> - **กันจอดับ:** มีสวิตช์ใน Settings ค่าเริ่มต้นเปิด
> - **ดึงข้อมูลใหม่เมื่อกลับมาเปิดจอ:** แล้วเตือนงานที่เข้ามาระหว่างจอดับ
> - **realtime ต่อไม่ได้:** ทำทั้ง poll สำรอง และแก้ต้นเหตุ M15 (ลองต่อใหม่เมื่อต่อครั้งแรกไม่ได้)
> **ที่เก็บแผน**: ไฟล์นี้ (`docs/plans/pool-new-trip-alert.md`)

## ฉบับแก้ 2026-10-14 (ใช้แทนส่วนที่ขัดกันใน Phase 3–4 ด้านล่าง)

**สิ่งที่เปลี่ยนไปตั้งแต่ร่างแรก**
- SignalR Redis backplane เปิดแล้ว (`6856ed1d`) ทำให้ `PoolTripAdded` ถึง tablet ครบทุกงาน ก่อนหน้านี้หายประมาณครึ่งหนึ่ง
- `PoolTripRemoved` ถูกส่งจริงแล้ว (`b6344d40`, `PoolTripRemovedBroadcastConsumer`) งานที่ถูกยกเลิกจะหายจาก Pool เอง

**ตัดสินใจกับผู้ใช้เพิ่ม (2026-10-14)**
- **ตัวเลขบนแท็บ Pool = จำนวนงานรอใน Pool ทั้งหมด** (`trips.length`) ไม่ใช่ "งานใหม่ที่ยังไม่ดู" จึงถูกต้องเสมอโดยไม่ต้องมีตัวนับแยก (แก้ V1)
- **เปิดแอปหรือ reload:** ไม่ดัง แค่แสดงตัวเลข
- **กันจอดับ:** เปิดเป็นค่าเริ่มต้น โดยยอมรับว่าเครื่องจะไม่ล็อกหน้าจอระหว่างเปิดแอป และปิดได้ต่อเครื่อง
- **เพิ่มการเตือน "trip ของคุณถูกยกเลิก"** โดยให้ server ส่งทันทีเฉพาะ operator คนนั้น (Phase 3b)

**ปรับการออกแบบหลังรีวิว**
- **V2:** ตรวจ "งานใหม่" จากการเปลี่ยนแปลงของรายการ `trips` ใน provider ที่เดียว ไม่ผูกกับแหล่งข้อมูล (event, ดึงสำรอง หรือดึงตอนเปิดจอ) มี id ที่ไม่เคยเห็นก็ดัง (รวมเสียง 3 วินาที) และ `seen` จำแยกจากรายการ (R4)
- **V3/V4:** กันหลายแท็บดังซ้อนด้วย `navigator.locks` (ใช้ localStorage แทนถ้าไม่รองรับ) และดังเฉพาะแท็บที่แสดงอยู่
- **V5:** ดึงรายการใหม่ทุกครั้งที่จอกลับมา (`visibilitychange`) ไม่ขึ้นกับ `connected` แต่ห่างกันอย่างน้อย 5 วินาที
- **I1:** แยกตัวกลาง `useOperatorAlerts` (ประเภท `pool-new`, `trip-ended`) ใช้เสียง การสั่น และแถบร่วมกัน
- **I2:** ค่าตั้งทั้งหมดอยู่ในไฟล์ `alert-config.ts`
- **I3:** แถบเตือนใช้ `role="status"` (หรือ `alert` สำหรับ trip ถูกยกเลิก)

### Phase 3b — เตือน "trip ของคุณถูกยกเลิก" (backend + frontend, ~1.5–2 ชม.)
- **Hub:** `OperatorPoolHub.OnConnectedAsync` ให้เข้ากลุ่ม `operator:{EmployeeCode}` เพิ่ม อ่านรหัสจาก claim ด้วยลำดับเดียวกับ `OperatorSyncMiddleware` (`EmployeeId` → `employeeCode` → `sub` → NameIdentifier) แยกเป็นตัวช่วยที่ใช้ร่วมกัน ไม่ต้อง query ฐานข้อมูลตอนเชื่อมต่อ และ `Unsubscribe` ถอดเฉพาะกลุ่ม pool ไม่แตะกลุ่ม operator
- **Consumer:** ขยาย `PoolTripRemovedBroadcastConsumer` (**ห้ามเปลี่ยนชื่อคลาส** เพราะชื่อคลาสคือชื่อคิว ถ้าเปลี่ยน คิวเดิมจะค้างโดยไม่มีตัวรับ):
  - ถ้า trip อยู่ใน Pool → `PoolTripRemoved` (เหมือนเดิม)
  - ถ้า trip มีคนรับแล้ว (`ClaimedByOperatorId` ไม่ว่าง) → โหลด operator → ส่ง `AssignedTripEnded { tripId, reason, orderRef }` ไปที่ `operator:{EmployeeCode}`
- **Broadcaster:** เพิ่ม `BroadcastAssignedTripEndedAsync` ใน `IOperatorPoolBroadcaster` และ `IOperatorPoolClient`
- **Frontend:** provider ฟัง `AssignedTripEnded` แล้วส่งให้ `useOperatorAlerts` (ประเภท `trip-ended`): ดังเสียงต่างจากงานใหม่ (ต่ำลง) และขึ้นแถบแดง "trip ของคุณถูกยกเลิก · {orderRef}" แตะแล้วไปหน้า `/m/trips/{id}`
- **Test:** consumer ส่งถึง operator ที่รับงานเท่านั้น และไม่ส่งเมื่อ trip จบตามปกติ (Completed ไม่ใช่ event ที่ consumer นี้ฟัง)

**ลำดับ commit:** Phase 3 (frontend) → Phase 3b (backend + frontend) → Phase 4 (frontend) ประมาณ 8 ชม. รวม

## สรุปการเปลี่ยนแปลง

เมื่อ trip แบบ Manual ถูก dispatch เข้า Pool tablet ทุกเครื่องที่เปิด PWA อยู่ (หน้า Pool, My trips, Trip detail หรือ Settings) จะ:
- ส่งเสียง alert และสั่น
- แสดงแถบ "งานใหม่ใน Pool" ที่แตะได้
- แสดงตัวเลขบนแท็บ Pool

**จอไม่ดับเอง** ระหว่างเปิดแอป และ**ถ้าจอเพิ่งกลับมาเปิด** หรือ realtime เพิ่งต่อกลับได้ ก็เตือนงานที่เข้ามาระหว่างนั้นด้วย

## เป้าหมาย / สิ่งที่ไม่ทำ

**เป้าหมาย**
- มีงานใหม่แล้วเตือนภายในประมาณ 1 วินาทีเมื่อ realtime ต่ออยู่ และช้าสุด 30 วินาทีเมื่อ realtime ต่อไม่ได้
- งานที่เข้าติดกันภายใน 3 วินาทีดังครั้งเดียว และเปิดหลายแท็บก็ดังครั้งเดียว
- Settings มีสวิตช์เสียงเตือน, ปุ่มทดสอบ และสวิตช์กันจอดับ ตั้งแยกได้ต่อเครื่อง
- realtime ทุกหน้าของระบบ (รวมหน้า admin) ลองต่อใหม่เองเมื่อต่อครั้งแรกไม่ได้ (แก้ M15)

**สิ่งที่ไม่ทำ**
- **เตือนตอนปิดแอปหรือปิด Chrome:** ต้องใช้ Web Push ซึ่งต้องมีอินเทอร์เน็ต หรือต้องทำแอป native
- **เสียงดังซ้ำจนกว่าจะกดรับรู้:** ผู้ใช้เลือกดังครั้งเดียว
- **แก้ backend:** ไม่ต้อง เพราะ hub ส่ง `PoolTripAdded` อยู่แล้ว
- **แก้ URL ของ hub ที่ฝังตอน build (`NEXT_PUBLIC_DTMS_HUBS_URL`):** เป็นเรื่องตั้งค่า ถ้าตั้งผิด poll สำรองจะยังเตือนได้

---

## เตรียมก่อนเริ่ม (~25 นาที)

ไฟล์ที่ต้องอ่าน:
- [frontend/lib/realtime/signalr-client.ts](frontend/lib/realtime/signalr-client.ts) (177 บรรทัด) แบ่งเป็น:
  - connection เดียวต่อ hub (:31-37, :39-41)
  - `ready = connection.start().catch(...)` ถ้าครั้งแรกล้ม connection จะค้างอยู่สถานะ Disconnected ตลอดไป ต้นเหตุของ M15 (:117-123)
  - `withAutomaticReconnect` ทำงานเฉพาะหลังเคยต่อสำเร็จแล้ว (:75-81)
  - drain cycle ที่ restart ไม่สำเร็จก็จะค้างเช่นกัน แม้ comment บอกว่า auto-reconnect จะจัดการให้ (:128-148)
- [frontend/lib/hooks/use-hub-subscription.ts](frontend/lib/hooks/use-hub-subscription.ts) แบ่งเป็น:
  - `await ensureStarted` แล้วค่อย attach และ subscribe (:156-185)
  - ฟัง `DRAIN_CYCLE_EVENT` แล้ว subscribe ใหม่ (:164-177)
  - ตอน unmount เรียก `Unsubscribe` (:194-197)
  - มี option `enabled` (:76, :114)
- [frontend/lib/hooks/use-pool-trips.ts](frontend/lib/hooks/use-pool-trips.ts) (121 บรรทัด) จะกลายเป็น provider ส่วน refetch ที่ล้มเหลวตอนนี้ล้างรายการเป็นว่าง (:66-68)
- [src/DTMS.Api/Realtime/Hubs/OperatorPoolHub.cs](src/DTMS.Api/Realtime/Hubs/OperatorPoolHub.cs): `Unsubscribe` ถอดทั้ง connection ออกจากกลุ่ม (:46)
- [frontend/app/m/layout.tsx](frontend/app/m/layout.tsx) ครอบทุกหน้า `/m` รวม `/m/login`
- [operator-shell-header.tsx:68](frontend/components/operator-pwa/operator-shell-header.tsx#L68) header เป็น `sticky top-0 z-10`
- [pool-trips-list.tsx](frontend/components/operator-pwa/pool-trips-list.tsx), [operator-tabs-nav.tsx](frontend/components/operator-pwa/operator-tabs-nav.tsx), [settings-panel.tsx](frontend/components/operator-pwa/settings-panel.tsx)

**ผู้ใช้ hub ทั้งหมด 7 ตัว** (โดนผลจากการแก้ M15):
- `lib/realtime/hubs/{dashboard,fleet,job,order,trip}-hub.ts`
- `components/dispatch/manual/manual-operator-board.tsx`
- `lib/hooks/use-pool-trips.ts`

ไม่มีหน้าไหนนำ `error` ของ hub ไปแสดง (`ConnectionIndicator` ไม่ได้ถูกใช้ที่ไหน)

| เรื่องที่ตัดสินใจ | ค่าที่เลือก | เหตุผล |
|---|---|---|
| ตัวรับ event ของ pool | `OperatorPoolProvider` ตัวเดียวใน `app/m/layout.tsx` | ถ้ามีสองตัว `Unsubscribe` จะทำให้อีกตัวไม่ได้รับ event (R1) |
| เสียง | สร้างด้วย Web Audio: คลื่น square 3 ช่วงสลับ 1000/1400 Hz ช่วงละประมาณ 0.45 วินาที รวมประมาณ 1.5 วินาที gain 0.8 | ดังชัดในโรงงาน ไม่ต้องมีไฟล์เสียง ใช้ offline ได้ |
| ปลดล็อกเสียง | resume `AudioContext` ตอนแตะจอครั้งแรก ถ้ายังถูกล็อกให้แถบเตือนบอก "แตะเพื่อเปิดเสียง" | Chrome ห้ามเล่นเสียงก่อนผู้ใช้แตะจอ |
| กันจอดับ | `navigator.wakeLock.request("screen")` ขอใหม่ทุกครั้งที่หน้ากลับมาแสดง (`visibilitychange`) | Chrome ปล่อย lock เองเมื่อหน้าถูกซ่อน |
| การแก้ M15 | ลูป `startWithRetry` ใน signalr-client ใช้ backoff เดียวกับ reconnect (1s → 30s + jitter) ส่วน `ready` resolve เมื่อต่อสำเร็จครั้งแรกและไม่ reject | ซ่อมที่จุดเดียว ทุก hub ได้ประโยชน์ |
| ค่าตั้งต่อเครื่อง | `localStorage`: `operator:pool-alert` และ `operator:keep-awake` (`"on"`/`"off"` ค่าเริ่มต้น on) อ่านใน `useEffect` เท่านั้น | กัน hydration mismatch (R8) |

**ฐานในการประเมินเวลา**: `df93120b` สร้างหน้า Pool และ hub แก้ 15 ไฟล์ 966 บรรทัด งานนี้แก้ประมาณ 12 ไฟล์ 500 บรรทัด และเป็น frontend อย่างเดียว

---

## รีวิวผลกระทบ: จุดที่อาจเกิด bug และวิธีแก้ที่อยู่ในแผนแล้ว

| # | จุดเสี่ยง | สิ่งที่จะเกิดถ้าไม่แก้ | วิธีแก้ (phase) |
|---|---|---|---|
| R1 | มีตัวรับ event ของ pool สองตัว | ออกจากหน้า Pool แล้วตัวเตือนไม่ได้รับ event เงียบๆ | ตัวรับตัวเดียวใน provider และ `usePoolTrips` throw ถ้าเรียกนอก provider (P2) |
| R2 | realtime ต่อไม่ได้ตั้งแต่แรก (M15) หรือ URL ของ hub ผิด | operator คิดว่ามีระบบเตือน แต่ไม่มีอะไรดัง | แก้ M15 (P1) และ poll สำรองทุก 30 วินาทีระหว่าง `!connected` (P3) |
| R3 | refetch ที่ล้มเหลวล้างรายการเป็นว่าง ([use-pool-trips.ts:66-68](frontend/lib/hooks/use-pool-trips.ts#L66-L68)) | หน้า Pool กะพริบว่างทุกครั้งที่ poll ล้ม | คงรายการเดิมไว้และเก็บ error (P2) |
| R4 | คำนวณ "งานใหม่" จากรายการปัจจุบัน | หลังรายการว่างชั่วคราว งานเดิมทุกงานดังซ้ำ | ใช้ set `seen` แยกต่างหาก และ refetch ที่ล้มไม่ไปแตะ `seen` (P3 พร้อมเทสต์) |
| R5 | แถบเตือนดันเนื้อหาลง | ปุ่ม Record pickup หรือ Complete เลื่อนตอนกำลังจะกด ทำให้กดผิดปุ่ม | แถบเป็น `fixed` ลอยทับบริเวณ header มี ✕ และไม่วางด้านล่าง (P3) |
| R6 | เปิดหลายแท็บ (tablet ทดสอบเปิดไว้ 13 แท็บ) | ดังซ้อนหลายเสียง | `claimChimeSlot()` ใช้ timestamp ใน localStorage ร่วมกันทุกแท็บ ส่วน Wake Lock ทำงานเฉพาะแท็บที่แสดงอยู่ (P3, P4) |
| R7 | provider ทำงานในหน้า login | ยิง hub-token และ pool แล้วได้ 401 วนไป | `enabled` ปิด hub, REST, poll และ visibility refetch ในหน้า `/m/login` (P2) |
| R8 | อ่าน localStorage ตอน render | hydration mismatch | อ่านใน `useEffect` เท่านั้น (P3, P4) |
| R9 | `OperatorTabsNav` ถูกใช้นอก provider ในอนาคต | หน้าพัง | `usePoolAlert()` คืน `null` นอก provider (P3) |
| R10 | เทสต์ import ด้วย alias `@/` | `node --test` รันไม่ได้ ([layer-stack.test.ts:5](frontend/lib/overlay/layer-stack.test.ts#L5)) | โมดูล logic ไม่มี import และเทสต์ import ด้วย `./x.ts` (P1, P3) |
| R11 | trip id เดิมกลับเข้า Pool | ไม่ดัง | ADDED ดังเมื่อ trip ไม่อยู่ในรายการ ณ ตอนนั้น ส่วน `seen` ใช้กับ refetch เท่านั้น (P3) |
| R12 | ปลดล็อกเสียงหรือการสั่นไม่ได้ / เครื่องไม่มีมอเตอร์สั่น | เงียบหลัง reload | ข้อความ "แตะเพื่อเปิดเสียง" และ `vibrate` ครอบ try/catch (P3) |
| R13 | `seen` โตไม่จำกัด | ใช้หน่วยความจำเพิ่มเล็กน้อย | จำกัด 500 id ล่าสุด (P3) |
| R14 | **การแก้ M15 กระทบทุก hub** | หน้า admin มีพฤติกรรม realtime เปลี่ยน | ต่อสำเร็จตั้งแต่แรกเหมือนเดิมทุกอย่าง ต่างเฉพาะกรณีล้มที่ตอนนี้จะลองใหม่แทนค้าง ทดสอบหน้า Orders, Trip drawer และ Dashboard (P1) |
| R15 | **`onclose` ถูกเรียกตอน drain cycle สั่ง `stop()` เอง** | start ซ้อนสองตัว ได้ error "Cannot start a HubConnection that is not in the Disconnected state" | flag `cycling` ต่อ entry ระหว่าง drain ทำให้ `onclose` ไม่สั่ง retry และลูปเช็ก `state === Disconnected` ก่อน start ทุกครั้ง (P1) |
| R16 | ต่อสำเร็จหลังลองใหม่แต่ไม่ได้ subscribe ใหม่ | ต่อติดแต่ไม่อยู่ในกลุ่ม จึงไม่ได้รับ event | หลัง restart ที่ไม่ใช่ครั้งแรก ส่ง `DRAIN_CYCLE_EVENT` ซึ่ง `use-hub-subscription` ฟังและ subscribe ใหม่อยู่แล้ว (:164-177) (P1) |
| R17 | ลองต่อใหม่ตลอดเมื่อ session หมดอายุ | ยิง hub-token ได้ 401 วนไป | backoff สูงสุด 30 วินาที คือ 2 request ต่อนาที ต่ำกว่า quota ต่อคน (600 ต่อนาที) มาก และหน้า `/m` จะ redirect ไป login ตอนโหลดหน้าถัดไปอยู่แล้ว (P1) |
| R18 | Wake Lock ทำให้แบตหมดเร็ว | แบตหมดกลางกะ | มีสวิตช์ปิดใน Settings ค่าเริ่มต้นเปิด และ lock ทำงานเฉพาะตอนหน้า `/m` แสดงอยู่ (P4) |
| R19 | เครื่องไม่รองรับ Wake Lock หรือไม่ใช่ secure context (ไม่ได้ตั้ง flag) | กดเปิดสวิตช์แล้วไม่มีผล | Settings แสดง "เครื่องนี้ไม่รองรับ" และครอบทุกอย่างด้วย try/catch (P4) |
| R20 | สามแหล่งข้อมูลทำงานพร้อมกัน (SignalR, poll และ refetch ตอนกลับมาเปิดจอ) | ดังซ้ำสำหรับงานเดียวกัน | ทุกเส้นทางผ่าน `seen` และรวมเสียง 3 วินาที มีเทสต์ (P3) |
| R21 | refetch ตอนกลับมาเปิดจอบ่อยเกินไป (สลับแอปไปมา) | request เยอะ | ไม่ refetch ถ้า refetch ครั้งก่อนห่างไม่ถึง 5 วินาที (P3) |

**สิ่งที่ตรวจแล้วว่าไม่กระทบ**
- **backend:** ไม่ได้แก้
- **dialog และ layer stack:** แถบเตือนใช้ `z-50` ซึ่งต่ำกว่า dialog (z 100 ขึ้นไป) และ toast (z-200)
- **คิว offline และ push:** ไม่เกี่ยวกัน
- **drain protocol (G1):** ใช้ event เดิม แต่แก้กรณี restart ล้มให้ลองใหม่จริง
- **จำนวน request:**
  - สลับแท็บแล้วไม่ refetch ใหม่ เพราะ provider ไม่ remount
  - poll ทำงานเฉพาะตอนต่อ realtime ไม่ได้
  - refetch ตอนกลับมาเปิดจอห่างกันอย่างน้อย 5 วินาที

---

## Phase 1 — ให้ realtime ลองต่อใหม่เมื่อต่อครั้งแรกไม่ได้ (แก้ M15, ~1.5 ชม.)

### 1.1 ลูปลองต่อใหม่แบบ pure พร้อมเทสต์ (~0.5 ชม.)
- **ใหม่:** `frontend/lib/realtime/start-with-retry.ts` ไม่มี import
  ```ts
  export function retryDelayMs(attempt: number, jitter: number): number; // min(1000·2^n, 30000) + jitter·1000
  export async function startWithRetry(opts: {
    start: () => Promise<void>;
    isDisconnected: () => boolean;   // เช็กก่อน start ทุกครั้ง (R15)
    shouldStop: () => boolean;       // เผื่อปิดในอนาคต
    sleep: (ms: number) => Promise<void>;
    random: () => number;
    onError?: (err: unknown, attempt: number) => void;
  }): Promise<void>; // resolve เมื่อ start สำเร็จ ไม่ reject
  ```
- **ใหม่:** `frontend/lib/realtime/start-with-retry.test.ts` ครอบคลุม:
  - ล้ม 2 ครั้งแล้วสำเร็จ: resolve และเรียก start 3 ครั้ง
  - delay เพิ่มตามลำดับ 1s, 2s, 4s ... และไม่เกิน 30s
  - ถ้า `isDisconnected()` เป็น false ไม่เรียก start
  - ถ้า `shouldStop()` ลูปจบ
- **แก้:** `frontend/package.json` เพิ่ม script `test:realtime`

### 1.2 ต่อเข้ากับ signalr-client (~1 ชม.)
- **แก้:** [signalr-client.ts](frontend/lib/realtime/signalr-client.ts)
  - `ConnectionEntry` เพิ่ม `cycling: boolean` และ `restarting: boolean`
  - `ready = startWithRetry({...})` แทน `connection.start().catch(...)` (:117-123) โดย log เตือนครั้งแรกที่ล้ม
  - `connection.onclose(...)`: ถ้าไม่ได้ `cycling` และไม่ได้ `restarting` ให้เรียก `startWithRetry` แล้วเมื่อสำเร็จส่ง `DRAIN_CYCLE_EVENT` (R16)
  - `cycleConnectionForDrain`: ตั้ง `cycling = true` ระหว่าง stop/start (R15) ถ้า start ล้มให้ไปเรียก `startWithRetry` แทน `return` แล้วส่ง event เมื่อสำเร็จ และแก้ comment ที่บอกว่า "automatic reconnect will pick up" ซึ่งไม่จริง
  - แก้ comment ของ `ConnectionEntry.ready` ให้ตรงกับพฤติกรรมใหม่: resolve เมื่อต่อสำเร็จครั้งแรก
- `use-hub-subscription.ts` ไม่ต้องแก้ เพราะ init รอ `ensureStarted` อยู่แล้ว (:157) และเช็ก `cancelled` หลัง await

### เกณฑ์ผ่าน Phase 1
- `npm run test:realtime` และ `npm run typecheck` ผ่าน
- **ทดสอบเอง:**
  1. หยุด container api
  2. เปิดหน้า Orders ของ admin ใน Console จะมี `[signalr] Failed to start` แค่ครั้งแรก
  3. start api กลับ
  4. ภายใน 30 วินาที ข้อมูลในหน้า Orders ต้องกลับมา update แบบ realtime (เช่น สร้าง order ใหม่แล้วโผล่เองโดยไม่ต้อง refresh)
- **ไม่ถอยหลัง:** หน้า Orders, Trip drawer, Dashboard และ Manual operator board update realtime ตามปกติเมื่อ api ทำงานตั้งแต่แรก

**Commit**: `fix(frontend): realtime pages recover when the hub is down at page load`

---

## Phase 2 — ย้ายตัวรับ event ของ Pool ไปไว้ที่ layout ของ `/m` (~2 ชม.)

- **ใหม่:** `frontend/components/operator-pwa/operator-pool-provider.tsx`
  - ย้าย reducer, `refetch`, `useHubSubscription` และ refetch ตอนต่อใหม่ ออกจาก `use-pool-trips.ts`
  - `enabled = !pathname?.startsWith("/m/login")` ใช้คุมทั้ง hub และ REST (R7)
  - context มีค่าเหมือนที่ hook คืนอยู่ตอนนี้ (`trips, loading, error, connected, refetch, optimisticRemove`)
  - ย้าย comment หัวไฟล์ (แหล่งข้อมูลจริง) มาที่นี่ และเพิ่ม comment เตือนเรื่อง R1
- **แก้:** `use-pool-trips.ts` เปลี่ยนเป็น `useContext` และ throw ถ้าเรียกนอก provider
- **แก้ R3:** เพิ่ม action `FAILED` ตอน refetch ล้ม ให้คงรายการเดิมไว้และเก็บ `error` แทนการส่งรายการว่าง
- **แก้:** `app/m/layout.tsx` ครอบ `{children}` ด้วย provider ภายใน `OverlayRoot`
- `pool-trips-list.tsx` ไม่ต้องแก้

### เกณฑ์ผ่าน Phase 2
- `npm run typecheck` ผ่าน
- หน้า Pool ทำงานเหมือนเดิม: รายการงาน, จุด Live, กดรับงาน และ toast 409
- สลับ Pool → My trips → Pool แล้วจุด Live ยังเขียวตลอด และไม่ refetch ทุกครั้งที่สลับ
- หน้า `/m/login` ไม่มี request ไป hub-token หรือ pool
- ปิด Wi-Fi สักครู่แล้วเปิดกลับ รายการ Pool ต้องไม่กลายเป็น "Pool is empty"

**Commit**: `refactor(operator): keep the pool feed alive across operator pages`

---

## Phase 3 — เสียง alert, สั่น, แถบเตือน, ตัวเลขบนแท็บ, poll สำรอง, refetch ตอนกลับมาเปิดจอ (~4 ชม.)

### 3.1 logic ตรวจจับงานใหม่แบบ pure พร้อมเทสต์ (~1 ชม.)
- **ใหม่:** `frontend/lib/operator-pwa/pool-alert-logic.ts` ไม่มี import (R10)
  ```ts
  export type AlertState = { seen: readonly string[]; unseenCount: number; lastChimeAt: number | null };
  export function onAdded(s: AlertState, tripId: string, inPoolNow: boolean, now: number): { state: AlertState; chime: boolean };
  export function onRefetch(s: AlertState, tripIds: string[], now: number, initial: boolean): { state: AlertState; chime: boolean };
  export function onRemoved(s: AlertState, tripId: string): AlertState;
  export function clear(s: AlertState): AlertState;
  export const COALESCE_MS = 3_000;
  export const SEEN_CAP = 500;
  ```
- **ใหม่:** `pool-alert-logic.test.ts` ครอบคลุม:
  - refetch ครั้งแรกไม่ดัง
  - ADDED ซ้ำไม่ดัง
  - งานที่สองภายใน 3 วินาทีเพิ่มตัวเลขแต่ไม่ดัง และงานหลัง 3 วินาทีดัง
  - refetch ที่มี id ใหม่ดัง
  - **R4:** refetch ได้รายการว่างแล้วตามด้วยรายการเดิม ไม่ดัง
  - **R11:** ADDED ของ id ที่เคยเห็นแต่ไม่อยู่ในรายการ ดัง
  - **R13:** จำไม่เกิน 500 id
  - **R20:** ADDED แล้วตามด้วย refetch ที่มี id เดียวกัน ไม่ดังซ้ำ
  - clear แล้วตัวเลขเป็น 0 แต่ยังจำ `seen`
- **แก้:** `package.json` เพิ่ม script `test:pwa`

### 3.2 เสียง, สั่น, ปลดล็อก และค่าตั้ง (~1 ชม.)
- **ใหม่:** `frontend/lib/operator-pwa/pool-alert.ts`
  - `unlockAudio()`
  - `playAlert()`: square 3 ช่วงสลับ 1000/1400 Hz รวมประมาณ 1.5 วินาที gain 0.8 มี attack/release สั้นกันเสียงคลิก
  - `vibrate()`: `[300,150,300,150,300]` ใน try/catch
  - `getAlertPref()/setAlertPref()`
  - `claimChimeSlot(now)` กันหลายแท็บดังซ้อน (R6) และคืน `true` ถ้า localStorage ใช้ไม่ได้

### 3.3 ต่อเข้ากับ provider (~1 ชม.)
- **การเตือน**
  - เรียก `onAdded/onRefetch/onRemoved`
  - เมื่อได้ `chime`, ค่าตั้งเปิดอยู่ และ `claimChimeSlot()` เป็นจริง ให้เรียก `playAlert()` และ `vibrate()`
- **หน้า `/m/pool`:** ดังและสั่นตามปกติ แต่ล้าง `unseenCount` ทันที
- **poll สำรอง (R2):** ระหว่าง `enabled && !connected` เรียก refetch ทุก 30 วินาที
- **refetch ตอนกลับมาเปิดจอ:** เมื่อ `visibilitychange` เป็น `visible` ให้ refetch ถ้าครั้งก่อนห่างเกิน 5 วินาที (R21)
- **ปลดล็อกเสียง:** ติดตั้งตัวรับ `pointerdown` ครั้งเดียวเพื่อเรียก `unlockAudio()`
- **context:** ส่ง `{ unseenCount, latestTrip, audioLocked, clearAlert }` และ `usePoolAlert()` คืน `null` นอก provider (R9)

### 3.4 แถบเตือนและตัวเลขบนแท็บ (~1 ชม.)
- **ใหม่:** `new-trip-banner.tsx`
  - `fixed` ลอยทับบริเวณ header ไม่ดันเนื้อหา (R5) และ `z-50`
  - แสดงทุกหน้ายกเว้น `/m/pool` และ `/m/login`
  - ข้อความ "งานใหม่ใน Pool (N) · <orderRef> <pickupCode> → <dropCode>" ใช้ field จาก [operator-pool.ts:10-20](frontend/lib/api/operator-pool.ts#L10-L20)
  - แตะแล้วไป `/m/pool` และปุ่ม ✕ ปิดแถบ
  - ถ้า `audioLocked` มีบรรทัดที่สอง "แตะเพื่อเปิดเสียง"
- **แก้:** `app/m/layout.tsx` render แถบภายใน provider
- **แก้:** `operator-tabs-nav.tsx` แสดงตัวเลขสีแดงบนแท็บ Pool

### เกณฑ์ผ่าน Phase 3
- `npm run test:pwa` และ `npm run typecheck` ผ่าน
- **อยู่หน้า `/m/trips` แล้ว dispatch งาน Manual:** ต้องดัง สั่น และขึ้นแถบภายในประมาณ 1 วินาที แตะแถบแล้วไปหน้า Pool และตัวเลขหายไป
- **dispatch 3 งานติดกัน:** ดังครั้งเดียว และแถบแสดงเลข 3
- **R5:** อยู่หน้า Trip detail ตอนแถบขึ้น ปุ่มต้องไม่เลื่อน
- **R6:** เปิด 2 แท็บแล้วได้ยินเสียงเดียว
- **R2:** ทดสอบบน Edge ของ PC เพราะ tablet ไม่มี DevTools (หยุด api ไม่ได้ เพราะจะ dispatch งานไม่ได้ด้วย)
  1. เปิด `/m/trips` ใน Edge แล้วใน DevTools → Network → Request blocking บล็อก `*:5219/*` (realtime) แล้ว reload หน้า
  2. dispatch งานจากหน้า admin ในอีกหน้าต่าง
  3. ต้องเตือนภายใน 30 วินาทีผ่าน poll สำรอง
- **กลับมาเปิดจอ:**
  1. ปิดจอ tablet
  2. dispatch งาน
  3. เปิดจอ ต้องดังและขึ้นแถบทันที

**Commit**: `feat(operator): sound an alert and show a banner when a new trip lands in the pool`

---

## Phase 4 — Settings: สวิตช์เสียงเตือน, ทดสอบเสียง, กันจอดับ (~2 ชม.)

- **ใหม่:** `frontend/lib/operator-pwa/keep-awake.ts`
  - `isWakeLockSupported()`
  - `getKeepAwakePref()/setKeepAwakePref()`
- **ใหม่:** `frontend/components/operator-pwa/keep-awake.tsx` (client) mount ใน layout ทำงานเมื่อ `enabled` และค่าตั้งเปิด
  - ขอ `navigator.wakeLock.request("screen")` ตอนหน้าแสดง และขอใหม่ทุกครั้งที่ `visibilitychange` เป็น `visible`
  - ปล่อย lock ตอนปิดค่าตั้งหรือ unmount
  - ครอบทุกอย่างด้วย try/catch (R19)
- **แก้:** `settings-panel.tsx` เพิ่มส่วน "การเตือนงานใหม่" เหนือส่วน Notifications
  - สวิตช์ "เสียงเตือนงานใหม่" และปุ่ม "ทดสอบเสียง" ซึ่งเรียก `unlockAudio(); playAlert(); vibrate()`
  - สวิตช์ "กันจอดับระหว่างเปิดแอป" ถ้าเครื่องไม่รองรับให้ปิดสวิตช์และแสดง "เครื่องนี้ไม่รองรับ"
  - ข้อความอธิบาย: ระบบเตือนทำงานเมื่อเปิดแอปและจอเปิดอยู่ ไม่ต้องใช้อินเทอร์เน็ต และการกันจอดับใช้แบตมากขึ้น
  - อ่านค่าตั้งใน `useEffect` (R8)
- ค่าตั้งกันจอดับต้องสื่อถึงทุกแท็บได้ จึงใช้ event `storage` หรือให้ provider อ่านค่าใหม่ทุกครั้งที่หน้ากลับมาแสดง

### เกณฑ์ผ่าน Phase 4
- `npm run typecheck` ผ่าน
- **บน tablet:** เปิด `/m/trips` ทิ้งไว้นานกว่าเวลาปิดหน้าจอที่ตั้งในเครื่อง จอต้องไม่ดับ ปิดสวิตช์แล้วจอต้องดับตามเวลาของเครื่อง
- **ปิดเสียงเตือน:** dispatch งานแล้วแถบขึ้น แต่ไม่ดังและไม่สั่น ค่าที่ตั้งยังอยู่หลัง reload
- **ปุ่มทดสอบเสียง:** ดังและสั่นทันที แม้ไม่เคยแตะจอมาก่อน

**Commit**: `feat(operator): let operators mute the new-trip alert and keep the screen awake`

---

## การตรวจสอบ

| ตรวจอะไร | วิธี | ที่ไหน |
|---|---|---|
| ลูปลองต่อใหม่ | `cd frontend && npm run test:realtime` | node test |
| logic เตือน | `npm run test:pwa` | node test |
| ของเดิมของ layer stack | `npm run test:overlay` | node test |
| type | `npm run typecheck` | frontend |
| build และ guard | `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only frontend` | container |
| realtime ของ admin ไม่ถอยหลัง | หน้า Orders, Trip drawer, Dashboard: ต้อง update realtime และฟื้นเองหลัง api restart | เบราว์เซอร์ |
| ทดสอบจริงทั้งระบบ | tablet บน Delta-IoT ทดสอบตามเกณฑ์ผ่านของ Phase 3 และ 4 | tablet จริง |

## เอกสารที่ต้องอัปเดตเมื่อเสร็จ
- ทำเครื่องหมาย ✅ ที่สถานะของแผนนี้
- `docs/reviews/frontend-review-2026-10-02.md`: M15 แก้แล้ว (Phase 1) และ M16 แก้บางส่วน (R3: refetch ที่ล้มไม่ล้างรายการแล้ว แต่ยังไม่แสดง error บนจอ)
- ไม่ต้องทำ ADR

## เกณฑ์ว่าเสร็จ
- [ ] มีงานใหม่แล้วดัง สั่น และขึ้นแถบ บนทุกหน้า `/m` ภายในประมาณ 1 วินาที หรือช้าสุด 30 วินาทีเมื่อ realtime ต่อไม่ได้
- [ ] เปิดหลายแท็บหรือมีงานเข้าติดกันก็ดังครั้งเดียว และตอนเปิดหน้าครั้งแรกไม่ดัง
- [ ] เปิดจอกลับมาแล้วเตือนงานที่เข้ามาระหว่างจอดับ
- [ ] จอไม่ดับระหว่างเปิดแอป และปิดได้ใน Settings
- [ ] realtime ทุกหน้าฟื้นเองหลัง api กลับมา (M15)
- [ ] เทสต์ทั้ง 3 ชุดผ่าน และใช้เวลาไม่เกินประมาณ 13 ชม.

## ทางถอยถ้าติดปัญหา
- **Phase 1 กระทบหน้า admin เกินคาด:** revert Phase 1 ได้อย่างเดียว ระบบเตือนยังพึ่ง poll สำรองได้ (R2)
- **ปลดล็อกเสียงบน tablet ไม่เสถียร:** ส่งเฉพาะสั่น แถบเตือน และตัวเลขก่อน
- **Wake Lock ใช้ไม่ได้บน tablet รุ่นนี้:** ให้ operator ตั้งเวลาปิดหน้าจอในเครื่องให้ยาวขึ้นแทน และซ่อนสวิตช์
- **Phase 2 มีของพัง:** ใส่การเตือนไว้ในหน้า Pool อย่างเดียว ได้ประโยชน์ประมาณ 60%

## ความเสี่ยงที่ยังเหลือหลังแก้
| ความเสี่ยง | โอกาส | ป้องกัน / ถอยกลับ |
|---|---|---|
| tablet ปิดเสียงสื่อไว้ | กลาง | แถบเตือนและการสั่นยังทำงาน และปุ่มทดสอบช่วยให้เห็นว่าเสียงปิดอยู่ |
| ปิด Chrome หรือสลับไปแอปอื่นนานๆ ทำให้ไม่เตือน | กลาง | ข้อจำกัดของเว็บแอป แจ้งในข้อความของ Settings |
| แต่ละ phase เป็น commit แยก | — | `git revert` ทีละ phase ได้ Phase 1 ไม่ขึ้นกับ phase อื่น ส่วน Phase 3 และ 4 ต้องมี Phase 2 ก่อน |

## ไว้ทำทีหลัง
- ตัวเลือกให้เสียงดังซ้ำจนกว่าจะกดรับรู้
- แสดง error ของ pool บนจอ (ส่วนที่เหลือของ M16)

## เมื่อเริ่มลงมือ
**ขั้นแรก**: สร้าง `frontend/lib/realtime/start-with-retry.ts` พร้อมไฟล์เทสต์ (Phase 1.1) เริ่มจากศูนย์จนเสร็จประมาณ 9.5 ชม. แต่ละ phase จบด้วย commit และ rebuild frontend ส่วนการทดสอบบน tablet จริงทำหลัง Phase 3 และ Phase 4
