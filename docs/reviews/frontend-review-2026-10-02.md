# Frontend review — ทั้ง frontend (2026-10-02)

รีวิวโดย workflow 10 reviewer + 10 adversarial verifier ตาม checklist ใน `.claude/commands/review-frontend.md` · typecheck: ผ่าน

**129 findings** — HIGH 15 · MEDIUM 58 · LOW 56 (ตัดซ้ำ 2 ข้อ, verifier หักล้าง 1 ข้อ)

> หมายเหตุ: verifier หักล้างได้แค่ 1 จาก 132 ข้อ — เข้มน้อยกว่าที่ควร ให้ถือ MEDIUM/LOW เป็น "น่าจะจริง" จนกว่าจะเปิดดูเอง

## สถานะ (อัปเดต 2026-10-20)

แก้แล้ว HIGH 12 จาก 15, MEDIUM 33 ข้อ (M5, M12, M14, M15, M16, M18, M21, M22, M23, M24, M25, M26, M27, M28, M29, M30, M31, M32, M33, M34, M35, M36, M38, M40, M42, M44, M45, M46, M47, M48, M49, M50, M57) และ LOW 1 ข้อ (L41) ส่วน M4, L28, L29 แก้เฉพาะส่วนที่เป็นเรื่อง dialog และ M43 ซ่อนฟอร์มไว้ก่อน ทุกข้อ commit บน main และ rebuild แล้ว หัวข้อที่แก้แล้วมีป้าย ✅ พร้อม commit

| ข้อ | เรื่อง | Commit |
|---|---|---|
| H5 | คิว offline เก็บ action ไว้เมื่อได้ 401 + ขึ้นป้ายให้ login ใหม่ | `57f91d3a` |
| H6 | Esc ใน combobox ปิดแค่ list | `a13ee6b2` |
| M12 | WMS location: Enter ไม่เลือกผลค้นหาชุดเก่า → เปลี่ยนเป็นโหลด location ทั้งหมดครั้งเดียวแล้วกรองในเบราว์เซอร์ | `3d598f59`, `0b43e4f0` |
| H15 | Template editor: Save รอจนเติม station เสร็จ | `8ee3ca17` |
| H7, H14 | backdrop ของ rail drawer และเมนูมือถือของหน้า landing ใช้ OverlayBackdrop | `a1e6d8a0` |
| H8 | drawer ของ order โหลดใหม่หลังทำ action | `a8480e1d` |
| H9 | ยืนยันก่อน Cancel/Retry trip พร้อมช่องเหตุผล | `c0bac7fd` |
| H3 | Reports โหลดใหม่ทันทีเมื่อเปลี่ยนช่วงเวลา | `e1b54373` |
| H10 | lightbox render ผ่าน portal ไปที่ body | `7cba1082` |
| H1, H2 | dark mode หน้า admin | `61e95e32` |
| H11 | PWA: trip ที่ complete แล้วแสดงเป็น done (backend เพิ่ม CompletedAt + migration) | `94c39a4c` |
| M23, M33, M34, M35, M40, M57, L41 (+ ส่วน dialog ของ M4, L28, L29) | layer stack กลาง: overlay ทั้ง 38 ตัวใช้ `ModalLayer` / `useDismissableLayer` (`lib/overlay`) — Esc ปิดทีละชั้น (ยกเว้นตอน busy), Tab วนใน dialog, คืน focus ให้ปุ่มที่เปิด, role/aria-modal/aria-labelledby, dialog ที่ย้อนกลับไม่ได้ (`intent="irreversible"`) เปิดมา focus ที่ปุ่มปลอดภัยและ Enter ใน input ไม่ส่งฟอร์ม, PASS ไม่รับ Enter ทั้งหน้าจออีก มี prebuild guard `check-overlay-keys.mjs` กันถอยกลับ ทดสอบในเบราว์เซอร์ครบ 3 checkpoint | `806fe858` … `8b2d57a6` (9 commits) |
| ต่อจาก H11 | PWA: trip ที่ dispatcher/order cancel แสดงเป็น Cancelled ไม่มีปุ่มทำงาน (DTO ส่ง Trip.Status มาด้วย) | `ef6317b0` |
| M5 | dark mode: ปุ่ม Cancel, ปุ่มรอง, filter, input, banner และป้ายสถานะทุกหน้า IAM มีสีของ dark mode | `c01e55a7`, `f6b256a0` |
| M49 | ไอคอน PWA 192/512 (maskable) + badge ของ notification สร้างจากโลโก้รถบรรทุกบนหัวหน้าเว็บ | `6782f815` |
| M18 | เมนู Robot, Driver, Auto door และ Active alerts แสดงเป็น "Soon" กดไม่ได้ (แทนลิงก์ 404 และจุดกะพริบ) | `8a6f4171` |
| M15 | realtime ทุกหน้าลองต่อใหม่เอง (backoff 1s → 30s) เมื่อต่อครั้งแรกไม่ได้หรือ restart หลัง drain ล้ม แล้ว subscribe กลุ่มใหม่ ไม่ต้อง reload หน้า | `c38ff16d` |
| M16, M45 | ข้อมูล pool อยู่ใน `OperatorPoolProvider` ที่ layout ของ `/m` refetch ที่ล้มเหลวคงรายการเดิมไว้ หน้า Pool แสดง "โหลด Pool ไม่สำเร็จ" พร้อมปุ่มลองใหม่แทน "Pool is empty" (หรือแถบเตือนเหนือรายการเดิม) และทิ้งผลของ fetch ที่ไม่ใช่ครั้งล่าสุด | `2c01dc95`, `f0a7c44a` |
| M48 | My trips และ Trip detail แสดง OrderRef, จุด pickup → drop และจำนวนชิ้น/น้ำหนักแบบการ์ด Pool (backend ส่งมากับ `/api/operator/trips/assigned`) ส่วน tripId เหลือเป็นบรรทัดเล็กในหน้า detail | `d193c8de` |
| M42, M44, M46, M47, M50 (+ M43 บางส่วน) | PWA: บันทึก offline ขึ้น 'Saved offline' ไม่ใช่ error สีแดง · โหลด trip ไม่สำเร็จบอกว่าโหลดไม่สำเร็จพร้อมปุ่มลองใหม่ (ไม่ใช่ 'not found') · รับงานจาก Pool ต้องออนไลน์ ไม่เข้าคิวอีก · logout ล้างคิว offline และ push เสมอ และถามก่อนถ้ายังมีรายการค้าง · ปุ่ม Settings, ป้าย queued และลิงก์ Back กดได้ 44px · ฟอร์มขอ override geofence ซ่อนไว้ (ส่งไม่ได้อยู่แล้ว และ geofence ปิด) | `d364498e` |
| M30, M31, M32, M36 | Dispatch: poll ล้มไม่ทำให้ตาราง trips หาย (แถบเตือน + Retry) · Trip drawer โหลดใหม่เมื่อมี timeline เข้า ป้ายสถานะและปุ่มไม่ค้างค่าเก่า · ?page=N ไม่ถูกรีเซ็ตตอนเปิดหน้า · ประกาศ token --color-wine/--color-flame ทั้ง light และ dark | `703ad77a` |
| M38 | /dispatch/manual อยู่ใน shell (TopNav + LeftRail) มีเมนู "Manual operators" ใต้ Dispatch (ต้องมี dtms:operator:pool:read) สีเป็น token ทั้งหมด และเวลาใช้ DateTime | `c40cb169` |
| M21, M25, M27 | Orders: ปุ่มใน drawer ทำงานกับ order ที่ไม่อยู่ในหน้า list (โหลด order มาเอง) · bulk cancel เตือนว่ามี order ที่ dispatched/running กี่รายการ หุ่นจะหยุด · CSV มี BOM (ภาษาไทยไม่เพี้ยนใน Excel) และกัน formula injection ทั้ง CSV ฝั่ง client และ report export ฝั่ง backend (`CsvField`) | `07e80468`, `be275d34` |
| M22, M24, M26, M28, M29 | Orders: ตัดโหมด Scroll ออก (เหลือแค่ Pages) · ?page=N ไม่ถูกรีเซ็ต · ช่องเซ็นชื่อ: สีเส้นตามธีมและพิกัดตรงนิ้วบนจอแคบ · Items: ป้ายสถานะใช้ -ink token อ่านได้ใน dark mode · แถวใช้ DataRow (คีย์บอร์ดได้) และมีการ์ดสำหรับจอเล็ก | `6377608d` |
| M14 | UOM: Enter ไม่แทนค่าที่พิมพ์ด้วยคำแนะนำแถวแรกอีก (KG ไม่กลายเป็น EA, BOX ไม่กลายเป็น BOXES) เลือกจากรายการเฉพาะตอนกดลูกศร/ชี้เมาส์ หรือพิมพ์ตรงคำแนะนำ (kg → KG) | `8021354a` |

**ไม่แก้ (ตัดสินใจ 2026-10-06)**
- **H4, H12, H13 — ข้อมูล mock** ใน /dashboard, /home และหน้า Profile: เจ้าของระบบเลือกข้าม คงไว้ตามเดิม

**ยังเปิดอยู่**
- ส่วนที่ไม่ใช่ dialog ของ M4 (aria-label ปุ่ม X), L28 (race ตอนคลิกหลายแถว), L29 (aria-label ปุ่มลบ item)
- MEDIUM/LOW ที่เหลือยังไม่ได้มีคนเปิดดูนอกจาก verifier

## HIGH

### H1. dark mode: ข้อความค่าต่างๆ ในหน้า System status และ Projection health มองไม่เห็น — CONFIRMED · ✅ แก้แล้ว `61e95e32`
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/system-status-experience.tsx:346`
- ปัญหา: มีการใส่ `dark:text-[var(--color-ink-100)]` และ `dark:text-[var(--color-ink-200)]` ทับซ้อนลงไป แต่ ink ramp กลับด้านอัตโนมัติอยู่แล้วใน `.dark` (globals.css:121-130 ink-100 = #1b2440, ink-200 = #28324f ซึ่งเป็นสี navy เข้ม) ข้อความจึงกลายเป็นสีน้ำเงินเข้มบนการ์ด navy เข้ม จุดที่เกิด: Field value system-status-experience.tsx:346, SectionHeader :217, ชื่อ projector projections-experience.tsx:147
- ผลกระทบ: ผู้ใช้ที่ OS ตั้งเป็น dark (providers.tsx ใช้ defaultTheme="system") จะอ่าน Latency, Streak, Last checked, Detail และชื่อ projector ไม่ออกเลย (contrast ประมาณ 1.1:1) บนหน้า monitoring ที่ on-call ต้องเปิดดู
- แก้: ลบ `dark:text-[var(--color-ink-100/200)]` ทิ้ง ให้ `text-[var(--color-ink-800)]` และ `ink-700` กลับสีเองตาม token
- Verifier: ยืนยันแล้ว: globals.css .dark กำหนด ink-100=#1b2440 และ ink-200=#28324f ส่วน dark variant ผูกกับ .dark (custom-variant บรรทัด 5) และ providers.tsx ใช้ defaultTheme="system" ดังนั้น Field (system-status:346 dark:text-ink-100), SectionHeader (:217 dark:text-ink-200) และชื่อ projector (projections-experience:147) จึงเป็นข้อความ navy เข้มบนพื้นการ์ด navy ทำให้แทบมองไม่เห็นใน dark mode

### H2. dark mode: modal ของ IAM และปุ่ม primary กลายเป็นตัวอักษรขาวบนพื้นขาว — CONFIRMED · ✅ แก้แล้ว `61e95e32`
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:1723`
- ปัญหา: `ModalShell` ใช้ `dark:bg-[var(--color-ink-900)]` ซึ่งใน dark คือ #f4f7fe (เกือบขาว) ขณะที่หัวข้อเป็น `dark:text-white` และ input สืบสี ink-900 (เกือบขาว) มาเหมือนกัน ปุ่ม primary `bg-[var(--color-ink-900)] text-white` ก็เป็นขาวบนขาวเช่นกัน จุดที่เกิด: ModalFooter :1903, iam-systems-experience.tsx:169,379,472, iam-system-subscriptions-experience.tsx:122,262,324
- ผลกระทบ: ใน dark mode ทุก modal ของ IAM (Edit metadata, Callback, Outbound token, Token refresh, Token source, Issue JWT, New system, Add subscription) อ่านหัวข้อและข้อความที่พิมพ์ไม่ได้ ปุ่ม Save, Create system และ Add subscription ก็มองไม่เห็นตัวอักษร
- แก้: ใช้ pattern เดียวกับ carrier-form-dialog.tsx:111 และ :230 คือ panel ใช้ `glass-strong` หรือ `bg-[var(--color-surface)]` ส่วนปุ่ม primary ใช้ `bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]` แทน ink-900
- Verifier: ยืนยันแล้ว: ModalShell (:1723), CreateSystemModal (systems:379) และ AddSubscriptionModal (subscriptions:262) ใช้ dark:bg-[var(--color-ink-900)] ซึ่งใน .dark เท่ากับ #f4f7fe แต่หัวข้อใช้ dark:text-white จึงเป็นขาวบนขาว ส่วนปุ่ม primary ใน ModalFooter (:1903), systems:169,472 และ subscriptions:122,324 ใช้ bg-ink-900 text-white โดยไม่มี dark override จึงเป็นตัวอักษรขาวบนพื้นเกือบขาวเหมือนกัน

### H3. เปลี่ยนช่วงเวลา (24h/7d/30d/90d) แล้วข้อมูลไม่โหลดใหม่ ทั้งใน Reports และหน้า Order/Robot analysis — CONFIRMED · ✅ แก้แล้ว `e1b54373`
- กลุ่ม: analytics · code
- ที่: `frontend/components/reports/reports-experience.tsx:38`
- ปัญหา: `useProjectionPoll` เก็บ fetcher ไว้ใน ref (lib/hooks/use-projection-poll.ts:41-42) และ `usePollSchedule` เริ่มรอบใหม่เฉพาะเมื่อ `[intervalMs, enabled]` เปลี่ยน (lib/hooks/use-poll-schedule.ts:122) พอ `window` เปลี่ยน fetcher ใหม่จึงถูกเก็บไว้เฉยๆ ไม่มีอะไรสั่งให้ fetch ทันที ทุก report (เช่น orders-summary-report.tsx:28-34 และอีก 6 ไฟล์ใน components/reports/) และ orders-analysis-experience.tsx:38-44 กับ robots-analysis-experience.tsx:42-48 ไม่ได้เรียก `refresh()` เมื่อ window เปลี่ยน และไม่ได้ใส่ `key` ให้ remount
- ผลกระทบ: ผู้ใช้กด 30d แล้ว tile/ตาราง/กราฟยังเป็นข้อมูล 7d ต่อไปอีกนานสุด 5 นาทีใน Reports และ 30 วินาทีในหน้า analysis แต่ปุ่ม toggle, subtitle "Trailing 30d" และลิงก์ CSV (`csvHref`) เปลี่ยนเป็นช่วงใหม่ทันที ตัวเลขบนจอจึงไม่ตรงกับไฟล์ที่ export และไม่ตรงกับช่วงที่เลือก
- แก้: ใน `useProjectionPoll` เพิ่ม effect ที่เรียก `trigger()` เมื่อ `fetcher` เปลี่ยน (ข้าม mount แรก) แล้ว `setData(null)` เพื่อไม่ให้แสดงข้อมูลช่วงเก่า หรือที่ฝั่ง caller ใส่ `key={windowKey}` ให้ report component และ `key={window}` ให้ส่วนที่ใช้ poll ในหน้า analysis
- Verifier: ยืนยันแล้ว: useProjectionPoll เก็บ fetcher ใน fetcherRef (บรรทัด 41-42) ส่วน tick เป็น useCallback ที่มี deps เป็น [] และ usePollSchedule รันใหม่เฉพาะเมื่อ [intervalMs, enabled] เปลี่ยน ฝั่ง ReportsExperience ไม่ได้ใส่ key ให้ report และ report ก็ไม่ได้เรียก refresh เมื่อ window เปลี่ยน orders/robots-analysis เป็นแบบเดียวกัน (มีแค่ hub CountersUpdated ที่อาจทำให้ refetch เร็วขึ้นบ้างในหน้า analysis) ส่วน csvHref คำนวณใหม่ทันที ตัวเลขบนจอจึงไม่ตรงกับไฟล์ที่ export

### H4. หน้า /dashboard (Overview) เอาข้อมูล mock และตัวเลขที่ hard-code ไว้มาแสดงปนกับข้อมูลจริง — CONFIRMED · ⏭️ ไม่แก้ (ตัดสินใจ 2026-10-06)
- กลุ่ม: analytics · uiux
- ที่: `frontend/components/dashboard/live-activity-card.tsx:8`
- ปัญหา: LiveActivityCard (`weekActivity` จาก lib/mock-data และค่า hard-code อย่าง +18%, $142.8k, On-time 96.4% ที่บรรทัด 163-179), PriorityShipments (priority-shipments.tsx:8, 40 "Three shipments need supervisor approval", 46 "See all 14" href="#"), DriverLeaderboard (driver-leaderboard.tsx:8, 25 href="#"), CommsCard (comms-card.tsx:45 "Truck 14 needs reroute", 102 ปุ่ม Reply ที่ไม่มี onClick) และ footer app/dashboard/page.tsx:33 "last refresh 07:42:18" กับ build 4aa1133 เป็นของปลอมทั้งหมด ปุ่ม "All routes" ที่ dispatch-funnel.tsx:114-120 ก็ไม่มี onClick เมนูใน left-rail.tsx:73 เรียกหน้านี้ว่า "Live operations cockpit"
- ผลกระทบ: Operator เห็นตัวเลขปลอม (เช่น รายได้, On-time %, รถที่ต้องอนุมัติ, คนขับ) วางคู่กับ KpiRail/DispatchFunnel ที่เป็นข้อมูลจริง และแยกไม่ออกว่าอันไหนจริง อาจตัดสินใจผิดได้ คลิก "See all" กับ "Reply" แล้วก็ไม่เกิดอะไรขึ้น
- แก้: เอาการ์ด mock (LiveActivityCard, PriorityShipments, DriverLeaderboard, CommsCard) และ footer ปลอมออกจาก app/dashboard/page.tsx หรือถ้าจะเก็บไว้ให้ติดป้าย "ตัวอย่าง/ยังไม่เชื่อมข้อมูล" ให้เห็นชัด ปุ่ม "All routes" ให้ลบออก หรือเปลี่ยนเป็นข้อความธรรมดาที่กดไม่ได้
- Verifier: ยืนยันแล้ว: live-activity-card, priority-shipments และ driver-leaderboard import ข้อมูลจาก @/lib/mock-data ส่วนค่า $142.8k และ 96.4% เป็นค่า hard-code ลิงก์ "See all" เป็น href="#" ปุ่ม Reply on channel และ All routes ไม่มี onClick footer เขียน 07:42:18 กับ build 4aa1133 ตายตัว และไม่มีป้าย demo/mock ที่ไหนเลย ทั้งหมดวางอยู่ข้าง KpiRail และ DispatchFunnel ที่เป็นข้อมูลจริง บนหน้าที่เมนูเรียกว่า "Live operations cockpit"

### H5. sw.js ลบ action ที่คิวไว้ตอน offline เมื่อได้ 401 (session หมดอายุ) — CONFIRMED · ✅ แก้แล้ว `57f91d3a`
- กลุ่ม: core · code
- ที่: `frontend/public/sw.js:125`
- ปัญหา: drainQueue ถือว่า 4xx ทุกตัว (ยกเว้น 429/408) เป็น permanent failure แล้ว deleteQueueRow ทันที รวมถึง 401 ด้วย app/api proxy (lib/api/proxy-helpers.ts:48-61) ส่ง token ที่หมดอายุจาก cookie ต่อไปให้ backend ซึ่งจะตอบกลับเป็น 401 ถ้า operator อยู่ offline นานกว่าอายุ JWT (เช่นทั้งกะ) แล้ว Background Sync ทำงานตอน token หมดอายุแล้ว pickup/drop confirmation ที่คิวไว้จะถูกลบทิ้งเงียบๆ lib/operator-pwa/offline-queue.ts:150 isPermanentFailure ก็ใช้กฎเดียวกัน
- ผลกระทบ: การยืนยัน pickup/drop ของ operator หายไปถาวรโดยไม่มีใครรู้ trip ค้าง และต้องไปไล่หาย้อนหลัง (ข้อมูลหาย) ตัดสินจาก code path เท่านั้น ยังไม่ได้สังเกตตอนรันจริง
- แก้: ให้ 401 (และ 403 ที่มาจาก session) เป็นกรณี 'ยังไม่ใช่ตอนนี้' แบบเดียวกับ 429/408 คือเก็บ row ไว้แล้ว break แก้ทั้ง sw.js:125 และ isPermanentFailure ใน offline-queue.ts ให้ตรงกัน แล้วให้ PWA shell แสดงว่า 'ต้อง login ใหม่เพื่อส่ง N รายการที่ค้าง'
- Verifier: sw.js:125 ลบ row ทุก 4xx ยกเว้น 429/408 จริง และ isPermanentFailure (offline-queue.ts:150) ใช้กฎเดียวกันทั้งใน drainQueue ฝั่ง foreground ด้วย getServerToken (server-session.ts:16) แค่อ่าน cookie ไม่มี refresh ไม่เช็ก expiry ก่อนส่ง proxy-helpers จึงส่ง token หมดอายุไป backend ได้ 401 แล้ว action ถูกลบทิ้ง ใน SW ไม่มีการแจ้งใครเลย

### H6. กด Escape ใน combobox แล้วปิด dialog ทั้งอัน ข้อมูลในฟอร์มหาย — CONFIRMED · ✅ แก้แล้ว `a13ee6b2`
- กลุ่ม: core · code
- ที่: `frontend/components/primitives/station-combobox.tsx:103`
- ปัญหา: เมื่อกด Escape ตอน list เปิดอยู่ handler แค่ setOpen(false) โดยไม่เรียก stopPropagation event จึง bubble ขึ้นไปถึง window listener ของ CreateOrderDialog (components/delivery-orders/create-dialog.tsx:308) ซึ่งจะเรียก onClose() และตอนเปิดใหม่ฟอร์มก็ถูก reset (create-dialog.tsx:263-280) ปัญหาเดียวกันมีใน int-id-combobox.tsx:121, action-template-combobox.tsx:102, uom-combobox.tsx:98 และ wms-location-combobox.tsx:139
- ผลกระทบ: ผู้ใช้กด Esc เพื่อปิด dropdown แต่ dialog สร้าง order หลาย item ปิดไปด้วย ข้อมูลที่กรอกไว้หายหมด
- แก้: ในทุก combobox ให้เรียก e.stopPropagation() (และ e.preventDefault()) เฉพาะตอน open เป็น true ก่อน setOpen(false) ถ้า list ปิดอยู่แล้วให้ปล่อย Esc ผ่านไปตามเดิม ควรรวม keyboard model ไว้ใน hook เดียว (เช่น useComboboxKeys) จะได้ไม่ต้องแก้ 5 ที่
- Verifier: ทั้ง 5 combobox ที่ Escape แค่ setOpen(false) ไม่มี stopPropagation create-dialog.tsx:306-311 มี window keydown listener ที่เรียก onClose() เมื่อกด Escape และ effect บรรทัด 263-283 reset form เป็น blankForm ตอนเปิดใหม่ React synthetic event bubble ไปถึง window จริง ข้อมูลฟอร์มหายจริง

### H7. Rail drawer (tablet) ทำ backdrop เองไว้ใน AnimatePresence ขัดกฎ OverlayBackdrop — CONFIRMED · ✅ แก้แล้ว `a1e6d8a0`
- กลุ่ม: core · uiux
- ที่: `frontend/components/shell/left-rail.tsx:345`
- ปัญหา: drawer ใช้ motion.div แบบ 'fixed inset-0 z-50' ซึ่งมีปุ่ม backdrop อยู่ข้างในเป็นลูกของ <AnimatePresence> นี่คือ pattern ที่ OverlayBackdrop บอกไว้ว่าเปิด/ปิดเร็วๆ แล้ว exit ค้าง จนเหลือ div ล่องหนที่กินทุกคลิก (field repro 2026-07-24)
- ผลกระทบ: บนจอที่เล็กกว่า lg (tablet portrait) ถ้ากด hamburger เปิด/ปิดเร็วๆ อาจได้หน้า /home หรือ /dashboard ที่คลิกอะไรไม่ได้เลยจนกว่าจะ hard refresh
- แก้: ย้าย backdrop ออกมาเป็น <OverlayBackdrop open={railDrawerOpen} onClick={closeRailDrawer} className="lg:hidden z-50 bg-[var(--color-ink-900)]/30 backdrop-blur-sm" /> ไว้เป็น sibling ก่อน AnimatePresence แล้วเหลือแค่ motion.aside ไว้ใน AnimatePresence ส่วน panel ให้ใช้ fixed z-[51]
- Verifier: left-rail.tsx:345-361 เป็น motion.div fixed inset-0 z-50 ครอบปุ่ม backdrop อยู่ใน AnimatePresence ไม่ได้ใช้ OverlayBackdrop (grep components/shell ไม่เจอ OverlayBackdrop) ตรงกับ pattern ที่ house rule ห้ามไว้และเคยเกิด stranded click-blocking div จริง แม้กระทบเฉพาะจอ < lg

### H8. Drawer ไม่ refresh หลังทำ action (Submit/Hold/Release/Reject/Reopen/Redispatch) — CONFIRMED · ✅ แก้แล้ว `a8480e1d`
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:887`
- ปัญหา: onAction ของ OrderDetailDrawer เรียก setTimeout(() => setDetailId(id), 50) เพื่อให้ drawer โหลดใหม่ แต่ detailId เท่ากับ id อยู่แล้ว ค่าจึงไม่เปลี่ยน และ effect ของ drawer (detail-drawer.tsx:106-161) มี deps แค่ [orderId] เลยไม่ fetch ใหม่ อีกทั้งสำหรับ hold/release/reject/reopen/redispatch ฟังก์ชัน runAction แค่เปิด dialog แล้ว return ทันที โค้ด re-open จึงทำงานก่อนผู้ใช้กดยืนยันด้วยซ้ำ ส่วน hub subscription ใน drawer (detail-drawer.tsx:102) อัปเดตเฉพาะ audit log ไม่อัปเดต header, items หรือ footer และ list poll ก็ถูกปิดระหว่างที่ drawer เปิด (orders-experience.tsx:407)
- ผลกระทบ: ผู้ใช้ Submit หรือ Hold จาก drawer สำเร็จแล้ว แต่ drawer ยังแสดงสถานะเดิมพร้อมปุ่มเดิม (เช่นปุ่ม Submit/Hold) ถ้ากดซ้ำจะได้ error จาก backend และผู้ใช้เข้าใจผิดว่า action ไม่สำเร็จ
- แก้: ให้ OrderDetailDrawer รับ prop refreshKey (number) แล้วใส่ใน deps ของ effect โหลดข้อมูล จากนั้น parent เพิ่มค่า refreshKey ใน onConfirm ของ StateActionDialog/ReopenDialog/RedispatchDialog/PodScanDialog และหลัง submit สำเร็จ ลบ hack setDetailId(null)→setDetailId(id) ทั้งที่บรรทัด 894 และ 936-940 ออก (hack นี้ทำให้ drawer เลื่อนปิดแล้วเปิดใหม่)
- Verifier: ยืนยันในโค้ด: onAction ใน orders-experience.tsx:887-895 เรียก setTimeout(() => setDetailId(id), 50) ซึ่งเป็นค่าเดิม React จึงไม่ re-render และ effect โหลดข้อมูลของ detail-drawer.tsx:106-161 มี deps แค่ [orderId] จึงไม่ fetch ใหม่ ส่วน hold/release/reject/abandon/reopen/redispatch นั้น runAction แค่ set target ของ dialog แล้ว return และ onConfirm ของ StateActionDialog (บรรทัด 1127-1175) อัปเดตเฉพาะ setOrders กับ fetchOrders ไม่ได้ refresh drawer (ยกเว้น abandon ที่ปิด drawer ไปเลย) ส่วน useOrderHubSubscription อัปเดตแค่ liveActivityEntry และ poll ถูกปิดเมื่อ detailId มีค่า (enabled: !createOpen && !detailId) ผลคือ drawer แสดงสถานะกับปุ่มเดิมหลังทำ action ทุกครั้ง

### H9. ปุ่ม Cancel และ Retry ของ Trip ทำงานทันทีโดยไม่มีการยืนยัน — CONFIRMED · ✅ แก้แล้ว `c0bac7fd`
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/trip-action-bar.tsx:142`
- ปัญหา: ActionButton "Cancel" เรียก run("cancel", () => cancelTrip(tripId, "Cancelled by operator")) ทันทีที่คลิก (บรรทัด 148) ส่วน Retry (บรรทัด 136-140) ก็เรียก retryTrip ทันทีเช่นกัน ไม่มี dialog ยืนยัน ทั้งที่ Pass ซึ่งผลกระทบเบากว่ายังมี PassRobotDialog ให้ยืนยัน ปุ่ม Cancel วางติดกับ Retry/Resume ในแถวเดียวกันซึ่งเป็น pill ขนาดเล็ก
- ผลกระทบ: แค่คลิกพลาดครั้งเดียวก็ยกเลิก trip ของหุ่นที่กำลังวิ่งงานจริงได้ และย้อนกลับไม่ได้ (ต้อง Retry ซึ่งจะได้ trip ใหม่และ attempt ใหม่) ส่วน Retry ที่กดซ้ำโดยไม่ตั้งใจจะส่งงานใหม่ไปให้หุ่นออกวิ่ง ขัดกับกฎ UX flow ที่ว่า destructive action ต้องยืนยันและบอกผลที่จะเกิดก่อน
- แก้: ทำ confirm dialog สำหรับ cancel/retry ตามแบบ PassRobotDialog (มี OverlayBackdrop, ล็อกตอน busy, แสดง trip/vehicle และบอกว่าหุ่นจะหยุดหรือจะถูกส่งงานใหม่) โดยให้ run() ทำงานหลังผู้ใช้กดยืนยันเท่านั้น และควรเปิดให้กรอกเหตุผลการยกเลิกแทนข้อความที่ hard-code ไว้
- Verifier: trip-action-bar.tsx:148 เรียก run("cancel", () => cancelTrip(...)) ทันทีที่คลิกจริง ไม่มี dialog ยืนยัน และ ActionButton ก็ไม่มี confirm ในตัว ซึ่งขัดกฎ review-frontend.md:90 ที่ระบุ cancel trip ไว้ตรงๆ ว่าต้องยืนยัน มีจุดเดียวที่ต้องแก้คือ canRetry (status==="Cancelled") กับ canCancel ไม่มีทางเปิดพร้อมกัน จึงกดพลาดระหว่าง Retry กับ Cancel ไม่ได้ แต่ Cancel วางติดกับ Pause/Resume ที่เปิดอยู่พร้อมกันได้ ส่วนการ Retry โดยไม่ยืนยันนั้นเสี่ยงน้อยกว่า เพราะทำได้กับ trip ที่ถูก cancel แล้วเท่านั้น

### H10. Lightbox ที่เปิดจาก gallery ถูกขังและตัดขอบอยู่ในกล่อง dialog/panel ไม่ได้เต็มจอ — CONFIRMED · ✅ แก้แล้ว `7cba1082`
- กลุ่ม: fleet · uiux
- ที่: `frontend/components/attachments/attachment-gallery.tsx:116`
- ปัญหา: AttachmentGallery render <AttachmentLightbox> ซึ่งใช้ `fixed inset-0` ไว้ภายในตัวเองโดยตรง ไม่ผ่าน portal แต่ gallery ถูกวางอยู่ใน container ที่ใช้ class `glass-strong` (มี `backdrop-filter: blur(32px)`, globals.css:314) และ `overflow-hidden` คือ photos-dialog.tsx:76 (max-w-lg) และ carrier-maintenance-panel.tsx:64 (max-w-md) ตามสเปก CSS ค่า backdrop-filter ที่ไม่ใช่ none จะสร้าง containing block ให้ลูกที่เป็น position:fixed ทำให้ backdrop z-[60] และตัวภาพที่กว้าง `min(calc(100vw - 3rem), …)` ถูกจัดตำแหน่งเทียบกับกล่อง dialog แทน viewport และถูก overflow-hidden ตัดทิ้ง ส่วน path ที่กด PhotoCell บนตาราง (FocusedPhoto) ไม่เจอปัญหานี้ เพราะ lightbox อยู่นอก glass container
- ผลกระทบ: ถ้ากดรูปย่อใน dialog Photos หรือในประวัติซ่อมบำรุง lightbox จะไม่คลุมทั้งจอ ภาพจะถูกตัดในกรอบเล็กๆ และปุ่ม Delete/Close ใต้ภาพอาจหลุดออกนอกพื้นที่ที่มองเห็น ผู้ใช้จึงดูภาพเต็มหรือลบภาพจาก gallery ไม่ได้ (ข้อนี้อนุมานจากสเปก CSS ยังต้องเปิดดูใน Edge/Chrome เพื่อยืนยัน)
- แก้: ให้ AttachmentLightbox render ผ่าน `createPortal(…, document.body)` แบบเดียวกับ components/primitives/row-menu-portal.tsx โดยครอบทั้ง OverlayBackdrop และ AnimatePresence เพื่อให้ fixed อิง viewport เสมอ ไม่ว่า gallery จะถูกวางไว้ที่ไหน
- Verifier: ตรวจแล้วตรงตามที่ว่า: AttachmentGallery.tsx:116 render AttachmentLightbox ตรงๆ ไม่มี portal ส่วน lightbox (attachment-lightbox.tsx:82-91) ใช้ OverlayBackdrop กับ div `fixed inset-0 z-[61]` ซึ่งอยู่ในต้นไม้ DOM เดียวกัน gallery ถูกวางใน motion.div `overflow-hidden glass-strong` (components/attachments/photos-dialog.tsx ไม่ใช่ fleet/ แต่บรรทัดใกล้เคียง) และ motion.aside `overflow-hidden glass-strong` (carrier-maintenance-panel.tsx:64) โดย globals.css กำหนด .glass-strong ให้มี backdrop-filter: blur(32px) ซึ่งตามสเปกและ Chromium จะกลายเป็น containing block ของลูกที่เป็น position:fixed (transform จาก motion ระหว่าง animate ก็มีผลแบบเดียวกัน) lightbox จึงถูกจัดตำแหน่งในกรอบ dialog และถูก overflow ตัด ผู้ใช้จะเจอทุกครั้งที่กดรูปย่อใน Photos dialog หรือใน maintenance panel ระดับ HIGH เหมาะสมแล้ว

### H11. Trip ที่ complete แล้วค้างอยู่ที่ปุ่ม 'Complete trip' ตลอดไป ไม่มีวันถึงขั้น done — CONFIRMED · ✅ แก้แล้ว `94c39a4c`
- กลุ่ม: operator-pwa · code
- ที่: `frontend/components/operator-pwa/trip-detail.tsx:86`
- ปัญหา: step คำนวณจาก acknowledgedAt/pickedUpAt/droppedAt เท่านั้น ไม่มีทางได้ค่า "done" ส่วน AssignedTrip/AssignedTripDto (backend GetAssignedTripsQuery.cs) ก็ไม่มี CompletedAt หลัง completeTrip สำเร็จ refresh() จะได้ trip เดิมที่มี droppedAt จึงยังเป็น step "complete" StepBadge "done" กับ ActionPanel return null จึงเป็นโค้ดที่ไม่มีวันถูกใช้
- ผลกระทบ: หลังกด Complete ปุ่มจะกลับมาให้กดอีก operator จะไม่แน่ใจว่าปิดงานแล้วหรือยัง ถ้ากดซ้ำจะได้ error 4xx จาก server แล้วกลายเป็นแบนเนอร์สีแดง ส่วน trips-list ก็ยังขึ้น trip ที่ปิดแล้วเป็น 'Delivered' ค้างไว้
- แก้: เพิ่ม CompletedAt (หรือ status) ใน AssignedTripDto และ type AssignedTrip แล้วคำนวณ step "done" จากค่านั้น หรือถ้า complete คือการออกจาก assignment จริง ให้ filter ออกจาก GetByOperatorIdAsync แล้ว redirect ไป /m/trips หลังกด Complete สำเร็จ
- Verifier: AssignedTripDto ไม่มี CompletedAt ส่วน CompleteTripCommandHandler เรียกแค่ trip.MarkVendorCompleted() กับ op.ClearTripAssignment() โดยไม่แตะ ManualTripExtension และ GetByOperatorIdAsync ก็ไม่ได้ filter trip ที่จบแล้วออก trip จึงยังกลับมาพร้อม droppedAt และ step ยังเป็น "complete" ตลอด ทำให้ step "done" ไม่มีวันเกิดขึ้น

### H12. Security tab แสดงสถานะความปลอดภัยปลอม และปุ่ม 'Sign out everywhere' ไม่ทำอะไรเลย — CONFIRMED · ⏭️ ไม่แก้ (ตัดสินใจ 2026-10-06)
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/profile/tab-security.tsx:77`
- ปัญหา: ปุ่ม 'Sign out everywhere' (บรรทัด 77) และปุ่ม Change/Manage/Regenerate ใน SettingRow (บรรทัด 154) ไม่มี onClick แถวต่างๆ อ้างว่า 2FA enrolled, recovery codes 8 of 10 และเปลี่ยนรหัสผ่านไปแล้ว 41 วัน ส่วนรายการ session (บรรทัด 9-31) hardcode ไว้ทั้งหมดรวมถึง IP 192.168.4.18 แต่ผู้ใช้เปิดเจอได้จริงจากลิงก์ /profile ใน components/shell/top-nav.tsx:221
- ผลกระทบ: ผู้ใช้ที่สงสัยว่าบัญชีโดนเจาะกด 'Sign out everywhere' แล้วเชื่อว่า session ถูกเพิกถอนแล้ว แต่ความจริงไม่มีอะไรเกิดขึ้น และเชื่อผิดๆ ว่ามี 2FA ป้องกันอยู่
- แก้: ซ่อนหรือลบ TabSecurity ออกจาก profile-tabs.tsx จนกว่า backend จะมี API สำหรับ session และ 2FA ถ้าจะเก็บไว้ก็แสดงเฉพาะข้อมูลจริง และลบปุ่มที่ไม่ได้ต่อกับระบบออก
- Verifier: ปุ่ม 'Sign out everywhere' (tab-security.tsx:77) และปุ่มใน SettingRow (บรรทัด 154) ไม่มี onClick จริง TabSecurity ถูก render ใน profile-tabs.tsx:106 และ /profile ก็เปิดได้จากลิงก์ใน shell/top-nav.tsx:221 ผู้ใช้จึงกดปุ่มเพิกถอน session ปลอมได้จริง ถือเป็นการหลอกในเรื่องความปลอดภัย

### H13. /home แสดง KPI ปฏิบัติการที่ hardcode ไว้ แต่ติดป้าย LIVE — CONFIRMED · ⏭️ ไม่แก้ (ตัดสินใจ 2026-10-06)
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/home/home-overview.tsx:30`
- ปัญหา: TILES (บรรทัด 30-63) เป็นตัวเลขคงที่ คือ Active fleet 142, On-time 96.4%, In transit 47 และ Distance 8,420 km คอมเมนต์ในโค้ดเองก็ยอมรับว่าเป็น mock แต่ tile 'In transit' แสดง StatusPulse กับคำว่า 'live' (บรรทัด 128-133) และมี trend badge สีเขียว หน้านี้คือปลายทางของลิงก์ 'Home' ใน left-rail
- ผลกระทบ: operator อ่านตัวเลขปลอมเป็นสถานะจริงของหน้างาน เช่น 'In transit 47 LIVE' และอาจตัดสินใจผิด อีกทั้งทำให้ผู้ใช้ไม่เชื่อตัวเลขจริงบนหน้าอื่นด้วย
- แก้: ต่อเข้ากับ projection หรือ report endpoint ที่มีอยู่แล้ว (เช่นตัวเลขที่ components/dashboard ใช้) ผ่าน use-projection-poll หรือถ้ายังไม่มีข้อมูลก็ลบ KPI column ออก หรือติดป้าย 'ตัวอย่าง' และเอา StatusPulse กับ trend badge ออก
- Verifier: TILES ใน home-overview.tsx hardcode ไว้และคอมเมนต์ก็เขียนว่า 'Mock numbers' ส่วน tile 'In transit' มี live:true ซึ่งไป render StatusPulse กับคำว่า 'live' หน้านี้ถูกใช้ใน app/home/page.tsx และ left-rail.tsx:65 ก็ลิงก์ Home มาที่ /home operator จึงเห็นตัวเลขปลอมที่ติดป้ายว่า live

### H14. Mobile menu ของ landing ใช้ backdrop ที่ทำเองใน AnimatePresence (ผิด house rule) — CONFIRMED · ✅ แก้แล้ว `a1e6d8a0`
- กลุ่ม: shell-pages · code
- ที่: `frontend/components/landing/top-nav.tsx:612`
- ปัญหา: backdrop เป็น <motion.button className="fixed inset-0 z-40 ..."> ที่อยู่ใน AnimatePresence และมี exit animation ซึ่งเป็น pattern ที่ components/primitives/overlay-backdrop.tsx เขียนไว้ว่าเคยทำให้ element ค้างอยู่ใน DOM เมื่อเปิดปิดเร็วๆ
- ผลกระทบ: ถ้าเปิดปิดเมนูเร็วๆ บนมือถือหรือแท็บเล็ต (<lg) อาจเหลือ div โปร่งใสแบบ fixed inset-0 ค้างอยู่ ทำให้คลิกอะไรบนหน้า / ไม่ได้เลย รวมถึงปุ่ม Sign in จนกว่าจะ refresh (ประเมินจากโค้ด ยังไม่ได้ repro จริง)
- แก้: ใช้ <OverlayBackdrop open={mobileOpen} onClick={() => setMobileOpen(false)} className="lg:hidden z-40 bg-[var(--color-ink-900)]/30 backdrop-blur-2xl" /> เป็น sibling ไว้ก่อน AnimatePresence ของ motion.nav แล้วลบ motion.button backdrop ออก
- Verifier: landing/top-nav.tsx (ราวบรรทัด 149-163 ไม่ใช่ 612) มี motion.button แบบ fixed inset-0 เป็น backdrop อยู่ใน AnimatePresence และมี exit animation ไม่ได้ใช้ OverlayBackdrop ซึ่งตรงกับ failure mode ที่ overlay-backdrop.tsx บันทึกไว้ และ house rule ใน review-frontend.md:51 กำหนดให้กรณีนี้เป็น HIGH

### H15. แก้ไข template ก่อนรายการ station โหลดเสร็จ จะลบ pickup/drop station ทิ้งเงียบๆ — CONFIRMED · ✅ แก้แล้ว `8ee3ca17`
- กลุ่ม: templates · code
- ที่: `frontend/components/order-templates/template-editor.tsx:350`
- ปัญหา: formFromDto (บรรทัด 154-155) ตั้ง pickupStationCode/dropStationCode เป็น "" ไว้ก่อน แล้วค่อยเติมใน effect บรรทัด 280-297 หลังจาก useStationOptions โหลดเสร็จ effect นี้ return ทิ้งเมื่อ stations.loading หรือ stations.error เป็นจริง (บรรทัด 282) แต่ handleSubmit ไม่ได้รอโหลดเสร็จ และส่ง `pickupStationCode: form.pickupStationCode.trim() || null` ไปตรงๆ (บรรทัด 350-351) ฝั่ง backend มองว่า null แปลว่าให้ล้างค่า: StationCodeHelpers.ResolveStationCodeAsync คืน Ok(null) แล้ว OrderTemplate.Update กำหนด PickupStationId = null โดยไม่มีเงื่อนไข
- ผลกระทบ: ถ้ากด Save changes ก่อน catalog station โหลดเสร็จ หรือตอน /stations error template จะถูกบันทึกโดยไม่มี pickup/drop station และไม่มีอะไรแจ้งผู้ใช้เลย ค่าที่หายไปนี้มีผลกับ order ทุกใบที่สร้างจาก template นี้หลังจากนั้น
- แก้: ปิดปุ่ม submit ไว้ตอน isEdit/isDuplicate และ stations.loading ยังเป็นจริง ถ้า stations.error ให้แสดงข้อความเตือนและไม่ให้บันทึก หรือแยกเก็บสถานะว่าค่ายังไม่ถูก resolve (เช่น ใช้ undefined) แล้วตัด field ออกจาก payload แทนการส่ง null ทางที่ดีกว่าคือให้ backend คืน pickupStationCode/dropStationCode มาใน read DTO ด้วย form จะได้ไม่ต้องพึ่ง catalog
- Verifier: ตรวจแล้วตรงตามที่รายงาน: formFromDto ตั้ง pickupStationCode/dropStationCode เป็น "" (บรรทัด 154-155) effect บรรทัด 280-297 return ทิ้งเมื่อ stations.loading || stations.error ส่วน handleSubmit ส่ง `form.pickupStationCode.trim() || null` ไปเลย ปุ่ม submit ปิดแค่ตอน busy (บรรทัด 419) ฝั่ง backend: PlanningEndpoints.cs:399 resolve code ที่เป็น null แล้ว OrderTemplate.cs:112 กำหนด PickupStationId = pickupStationId โดยไม่มีเงื่อนไข ถ้ากดบันทึกระหว่างที่ /stations ยังโหลดอยู่ (ช่วงสั้น) หรือ /stations error (เกิดได้จริง) station จะหายไปโดยไม่มีอะไรแจ้ง

## MEDIUM

### M1. Audit log ไม่แสดง details จึงไม่รู้ว่า action นั้นทำกับ system ไหน — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-audit-log-experience.tsx:198`
- ปัญหา: backend ปัจจุบันเขียน audit เกือบทุกแถวเป็น action แบบ system-* และ subscription-* (เช่น system-credential-rotated, system-callback-token-revealed, system-hard-deleted ใน SystemAdminEndpoints.cs:81-1115) โดยเก็บ systemKey ไว้ใน `details` (JSON) และ `role` เป็น null แต่ `AuditRow` แสดงแค่ actor, action, role และ permissionCode ส่วน `details` ซึ่งมีอยู่ใน `AuditLogEntryDto.details` ไม่ถูกแสดงเลย
- ผลกระทบ: ตอบคำถามพื้นฐานของ audit log อย่าง "ใคร rotate key ของ OMS" หรือ "ใคร reveal token ของ wms" ไม่ได้ เพราะทุกแถวขึ้นแค่ actor กับ action แล้วตามด้วย — — ทำให้หน้านี้ใช้งานจริงไม่ได้สำหรับ entry ที่ระบบเขียนอยู่ทุกวันนี้
- แก้: parse `entry.details` แล้วเพิ่มคอลัมน์ System (หยิบ `systemKey` มาแสดงเป็น font-mono) แสดง JSON ที่เหลือแบบย่อในแถวที่ขยายได้หรือใน tooltip และเปลี่ยนคอลัมน์ Role เป็น Target ให้ตรงกับข้อมูลจริง
- Verifier: ยืนยันแล้ว: backend เขียน PermissionAuditEntry เป็น action แบบ system-* และ subscription-* โดยใส่ details = JSON {systemKey} และไม่ได้ใส่ role (เช่น SystemAdminEndpoints.cs:1080-1084 system-grant) ส่วน AuditRow แสดงแค่ occurredAt, actor, action, role และ permissionCode โดย AuditLogEntryDto.details (lib/api/iam.ts:28) ไม่ถูกแสดงเลย ผลคือไม่รู้ว่า action ทำกับ system ไหน แต่ลดเหลือ MEDIUM เพราะเป็นข้อมูลที่ขาดหายบนหน้า admin ที่อ่านอย่างเดียว ไม่มีข้อมูลเสียหายหรือ interaction ที่ใช้ไม่ได้

### M2. Audit log ยิง request ทุกครั้งที่พิมพ์ ไม่มี abort และผลลัพธ์อาจมาไม่ตรงลำดับ — CONFIRMED
- กลุ่ม: admin · code
- ที่: `frontend/components/admin/iam-audit-log-experience.tsx:48`
- ปัญหา: `load` มี deps เป็น [actor, role, action, page] และ effect เรียกมันทุกครั้งที่ค่าเปลี่ยน พอพิมพ์ใน FilterInput จึงยิง `queryAuditLog` ทุกตัวอักษร ทั้งยังไม่ส่ง `signal` และไม่มี AbortController หรือ sequence guard
- ผลกระทบ: พิมพ์ EmployeeId 8 หลักจะได้ 8 request พร้อมกัน ถ้า response ของ "8634" กลับมาหลังของ "86347852" ตารางจะแสดงผลของ filter เก่า ขณะที่ช่อง input แสดงค่าใหม่ ผู้ใช้จึงเห็นข้อมูลที่ไม่ตรงกับ filter
- แก้: ใน effect ให้สร้าง AbortController แล้วส่ง `signal` เข้า `queryAuditLog` (ฟังก์ชันรองรับ `params.signal` อยู่แล้ว) จากนั้น abort ใน cleanup และ debounce ค่า actor/role ประมาณ 300ms ก่อนเอาเข้า deps
- Verifier: ยืนยันแล้ว: load มี deps [actor, role, action, page] และ effect เรียกทุกครั้งที่ค่าเปลี่ยน ทุกตัวอักษรที่พิมพ์ใน FilterInput จึงยิง request ไม่มี AbortController และไม่มี sequence guard ทั้งที่ queryAuditLog รับ params.signal อยู่แล้ว (lib/api/iam.ts:56,67) ถ้า response กลับมาไม่ตรงลำดับจะทำให้ตารางไม่ตรงกับ filter และ house rule ก็ระบุเรื่อง out-of-order fetch ไว้ตรงๆ

### M3. Audit log: ตัวเลือก Action ใน filter เป็นของ feature ที่ถูกลบไปแล้ว — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-audit-log-experience.tsx:10`
- ปัญหา: `ACTION_LABELS` ซึ่งใช้เป็น option ของ select มีแค่ grant, revoke, permission-* และ role-* แต่ roles ถูกลบไปตั้งแต่ 2026-08-01 และ permission catalog ก็อ่านได้อย่างเดียว ส่วน backend เขียนเฉพาะ system-* และ subscription-* (SystemAdminEndpoints.cs, SystemSubscriptionEndpoints.cs) ช่อง filter Role (:102) ก็เป็นของ roles ที่ไม่มีแล้ว
- ผลกระทบ: เลือก action ใดใน dropdown ก็ได้ผลว่างทุกครั้ง และกรองหา action ที่มีอยู่จริง เช่น system-credential-rotated ไม่ได้ ส่วนแถว system-* แสดงเป็น key ดิบสีเทาทั้งหมด
- แก้: แทน ACTION_LABELS ด้วยรายการ action ที่ backend เขียนจริง (system-created, system-credential-rotated, system-token-issued/revoked, system-grant/revoke, subscription-* ฯลฯ) พร้อม tone ให้ถูก (revoke/delete = coral) และลบ filter Role หรือเปลี่ยนเป็น System
- Verifier: ยืนยันแล้ว: grep src ไม่พบจุดไหนเขียน action grant, revoke, permission-* หรือ role-* อีกแล้ว (มีแค่ doc comment ใน PermissionAuditEntry.cs:18) ตัวเขียนจริงมีแค่ system-* และ subscription-* แต่ ACTION_LABELS ซึ่งใช้เป็น option ของ dropdown มีแค่ action เก่า จึงกรองหา action ที่มีอยู่จริงไม่ได้ ส่วนแถวทั้งหมดขึ้นเป็น key ดิบ tone ink และ filter Role ก็ไม่ match อะไรเพราะ role เป็น null

### M4. modal ที่เขียนเองใน IAM ไม่มี role=dialog, Esc, focus trap และปุ่ม X ไม่มี aria-label — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:1720`
- ปัญหา: `ModalShell` (ใช้กับ 5 modal), `CreateSystemModal` (iam-systems-experience.tsx:378) และ `AddSubscriptionModal` (iam-system-subscriptions-experience.tsx:261) เป็น div fixed ที่เขียนเอง ไม่มี role="dialog" หรือ aria-modal ไม่ย้าย focus เข้า modal ไม่ดัก Esc และคลิก backdrop แล้วไม่ปิด ปุ่ม X ที่ :1726 และ subscriptions:265 ก็ไม่มี aria-label (ไม่ได้อยู่ใน AnimatePresence จึงไม่เจอปัญหา stranded backdrop)
- ผลกระทบ: ผู้ใช้ keyboard กด Tab แล้ว focus หลุดไปที่หน้าหลังด้านล่าง ปิดด้วย Esc ไม่ได้ screen reader อ่านปุ่มปิดเป็นแค่ "button" และพฤติกรรมไม่เหมือน dialog อื่นในแอป (replay-dialog, carrier-form-dialog)
- แก้: รวมเป็น dialog shell ตัวเดียวตาม pattern ของ replay-dialog.tsx:98-117 คือ OverlayBackdrop + motion panel, role=dialog, aria-modal, Esc handler (ปิดไม่ได้ขณะ submitting) และ autoFocus ที่ input แรก จากนั้นใส่ aria-label="Close" ให้ปุ่ม X
- Verifier: ยืนยันแล้ว: ModalShell (:1720-1735) และ modal อีก 2 ตัวเป็น div fixed ธรรมดา ไม่มี role=dialog/aria-modal ไม่มี Esc handler ไม่มี focus management คลิก backdrop ไม่ปิด และปุ่ม X ไม่มี aria-label ซึ่งขัดกับ house rule ข้อ Dialogs (review-frontend.md:81) ส่วนการที่ไม่ได้อยู่ใน AnimatePresence ก็ทำให้ไม่เข้าข่ายปัญหา stranded backdrop ระดับ HIGH ตามที่ reviewer ระบุไว้ถูกต้อง

### M5. dark mode: ปุ่มรอง, input และ banner ใน IAM ใช้ bg-white หรือ rose-50 โดยไม่มี dark variant — CONFIRMED · ✅ แก้แล้ว `c01e55a7`, `f6b256a0`
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:478`
- ปัญหา: ปุ่ม `bg-white ... text-[var(--color-ink-600)]` ไม่มี `dark:bg-*` ใน dark จึงเป็นตัวอักษร #a6b0cb บนพื้นขาว (ประมาณ 2:1) จุดที่เกิด: :402, 478, 700, 729, 765, 773, 783, 874, 901 นอกจากนี้ error banner ที่ :423 และ :1749, iam-system-subscriptions-experience.tsx:133 ก็ไม่มี dark variant รวมทั้ง input/select และ toneClass ใน iam-audit-log-experience.tsx:114, 242, 248 และป้าย amber ใน IssueTokenModal :1843 (globals.css:16 ระบุชัดว่าคลาส literal เหล่านี้ต้องมี `dark:` เอง)
- ผลกระทบ: ปุ่ม Edit, Configure, Reveal token, Refresh now, Deactivate และ Manage subscriptions อ่านยากมากใน dark mode และเป็นกล่องขาวจ้าที่ไม่เข้ากับหน้าอื่น
- แก้: เติม `dark:bg-white/[0.05] dark:border-white/[0.08]` ให้ปุ่มรองตามที่ iam-systems-experience.tsx:272 ทำไว้แล้ว และให้ banner ใช้ token `bg-[var(--color-coral-soft)] text-[var(--color-coral)]` แบบ replay-dialog.tsx:207
- Verifier: ยืนยันแล้ว: ปุ่มที่ :402, 478, 700, 729, 765, 773, 874, 901 ใช้ bg-white text-ink-600 โดยไม่มี dark:bg-* (ink-600 ใน dark = #a6b0cb บนพื้นขาว) ขณะที่ iam-systems-experience.tsx:272 มี dark:bg-white/[0.05] ส่วน error banner :423, subscriptions:133,306, input/select และ toneClass ใน audit-log ก็ไม่มี dark variant ซึ่ง globals.css:16 ระบุไว้ชัดว่าต้องใส่ (บรรทัด 783 เป็น onClick ไม่ใช่ className แต่ปุ่มตัวเดียวกันก็มีปัญหานี้)

### M6. DataFreshnessChip ได้เวลาที่ client fetch เสร็จ ไม่ใช่ lastEventAt ของ projection — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/dashboard/kpi-rail.tsx:104`
- ปัญหา: `<DataFreshnessChip lastEventAt={lastUpdated ?? data.lastEventAt} />` โดย `lastUpdated` คือ `new Date()` ตอนที่ fetch สำเร็จ (use-projection-poll.ts:51) และมีค่าเสมอเมื่อมี data แบบเดียวกันอยู่ที่ dispatch-funnel.tsx:104, orders-analysis-experience.tsx:97 และ robots-analysis-experience.tsx:97 ซึ่งขัดกับ contract ของ chip เอง (data-freshness-chip.tsx:18-20 "NOT the wall clock when the API responded")
- ผลกระทบ: ถ้า projector ค้างหรือ outbox ตามไม่ทัน แต่ API ยังตอบได้ chip จะขึ้น "Live"/"Updated Ns ago" ตลอด สถานะ Stale สีอำพันจะไม่ขึ้นเลย operator จึงเชื่อตัวเลขที่หยุดนิ่งไปแล้ว
- แก้: ส่ง `data.lastEventAt` อย่างเดียว ถ้ากังวลว่าช่วงเงียบตอนกลางคืนจะขึ้น Stale ให้แยกเป็นสอง chip คือ "Fetched" (จาก lastUpdated) กับ "Last event" (จาก lastEventAt) แทนการใช้ `??` รวมกัน
- Verifier: ยืนยันแล้ว: ทั้ง 4 จุดใช้ `lastUpdated ?? data.lastEventAt` โดย lastUpdated มาจาก new Date() ตอน fetch สำเร็จ และมีค่าทุกครั้งที่มี data จึงไม่เคย fallback ไปใช้ lastEventAt เลย chip จะไม่ขึ้น stale ตราบใดที่ API ยังตอบได้ ซึ่งขัดกับ doc ของ chip เอง commit f2dbc140 ไม่ได้บอกเหตุผลไว้ว่าตั้งใจทำแบบนี้ แต่อาจตั้งใจเลี่ยงไม่ให้ขึ้น stale ในช่วงที่ไม่มี event เลยให้ MEDIUM

### M7. KpiRail ไม่แสดง error เมื่อโหลดไม่สำเร็จ จะเห็นเป็น 0 ทุก tile — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/dashboard/kpi-rail.tsx:52`
- ปัญหา: destructure มาแค่ `{ data, lastUpdated, refresh }` ไม่ได้ใช้ `error` หรือ `loading` และบรรทัด 74 fallback เป็น `EMPTY_TOTALS` ถ้าโหลดครั้งแรกล้มเหลว (401/429/500) tile จะแสดง 0 ตลอด ถ้าล้มเหลวภายหลังก็จะค้างค่าเก่าโดยไม่มีอะไรบอก (ต่างจาก DispatchFunnel ที่แสดง error ไว้ที่บรรทัด 129-133)
- ผลกระทบ: "Lost (24h) 0" หรือ "Confirmed 0" ดูเหมือนข้อมูลจริง ทั้งที่จริงคือ API พัง operator ไม่มีทางรู้
- แก้: ดึง `error` และ `loading` มาใช้ ตอน `data === null` ให้แสดง skeleton หรือ "—" แทน 0 และตอนมี error ให้แสดงแถบ coral แบบเดียวกับ dispatch-funnel.tsx:129
- Verifier: ยืนยันแล้ว: KpiRail destructure มาแค่ { data, lastUpdated, refresh } แล้ว fallback เป็น EMPTY_TOTALS ทุก tile จึงแสดง 0 เมื่อโหลดล้มเหลว และไม่มีการแสดง error เลย ต่างจาก dispatch-funnel.tsx:129-133 ที่แสดง error ไว้

### M8. tile "In flight" (ติดป้าย Live) นับจำนวนครั้งที่เปลี่ยนสถานะ ไม่ใช่จำนวน order ที่กำลังวิ่งอยู่ — CONFIRMED
- กลุ่ม: analytics · uiux
- ที่: `frontend/components/dashboard/kpi-rail.tsx:131`
- ปัญหา: `inFlight = t.dispatched + t.inProgress` แต่ OrderFunnelHourly นับ event การเปลี่ยนสถานะรายชั่วโมง (src/Modules/DeliveryOrder/.../OrderFunnelProjector.cs:11-12) ผลรวม 24h จึงนับ order เดียวซ้ำ 2 ครั้ง (Dispatched แล้ว InProgress) และนับรวม order ที่ complete ไปแล้วด้วย tile นี้ยังติด StatusPulse "Live" (บรรทัด 143-147) ส่วน orders-analysis-experience.tsx:77 tile "Dispatched" ก็บวกแบบเดียวกัน
- ผลกระทบ: ตัวเลข "In flight" สูงเกินจริงประมาณ 2 เท่า และไม่ได้บอกว่าตอนนี้มีงานค้างอยู่เท่าไร ซึ่งเป็นสิ่งที่ป้าย Live สื่อ
- แก้: เปลี่ยน label เป็น "Dispatched (24h)" ใช้ `t.dispatched` อย่างเดียว และเอา pulse ออก ถ้าต้องการจำนวนที่กำลังวิ่งจริงให้ดึง count ปัจจุบันจาก OrderListView แทน funnel ส่วน orders-analysis:77 ก็ไม่ควรบวก inProgress เข้าไป
- Verifier: ยืนยันแล้ว: OrderFunnelProjector นับเพิ่มคอลัมน์ตาม event การเปลี่ยนสถานะ (Dispatched และ InProgress แยกกัน) ส่วน buildKpis คำนวณ inFlight = dispatched + inProgress พร้อม pulse "Live" order เดียวที่ผ่านทั้งสองสถานะจึงถูกนับสองครั้ง และนับรวม order ที่จบไปแล้วด้วย ตัวเลขนี้จึงไม่ใช่จำนวนงานที่กำลังวิ่งอยู่จริง ส่วน orders-analysis:77 บวกแบบเดียวกัน

### M9. Reports poll ทุก 5 นาทีด้วยช่วงเวลาที่ถูกตรึงไว้ตอนเปิดหน้า — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/reports/reports-experience.tsx:38`
- ปัญหา: `useMemo(() => buildWindowRange(windowKey), [windowKey])` คำนวณ `toUtc` (ต้นชั่วโมงถัดไป) แค่ครั้งเดียว การ poll ทุก 5 นาทีจึงส่ง from/to ชุดเดิมตลอด ต่างจาก kpi-rail.tsx:49 และ dispatch-funnel.tsx:56 ที่เรียก `defaultWindow()` ใหม่ในทุก fetch
- ผลกระทบ: เปิดหน้า Reports ทิ้งไว้เกินต้นชั่วโมงถัดไป order ใหม่จะไม่ขึ้นใน report อีกเลยจนกว่าจะกดเปลี่ยนช่วงเวลา ทั้งที่หน้ายัง poll อยู่ ทำให้การ poll ไม่มีประโยชน์
- แก้: ส่ง `windowKey` (ไม่ใช่ range) ลงไปที่ report แล้วเรียก `buildWindowRange(windowKey)` ภายใน fetcher ทำพร้อมกับการแก้ finding เรื่องเปลี่ยน window แล้วไม่ refetch ได้เลย
- Verifier: ยืนยันแล้ว: buildWindowRange ถูก useMemo ไว้ด้วย [windowKey] ทำให้ toUtc ตรึงไว้ที่ต้นชั่วโมงถัดไปนับจากตอนเลือก ทุกรอบ poll 5 นาทีจึงส่ง range เดิมซ้ำ หลังผ่านชั่วโมงนั้นไปแล้ว order ใหม่จะไม่เข้ามาใน report อีก ต่างจาก kpi-rail และ orders-analysis ที่สร้าง window ใหม่ภายใน fetcher ทุกครั้ง

### M10. ข้อความ error ที่แสดงให้ผู้ใช้เป็น JSON ดิบ {"message":...} เพราะ client อ่านด้วย res.text() — CONFIRMED
- กลุ่ม: api-layer · code
- ที่: `frontend/lib/api/delivery-orders.ts:155`
- ปัญหา: proxyToBackend (lib/api/proxy-helpers.ts:126-170) แปลงทุก non-2xx เป็น JSON `{ message }` แต่ client หลายจุดใส่ res.text() ลง Error ตรงๆ ได้แก่ confirmItemPod (delivery-orders.ts:155), getStations / getMapRobotPositions / listMaps (facility.ts:64, 235, 244), searchItems (items.ts:111), searchWmsLocations (wms-locations.ts:54) และ send() ใน fleet-attachments.ts:48 ที่ตัดได้แค่เครื่องหมายคำพูด ไม่ได้ parse JSON
- ผลกระทบ: ผู้ใช้เห็นข้อความแบบ `{"message":"Couldn't reach the backend service."}` หรือ `{"message":"already has 10 images..."}` ใน toast, dialog และ banner เช่น podError ใน orders-experience.tsx:942 และ error ของ station combobox ใน useStationOptions อ่านยากและดูเป็นบั๊ก
- แก้: parse JSON แล้วดึง message/detail แบบเดียวกับ unwrap() ใน delivery-orders.ts:275 ดีที่สุดคือรวมเป็น helper ตัวเดียว เช่น readApiError(res) ใน lib/api/errors.ts ที่เรียก throwIfRateLimited ก่อน แล้วอ่าน message ?? detail ?? error ให้ทุกไฟล์ใช้ร่วมกัน
- Verifier: route ที่เกี่ยวข้อง (pod-scan, facility/stations, items, wms/locations, fleet/attachments) ใช้ proxyToBackend ทั้งหมด ซึ่งคืน non-2xx เป็น NextResponse.json({ message }) ส่วน confirmItemPod, getStations, getMapRobotPositions, listMaps, searchItems, searchWmsLocations ใส่ res.text() ลงใน Error ตรงๆ และ fleet-attachments send() ตัดได้แค่ quote ที่หัวกับท้าย ผู้ใช้จึงเห็น {"message":...} ดิบ ต่างจาก unwrap() ที่ parse JSON ให้

### M11. route export CSV ไม่มี try/catch และ timeout และทิ้งข้อความ error จาก backend — CONFIRMED
- กลุ่ม: api-layer · code
- ที่: `frontend/app/api/reports/orders-export/route.ts:22`
- ปัญหา: orders-export, trips-export และ jobs-export (โค้ดเหมือนกัน) เรียก fetch(url) โดยไม่มี try/catch และ AbortController ถ้า backend ล่มหรือค้าง จะเกิด unhandled rejection หรือ request แขวนไม่มีกำหนด ถ้าไม่ ok ก็คืน `Export failed: ${status}` (บรรทัด 31-35) ทิ้ง message/detail ของ backend และ Retry-After ไป ซึ่งต่างจาก proxyToBackend ที่จัดการทั้งสามเรื่องนี้
- ผลกระทบ: ปุ่ม CSV ใน components/reports เป็น <a href> ธรรมดา เมื่อ export ล้มเหลว browser จะออกจากแอปไปหน้า JSON ดิบ `{"message":"Export failed: 403"}` หรือหน้า 500 ของ Next ผู้ใช้ไม่รู้สาเหตุ และกลับสู่ filter เดิมได้ยาก
- แก้: แยก helper streamFromBackend(path, search, accept) ใน lib/api/proxy-helpers.ts ที่ใช้ timeout, try/catch (คืน 502/504) และดึงข้อความ error แบบเดียวกับ proxyToBackend แล้วให้ทั้งสาม route เรียกใช้ ฝั่ง UI ควรเพิ่ม attribute download หรือ fetch แล้วเรียก blob download พร้อม toast เมื่อ error
- Verifier: orders-export/route.ts เรียก fetch โดยไม่มี try/catch และ AbortController และเมื่อไม่ ok ก็คืนแค่ `Export failed: ${status}` ทิ้งข้อความจาก backend; components/reports ใช้ <a href={csvHref}> ธรรมดา ถ้า export ล้ม browser จะออกจากแอปไปหน้า JSON ดิบหรือหน้า 500 ของ Next (fetch throw = unhandled ใน route handler) ส่วนเรื่องแขวนนานขึ้นกับ backend จริงแต่โค้ดไม่มีกลไกป้องกัน

### M12. WmsLocationCombobox: กด Enter เลือก location จากผลค้นหาชุดเก่า — CONFIRMED · ✅ แก้แล้ว `3d598f59, 0b43e4f0`
- กลุ่ม: core · code
- ที่: `frontend/components/primitives/wms-location-combobox.tsx:135`
- ปัญหา: useWmsLocationSearch (lib/api/wms-locations.ts:80-113) debounce ไว้ 300ms และระหว่างรอ debounce กับระหว่าง fetch จะยังเก็บ items ของ query ก่อนหน้าไว้ ส่วน highlight จะ reset ก็ต่อเมื่อ items เปลี่ยน ถ้าผู้ใช้พิมพ์ 'STF_35' แล้วกด Enter ภายใน ~300ms+latency หรือสแกนด้วย wedge ที่ส่ง Enter มาด้วย onKeyDown จะ commit(items[highlight]) ซึ่งเป็นแถวแรกของผลค้นหาชุดก่อน ไม่ใช่ STF_35
- ผลกระทบ: pickup/drop location ของ manual order ผิดตัวโดยไม่มีอะไรเตือน หุ่นหรือคนจะวิ่งไปผิดจุด
- แก้: เก็บ query ที่ items ชุดปัจจุบันตอบไว้ (เช่นให้ hook คืน resolvedQuery) แล้วยอมให้ Enter commit ได้ก็ต่อเมื่อ !loading และ resolvedQuery === query ถ้ามีแถวที่ code ตรงกับ query แบบ exact ให้เลือกแถวนั้นก่อน ถ้ายังไม่พร้อมให้ตั้ง flag ไว้แล้ว commit หลังผลมาถึง
- Verifier: useWmsLocationSearch เก็บ items ชุดเก่าไว้ระหว่าง debounce 300ms/fetch และ highlight reset เฉพาะเมื่อ items เปลี่ยน onKeyDown Enter (บรรทัด 135-137) commit(items[highlight]) โดยไม่เช็ก loading/query จริง แต่เคส wedge scanner ไม่เกิดเพราะ handheld ไม่ส่ง Enter (house rule) และหลัง commit ช่องแสดง code ที่เลือกให้เห็น ไม่ได้เงียบทั้งหมด จึงลดเป็น MEDIUM

### M13. WmsLocationCombobox ใช้สี gray/blue/white ตรงๆ ใน dark mode เลยอ่านตัวอักษรไม่ออก — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/primitives/wms-location-combobox.tsx:166`
- ปัญหา: wrapper ใช้ bg-white, border-gray-300 และ focus-within:border-blue-500 ส่วน panel ที่บรรทัด 216 ใช้ bg-white กับ border-gray-200 ไม่มี dark: variant เลย ทั้ง input และแถวในรายการไม่ได้ตั้งสีตัวอักษร จึงได้ body color มา ซึ่งใน .dark คือ --color-ink-900 #f4f7fe (globals.css:199, 135) ผลคือตัวอักษรเกือบขาวบนพื้นขาว นอกจากนี้ยังไม่เข้าชุดกับ StationCombobox ที่อยู่ข้างกันใน create-dialog ซึ่งใช้ token
- ผลกระทบ: ใน dark mode ช่อง pickup/drop ของ manual order มองไม่เห็นสิ่งที่พิมพ์ และอ่านรายการ location ไม่ได้
- แก้: เปลี่ยนไปใช้ class ชุดเดียวกับ StationCombobox: input ใช้ bg-white/65 + dark:bg-white/[0.04] + text-[var(--color-ink-900)] ส่วน panel ใช้ bg-[var(--color-popover)]/95 + border-[var(--color-ink-100)] แถว highlight ใช้ bg-[var(--color-brand-500)]/15 chip inactive ใช้ --color-amber
- Verifier: บรรทัด 166 และ 217 ใช้ bg-white/border-gray/blue ไม่มี dark: variant input และแถว option ไม่ได้ตั้งสีตัวอักษร จึงได้ body color ซึ่งใน .dark คือ ink-900 #f4f7fe (globals.css:130, 199) panel อยู่ใน body portal จึงได้สีนั้นเช่นกัน ใช้จริงใน create-dialog.tsx:719/735 ข้าง StationCombobox ที่ใช้ token

### M14. UomCombobox: พิมพ์หน่วยให้ตรงตัวแล้วกด Enter กลับได้หน่วยอื่น — CONFIRMED · ✅ แก้แล้ว `8021354a`
- กลุ่ม: core · code
- ที่: `frontend/components/primitives/uom-combobox.tsx:92`
- ปัญหา: filtered คืน suggestions ทั้งชุดเมื่อค่าที่พิมพ์ตรงกับ suggestion ตัวใดตัวหนึ่งพอดี (บรรทัด 61) และ highlight ถูก reset เป็น 0 ทุกครั้งที่ value เปลี่ยน ถ้าพิมพ์ 'KG' แล้วกด Enter จะได้ pick(filtered[0]) ซึ่งคือ suggestion ตัวแรก (เช่น 'EA') แทน KG
- ผลกระทบ: UOM ของ order item ถูกแทนที่เงียบๆ ด้วยหน่วยที่ผิด
- แก้: ตอน filter เข้าโหมด exact match ให้ตั้ง highlight ไปที่ index ของตัวที่ตรง หรือใน Enter ให้เช็กก่อนว่าถ้า suggestions มีตัวที่ normalize แล้วเท่ากับ value ให้แค่ setOpen(false) ไม่ต้อง pick
- Verifier: filtered (บรรทัด 59-64) คืน suggestions ทั้งหมดเมื่อ value ตรง exact และ highlight reset เป็น 0 เมื่อ value เปลี่ยน (66-68) Enter (92-97) จึง pick(filtered[0]) แทนค่าที่พิมพ์ ถ้าค่าที่ตรงไม่ใช่ตัวแรก UOM ถูกแทนที่จริง

### M15. Hub start ครั้งแรกล้มแล้ว realtime จะไม่กลับมาจนกว่าจะ reload หน้า — CONFIRMED · ✅ แก้แล้ว `c38ff16d`
- กลุ่ม: core · code
- ที่: `frontend/lib/realtime/signalr-client.ts:102`
- ปัญหา: ถ้า connection.start() ครั้งแรกล้ม (backend ยังไม่ขึ้น หรือ hub-token ได้ 401) code จะแค่ log แล้วเก็บ connection ที่อยู่ในสถานะ Disconnected ไว้ใน map ตลอดไป withAutomaticReconnect ทำงานเฉพาะหลังเคยต่อสำเร็จแล้วเท่านั้น ส่วน useHubSubscription ทำแค่ setError ไม่ retry ใน cycleConnectionForDrain (บรรทัด 125-131) comment บอกว่า 'automatic reconnect will pick up' แต่หลังจาก stop() เอง auto-reconnect จะไม่ทำงาน
- ผลกระทบ: หน้าโหลดได้แต่ไม่อัปเดต live ไปตลอด session ของ SPA แม้จะย้ายหน้าแล้วก็ตาม (ตรงกับอาการ 'page loads but never updates') เหลือแค่ polling ทำงาน
- แก้: ถ้า start ล้ม ให้ลบ entry ออกจาก map (หรือ retry ด้วย backoff เดียวกับ nextRetryDelayInMilliseconds) จะได้ให้ subscriber ถัดไปหรือ retry timer เรียก start ใหม่ได้ ใน drain cycle ถ้า start ล้มให้ schedule retry เอง
- Verifier: signalr-client.ts:102-110 catch แค่ console.warn แล้วเก็บ entry ไว้ใน map ตลอด getHub คืน existing โดยไม่เช็ก state, ensureStarted คืน ready ที่ resolve แล้ว withAutomaticReconnect ไม่ retry initial start และหลัง stop() เองใน cycleConnectionForDrain ก็ไม่ auto-reconnect ตามที่ comment อ้าง ไม่มีที่ไหนเรียก disposeHub อัตโนมัติ

### M16. usePoolTrips กลบ error ไว้ หน้า pool เลยแสดงว่าว่าง — CONFIRMED · ✅ แก้แล้ว `2c01dc95`, `f0a7c44a`
- กลุ่ม: core · code
- ที่: `frontend/lib/hooks/use-pool-trips.ts:67`
- ปัญหา: ใน catch ของ refetch มีการ dispatch REFETCH ด้วย trips: [] และ reducer ก็ตั้ง error: null เสมอ ทำให้ state.error ไม่มีทางมีค่า ถ้า GET /api/operator/trips/pool ล้ม (offline, 5xx, 401) operator จะเห็น pool ว่างแทนข้อความ error นอกจากนี้ refetch ที่ยิงพร้อมกันตอน mount กับตอน connected ไม่ได้กันผลตอบกลับที่มาไม่ตามลำดับ
- ผลกระทบ: operator คิดว่าไม่มีงานให้ claim ทั้งที่จริงคือโหลดไม่สำเร็จ งานใน pool เลยค้าง
- แก้: เพิ่ม action FAILED ที่เก็บ trips เดิมไว้แล้วตั้ง error: message ให้ UI แสดง error พร้อมปุ่ม retry และใส่ request-id/AbortController เพื่อทิ้งผลตอบกลับที่เก่ากว่า
- Verifier: catch บรรทัด 67-69 dispatch REFETCH trips: [] และ reducer ตั้ง error: null เสมอ state.error จึงไม่มีวันมีค่า ซ้ำร้าย pool-trips-list.tsx:21 ไม่ได้อ่าน error เลย operator เห็น pool ว่างเมื่อโหลดล้ม

### M17. Combobox ทุกตัว: กด Tab ออกแล้ว list ยังเปิดค้าง, ไม่มี ARIA ของ combobox และปุ่ม Clear ใช้คีย์บอร์ดไม่ได้ — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/primitives/station-combobox.tsx:134`
- ปัญหา: onBlur แค่ setAutofillGuard(true) ไม่ได้ปิด list เพราะ listener ปิดมีแค่ mousedown ถ้ากด Tab ออก list ที่อยู่ใน portal จะยังลอยค้าง และ input ยังแสดง query ที่ยังไม่ commit ทำให้ดูเหมือนเลือกไปแล้ว input ไม่มี role="combobox", aria-expanded, aria-controls หรือ aria-activedescendant (UomCombobox มีบางส่วน) ปุ่ม Clear ผูกไว้แค่ onMouseDown (บรรทัด 156) จึงกด Enter/Space ไม่ได้ ปัญหาเดียวกันมีใน int-id-combobox.tsx:400/419 และ action-template-combobox.tsx
- ผลกระทบ: ผู้ใช้คีย์บอร์ดและ screen reader ไม่รู้ว่า option ไหนถูก highlight ล้างค่าไม่ได้ และเข้าใจผิดว่าได้เลือกค่าแล้ว
- แก้: ใน onBlur ให้ setOpen(false) และ setQuery("") (คลิกที่ list ใช้ preventDefault ใน mousedown อยู่แล้วจึงไม่กระทบ) ใส่ role=combobox, aria-expanded, aria-controls={listId}, aria-activedescendant ลงใน input และ id ให้แต่ละ option ปุ่ม Clear ให้ใช้ onClick (คง onMouseDown preventDefault ไว้กัน blur) ควรรวมทั้ง 5 ตัวเป็น base combobox ตัวเดียว
- Verifier: station-combobox onBlur (134) แค่ setAutofillGuard ไม่ปิด list ตัวปิดมีแค่ mousedown จึง Tab ออกแล้ว list ค้าง input ไม่มี role=combobox/aria-activedescendant ปุ่ม Clear ผูก onMouseDown อย่างเดียว (156) กด Enter/Space ที่ปุ่มจะยิงแค่ click จึงล้างค่าด้วยคีย์บอร์ดไม่ได้ (wms-location ใช้ onClick ซึ่งถูกต้อง)

### M18. เมนู rail มีลิงก์ที่ไปหน้า 404 และปุ่ม 'Active alerts' ที่กดแล้วไม่ทำอะไร — CONFIRMED · ✅ แก้แล้ว `8a6f4171`
- กลุ่ม: core · uiux
- ที่: `frontend/components/shell/left-rail.tsx:189`
- ปัญหา: href '/equipment/auto-doors' (บรรทัด 189), '/fleet/robots' (บรรทัด 145) และ '/fleet/drivers' (บรรทัด 152) ไม่มี app/<route>/page.tsx อยู่จริง (ตรวจด้วย ls แล้ว) ส่วน 'Active alerts' (บรรทัด 238) ไม่มี href แต่มี StatusPulse สี coral (สีของ failure) กระพริบอยู่ตลอด
- ผลกระทบ: ผู้ใช้ทุกคนเจอหน้า 404 จากเมนูหลัก และเห็นสัญญาณ alert สีแดงปลอมทุกหน้าที่ใช้ rail
- แก้: เอา 3 รายการที่ยังไม่มีหน้าออก (หรือใส่ requires/flag ซ่อนไว้) และเอา badge ของ Active alerts ออกจนกว่าจะต่อกับข้อมูล alert จริง
- Verifier: ตรวจแล้ว app/fleet มีแค่ carriers และ carrier-types ไม่มี robots/drivers และไม่มี app/equipment เลย ทั้ง 3 ไม่มี requires จึงทุกคนเห็น 'Active alerts' (238) ไม่มี href และ badge:true ทำให้ RailButton แสดง StatusPulse tone coral ตลอด

### M19. GreetingStrip และ TopNav บน /dashboard ยังแสดงข้อมูล placeholder ปลอม — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/shell/greeting-strip.tsx:21`
- ปัญหา: มีข้อความ hardcode เช่น 'All operations nominal', '07:42 GMT+7', 'Good evening' (ไม่ว่าเวลาจริงจะเป็นเท่าไร), 'Three high-priority dispatches ... Eastern Seaboard' ปุ่ม 'Ask Co-pilot' และ 'New dispatch' (บรรทัด 35, 39) ไม่มี onClick ใน top-nav.tsx:13 มี nav 'Messages/Discover/Wallet/Projects' ที่กดแล้วไม่ไปไหน ช่อง search ไม่ทำงาน และ Notifications มี badge coral ค้างไว้ตลอด (บรรทัด 116)
- ผลกระทบ: ห้อง ops เห็นสถานะ 'ปกติ' และงานด่วนที่ไม่มีอยู่จริงบนหน้า overview ซึ่งอาจทำให้ตัดสินใจผิด ส่วนปุ่มที่กดแล้วเงียบก็ทำให้ไม่เชื่อถือ UI
- แก้: ผูก greeting/สถานะกับข้อมูลจริง (เช่น vendor-health summary) หรือเอาออก แล้วซ่อนปุ่มและ nav ที่ไม่มีปลายทาง
- Verifier: greeting-strip.tsx hardcode 'All operations nominal', '07:42 GMT+7', 'Good evening', 'Three high-priority dispatches ... Eastern Seaboard' ปุ่ม 2 ปุ่มไม่มี onClick ใช้ใน app/dashboard/page.tsx top-nav.tsx:13 มี navItems Messages/Discover/Wallet/Projects และ Notifications badge ค้าง TopNav ใช้ทั้ง dashboard และหน้า admin ทุกหน้า

### M20. DataRow: แถวที่ focus อยู่อ่านไม่ออกใน dark mode และ role=button ทำลาย semantics ของตาราง — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/primitives/data-table/data-row.tsx:73`
- ปัญหา: focus-visible:bg-white/60 ไม่มี dark: variant ใน dark mode ตัวอักษร ink-900 (#f4f7fe) จึงอยู่บนพื้นขาว 60% และใส่ role="button" ไว้บน <tr> (บรรทัด 67) ทำให้ screen reader ไม่อ่าน cell ตามหัวคอลัมน์ และยังซ้อน interactive กับ IdCell หรือปุ่ม row menu ที่อยู่ข้างใน
- ผลกระทบ: ผู้ใช้คีย์บอร์ดใน dark mode อ่านแถวที่เลือกอยู่ไม่ได้ ใช้ได้กับทุกตารางของระบบเพราะทุกหน้าใช้ primitive นี้
- แก้: เพิ่ม dark:focus-visible:bg-white/[0.06] ส่วน a11y ให้ตัด role=button ออกแล้ววาง <button>/<Link> ที่ครอบ cell หลัก (เช่น OrderRef) แทน หรืออย่างน้อยให้คง role=row ไว้แล้วใช้ aria-label บอก action
- Verifier: data-row.tsx:73 focus-visible:bg-white/60 ไม่มี dark: variant ขณะที่ hover มี dark:hover:bg-white/[0.03] และบรรทัด 67 ใส่ role=button บน <tr> ซึ่งทับ implicit role=row จริง กระทบทุกตารางที่ใช้ DataRow แบบ interactive แต่เฉพาะผู้ใช้คีย์บอร์ด

### M21. ปุ่ม action ใน drawer เงียบหายเมื่อ order ไม่อยู่ใน list หน้าปัจจุบัน — CONFIRMED · ✅ แก้แล้ว `07e80468`
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:575`
- ปัญหา: runAction เริ่มด้วย const target = orders.find(o => o.id === id); if (!target) return; แต่ drawer เปิด order ที่ไม่อยู่ในหน้าปัจจุบันได้ เช่นกด OrderRef ใน Trip drawer ที่ซ้อนอยู่ (onOpenOrder บรรทัด 886) หรือ order ถูก poll/filter ออกจาก list ไปแล้ว
- ผลกระทบ: ในกรณีนี้ปุ่ม Cancel order, Hold, Reject, Reopen, Redispatch, Abandon, Reorder และ Submit ใน drawer กดแล้วไม่เกิดอะไรขึ้นเลย และไม่มี toast หรือ error แจ้ง
- แก้: ส่ง DeliveryOrderDetailDto จาก drawer มาให้ runAction โดยตรง (drawer มี data อยู่แล้ว) หรือ fallback ไปเรียก getOrder(id) เมื่อหาใน orders ไม่เจอ ห้าม return เงียบ
- Verifier: runAction บรรทัด 575-576 ใช้ orders.find(...) และถ้าไม่เจอก็ return เงียบ ทางเข้าที่ทำให้หาไม่เจอมีจริง: onOpenOrder (บรรทัด 886) ที่ TripDetailDrawer ใช้กระโดดไป order อื่นซึ่งอาจไม่อยู่ในหน้าปัจจุบัน ทุกปุ่มใน footer ของ drawer (detail-drawer.tsx:543-653) ส่งผ่าน onAction ไปที่ runAction จึงกดแล้วไม่เกิดอะไรขึ้นและไม่มี toast แจ้ง

### M22. Deep link ?page=N ถูก reset เป็นหน้า 1 ทุกครั้งที่ mount — CONFIRMED · ✅ แก้แล้ว `6377608d`
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:301`
- ปัญหา: effect ที่ setPage(1) เมื่อ filter เปลี่ยน ทำงานตอน mount ด้วย (effect ทำงานรอบแรกเสมอ) ค่า page ที่ hydrate จาก URL (บรรทัด 148-151) จึงถูกเขียนทับเป็น 1 ทันที และ fetch ยิง 2 ครั้ง (page N แล้วตามด้วย page 1)
- ผลกระทบ: refresh, back หรือลิงก์ที่แชร์มาที่หน้า 3 จะกลับไปหน้า 1 เสมอ ขัดกับที่ comment บอกว่า restore view ได้ และเปลือง request หนึ่งครั้ง
- แก้: ข้ามรอบแรกด้วย ref (const first = useRef(true); if (first.current) { first.current = false; return; }) หรือย้ายการ reset page ไปไว้ใน setter ของ filter แต่ละตัวแทนการใช้ effect
- Verifier: effect บรรทัด 299-303 ไม่มี guard สำหรับรอบแรก ตอน mount จึงเรียก setPage(1) เสมอ แม้ page จะ hydrate มาจาก ?page=N (บรรทัด 148-151) ผลคือ fetch ยิงด้วย page N หนึ่งครั้งแล้วยิงซ้ำด้วย page 1 และ effect ที่ mirror กลับ URL ก็ลบ page ออก deep link และการ refresh จึงกลับไปหน้า 1 ทุกครั้ง

### M23. กด Escape ใน dialog หรือ trip drawer ที่ซ้อนอยู่ ทำให้ order drawer ปิดไปด้วย — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/detail-drawer.tsx:163`
- ปัญหา: overlay ทุกตัวผูก keydown listener บน window เอง ได้แก่ detail-drawer:165, trip-detail-drawer:124, cancel-dialog:40, state-action-dialog:131, pod-scan-dialog:77, reopen-dialog:46 และ redispatch-dialog:45 โดยไม่มีตัวไหนเรียก stopPropagation หรือเช็กว่าตัวเองอยู่บนสุด ส่วน comment ที่บรรทัด 670-674 อ้างว่า Escape ปิดเฉพาะ trip drawer ซึ่งไม่จริง
- ผลกระทบ: ผู้ใช้เปิด Hold, POD หรือ Cancel dialog จาก drawer แล้วกด Esc จะปิดทั้ง dialog และ drawer ทำให้หลุดบริบท ต้องหา order ใหม่ในตาราง และอาจปิด drawer ระหว่างที่ request ยังทำงานอยู่
- แก้: ทำ overlay stack กลาง (เช่น hook useEscapeLayer ที่ register ลำดับชั้นแล้วให้เฉพาะชั้นบนสุดตอบ Esc) หรืออย่างน้อยให้ drawer ignore Esc เมื่อ openTripId != null และให้ dialog เรียก e.stopImmediatePropagation() โดย listen ใน capture phase
- Verifier: ทุก overlay ผูก keydown บน window เอง (detail-drawer:165, trip-detail-drawer:124, state-action-dialog:131, pod-scan-dialog:77, reopen-dialog:46, redispatch-dialog:45, cancel-dialog:40) จาก grep ไม่พบ stopPropagation หรือการเช็ก layer เลย กด Esc ครั้งเดียวจึงยิงทุก listener และปิดทั้ง dialog/trip drawer และ order drawer พร้อมกัน ส่วน comment ที่ detail-drawer.tsx:670-674 ซึ่งบอกว่า Escape ปิดเฉพาะ trip drawer นั้นไม่ตรงกับพฤติกรรมจริง

### M24. Signature pad: เส้นดำมองไม่เห็นใน dark mode และพิกัดเพี้ยนเมื่อ canvas ถูกย่อ — CONFIRMED · ✅ แก้แล้ว `6377608d`
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/pod-scan-dialog.tsx:90`
- ปัญหา: strokeStyle ถูก hardcode เป็น "#1a1a1a" แต่พื้นหลัง canvas ใน dark mode คือ dark:bg-white/[0.04] บน surface #131c38 นอกจากนี้ canvas มี width={460} แต่แสดงด้วย CSS w-full ขณะที่ pt() ใช้ clientX - rect.left โดยไม่คูณ canvas.width / rect.width
- ผลกระทบ: ใน dark mode ผู้ใช้วาดแล้วไม่เห็นเส้น บนจอแคบ (dialog กว้าง 520px หรือมือถือ 360px) เส้นจะเลื่อนออกจากตำแหน่งนิ้วหรือเมาส์ (ประมาณ 4% ที่ desktop และมากกว่า 35% บนมือถือ)
- แก้: อ่านสีเส้นจาก getComputedStyle(canvas).color หรือ var(--color-ink-900) และ scale พิกัดด้วย (e.clientX - r.left) * (canvas.width / r.width) รวมถึงแกน y ในทำนองเดียวกัน
- Verifier: บรรทัด 90 hardcode strokeStyle เป็น "#1a1a1a" บนพื้น dark:bg-white/[0.04] ทับ surface สีเข้ม ใน dark mode จึงแทบมองไม่เห็นเส้น ส่วน canvas ประกาศ width={460} แต่ใช้ class w-full และ pt() (บรรทัด 94-97) ใช้ clientX - rect.left โดยไม่ scale ด้วย canvas.width/rect.width ถ้าความกว้างที่แสดงไม่เท่ากับ 460px เส้นจึงเลื่อนจากตำแหน่งที่ชี้ บนจอแคบจะเลื่อนมาก

### M25. Bulk cancel ไม่เตือนเรื่อง cascade ที่จะหยุด robot — CONFIRMED · ✅ แก้แล้ว `07e80468`
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/cancel-dialog.tsx:108`
- ปัญหา: callout เรื่อง cascade แสดงเฉพาะเมื่อ count <= 1 และ dialog bulk ที่ orders-experience.tsx:1192 ไม่ได้ส่ง activeTripCount มา แต่ bulk cancel ใช้กับ order ทุกสถานะที่ถูกเลือกได้ รวมถึง Dispatched/InProgress
- ผลกระทบ: ผู้ใช้ cancel หลาย order ที่กำลังวิ่งอยู่โดยไม่รู้ว่า robot จะหยุดและกลับฐาน ซึ่งขัดกับ rule ที่ว่า destructive action ต้องบอกผลที่จะเกิดขึ้น
- แก้: นับ order ที่อยู่ใน ORDER_IN_FLIGHT_STATES จาก bulkCancelTargets แล้วแสดง callout ว่ามี 'N orders in flight จะถูกสั่งหยุดที่ RIOT3' (หรือ query trips แบบ batch)
- Verifier: cancel-dialog.tsx:108 แสดง callout cascade เฉพาะเมื่อ activeTripCount > 0 && count <= 1 และ CancelOrderDialog ตัว bulk (orders-experience.tsx ราวบรรทัด 1186-1195) ไม่ได้ส่ง activeTripCount มา ส่วน orders-table ก็ไม่มีการจำกัดว่า order สถานะไหนเลือกได้ (grep ไม่พบ disabled หรือ selectable) bulk cancel order ที่กำลังวิ่งจึงไม่มีคำเตือนเรื่อง robot หยุด

### M26. โหมด Scroll (infinite) ทำให้ list ค้างข้อมูลเก่าและปนลำดับ — CONFIRMED · ✅ แก้แล้ว `6377608d`
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:366`
- ปัญหา: ในโหมด infinite ที่ page>1 ทั้ง poll 15 วินาที และ SignalR refetch จะเรียก fetchOrders ด้วย page ล่าสุดแล้ว append เท่านั้น แถวของหน้า 1..N-1 จึงไม่เคยถูก refresh นอกจากนี้การเปลี่ยน sort ไม่ reset page (deps บรรทัด 303 ไม่มี sortBy/sortDir) และการสลับจาก Pages หน้า 3 ไป Scroll จะ append หน้า 3 โดยไม่มีหน้า 1-2
- ผลกระทบ: ใน Scroll mode สถานะ order ด้านบนไม่อัปเดตตาม realtime, order ที่ cancel แล้วยังค้างอยู่ใน list และเปลี่ยน sort แล้วได้ list ที่ลำดับปนกัน
- แก้: เพิ่ม sortBy, sortDir และ paginationMode ใน deps ของ effect reset page และให้ refresh ใน infinite mode ดึงช่วง 1..page ใหม่ทั้งหมด (pageSize*page) แทนการ append เฉพาะหน้าล่าสุด
- Verifier: fetchOrders (บรรทัด 357-363) ในโหมด infinite ที่ page>1 จะ append เฉพาะหน้าปัจจุบัน แต่ poll และ SignalR refetch เรียก fetchOrders ตัวเดียวกัน แถวของหน้าก่อนหน้าจึงไม่เคยถูก refresh และ order ที่ถูกลบหรือเปลี่ยนสถานะยังค้างอยู่ deps ของ effect reset page (บรรทัด 303) ไม่มี sortBy, sortDir และ paginationMode เปลี่ยน sort แล้วจึงได้หน้า N ใหม่ไปต่อท้ายข้อมูลที่เรียงแบบเดิม ส่วนการสลับจาก Pages หน้า 3 ไป Scroll จะได้แค่ข้อมูลหน้า 3

### M27. CSV export: ภาษาไทยเพี้ยนใน Excel และเสี่ยง formula injection — CONFIRMED · ✅ แก้แล้ว `07e80468`, `be275d34`
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:82`
- ปัญหา: Blob สร้างเป็น text/csv;charset=utf-8 แต่ไม่มี BOM (﻿) และฟังก์ชัน escape ไม่ได้กันค่าที่ขึ้นต้นด้วย = + - @ ทั้งที่ notes และ requestedBy มาจากระบบภายนอกหรือ free text
- ผลกระทบ: ถ้าเปิดไฟล์ด้วย Excel บน Windows ไทย notes และชื่อผู้ใช้ภาษาไทยจะกลายเป็น mojibake และ cell ที่ขึ้นต้นด้วย = จะถูก execute เป็นสูตร
- แก้: prepend "﻿" ให้เนื้อหาไฟล์ และใน escape ให้เติม ' นำหน้าค่าที่ match /^[=+\-@\t\r]/
- Verifier: บรรทัด 82 สร้าง Blob จาก lines.join("\n") โดยไม่มี BOM ทำให้ Excel บน Windows อ่านไฟล์ UTF-8 ที่ไม่มี BOM เป็น ANSI และภาษาไทยใน notes/requestedBy จะเพี้ยน ส่วน escape (บรรทัด 59-60) quote เฉพาะค่าที่มี " , หรือ newline ไม่ได้กันค่าที่ขึ้นต้นด้วย = + - @ ตัวที่ผู้ใช้เจอแน่คือภาษาไทยเพี้ยน ส่วน formula injection มาจาก free text ภายในองค์กร ความเสี่ยงจึงต่ำกว่า

### M28. Items: แถวในตารางใช้คีย์บอร์ดไม่ได้ (hand-rolled tr แทน DataRow) — CONFIRMED · ✅ แก้แล้ว `6377608d`
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/items/items-experience.tsx:248`
- ปัญหา: ใช้ <tr onClick> โดยตรง ไม่มี tabIndex, role หรือ Enter handler และไม่มี focus ring ซึ่ง DataRow primitive (data-row.tsx) ถูกสร้างมาแก้ปัญหานี้โดยเฉพาะ อีกทั้งหน้านี้ไม่มี MobileCardRow สำหรับจอเล็ก
- ผลกระทบ: ผู้ใช้คีย์บอร์ดหรือ screen reader เปิด item detail ไม่ได้ และหน้าตาไม่สอดคล้องกับตารางอื่น ๆ
- แก้: เปลี่ยนเป็น <DataRow key={it.id} delayIndex={i} onClick={() => setSelectedId(it.id)}> และเพิ่ม mobile card ด้วย MobileCardRow แบบเดียวกับ orders-table
- Verifier: items-experience.tsx:248-251 ใช้ <tr onClick> โดยไม่มี tabIndex, role หรือ onKeyDown ขณะที่ components/primitives/data-table/data-row.tsx มีทั้ง tabIndex, role=button, handleKeyDown และ focus-visible ring ซึ่ง docstring ระบุว่าสร้างมาแก้ปัญหานี้โดยตรง หน้านี้ไม่มี MobileCardRow ด้วย (grep ไม่พบ)

### M29. Items: status badge และ handling chip อ่านไม่ออกใน dark mode — CONFIRMED · ✅ แก้แล้ว `6377608d`
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/items/items-experience.tsx:37`
- ปัญหา: PENDING, PICKED, DROPPED_OFF, RETURNED (และ handling chip ที่บรรทัด 429) ใช้ text-[var(--color-brand-900)] บนพื้น pastel ใน dark mode ค่า brand-900 คือ #2f3ab8 บน pastel-sky #1b2a60 หรือ pastel-peach #421f18 ทำให้ contrast ประมาณ 1.5:1
- ผลกระทบ: ใน dark mode สถานะ item ส่วนใหญ่แทบมองไม่เห็น
- แก้: ใช้คู่ token -ink ที่ออกแบบมาให้อ่านได้ทั้งสองธีม เช่น text-[var(--color-pastel-sky-ink)] หรือ text-[var(--color-pastel-peach-ink)] เหมือน ItemStatusBadge ใน detail-drawer.tsx:894-932 (หรือ reuse component นั้นเลย)
- Verifier: STATUS_TONE (บรรทัด 36-44) และ handling chip (บรรทัด 429) ใช้ text-[var(--color-brand-900)] ใน dark mode globals.css กำหนด brand-900 เป็น #2f3ab8 บน pastel-sky #1b2a60 หรือ pastel-peach #421f18 ซึ่ง contrast ต่ำมาก ทั้งที่มี token -ink (pastel-sky-ink #c2d0ff, pastel-peach-ink #ffc9b0) ที่ออกแบบมาให้อ่านได้ใน dark mode อยู่แล้ว

### M30. Deep link หรือ refresh ที่มี ?page=N จะถูกรีเซ็ตกลับไปหน้า 1 ทุกครั้ง — CONFIRMED · ✅ แก้แล้ว `703ad77a`
- กลุ่ม: dispatch · code
- ที่: `frontend/components/dispatch/trips-experience.tsx:153`
- ปัญหา: effect `useEffect(() => { setPage(1); }, [statusFilter, search, vehicleKey, fromDate, toDate, pageSize])` ทำงานตอน mount ด้วย จึงเขียนทับค่า page ที่อ่านมาจาก URL ที่บรรทัด 127-130 ทันที ตามด้วย effect ที่บรรทัด 158-171 ซึ่ง replace URL ให้ไม่มี page แล้ว ระหว่างนั้นยังยิง fetch ของ page=N ออกไปก่อนหนึ่งครั้งแล้วค่อย abort
- ผลกระทบ: ตามที่ comment บรรทัด 157 เขียนว่า "refresh/back/forward and deep links work" ใช้ได้กับทุก filter ยกเว้นหน้า ผู้ใช้ที่ refresh ตอนอยู่หน้า 3 หรือส่งลิงก์หน้า 3 ให้เพื่อน จะถูกพากลับไปหน้า 1 และเสีย request เปล่าไปหนึ่งครั้ง
- แก้: ข้ามรอบแรกด้วย ref (`const firstRun = useRef(true)`) หรือย้ายการ setPage(1) ไปไว้ใน handler ที่เปลี่ยน filter โดยตรง (onStatusFilter/onFromDate/onToDate/onPageSizeChange) ส่วน search/vehicle ที่ debounce อยู่ ให้เทียบกับค่าก่อนหน้าผ่าน ref
- Verifier: effect setPage(1) ที่บรรทัด 153-155 ทำงานตอน mount ด้วยเพราะไม่มี guard ใดๆ ค่า page ที่ initializer อ่านจาก ?page=N จึงถูกเขียนทับเป็น 1 ทันที จากนั้น effect ที่ mirror URL ก็ replace URL ตามค่าใหม่ และ fetchTrips รอบแรกยิงออกไปด้วย page=N ก่อนจะถูก abort ตอน re-render ซึ่งขัดกับ comment บรรทัด 157 ที่บอกว่า deep link ใช้ได้

### M31. Poll ที่ล้มเหลวแบบ silent ทำให้ตาราง trips หายไปทั้งตาราง — CONFIRMED · ✅ แก้แล้ว `703ad77a`
- กลุ่ม: dispatch · code
- ที่: `frontend/components/dispatch/trips-experience.tsx:534`
- ปัญหา: fetchTrips เรียก setError ทุกครั้งที่ล้ม รวมถึงตอนที่ poll เรียกแบบ silent (บรรทัด 208) และ TripsTable เช็ก `if (error)` ก่อนแสดงตาราง จึงเอากล่อง error มาแทนตารางทั้งหมด แม้ trips ที่โหลดไว้แล้วจะยังอยู่ใน state ก็ตาม
- ผลกระทบ: ถ้า poll รอบ 15 วินาทีล้มเพียงครั้งเดียว เช่นเจอ 429 จาก per-caller quota หรือเน็ตสะดุด รายการที่ operator กำลังดูจะหายไปจนกว่า poll จะสำเร็จอีกรอบ และเพราะ usePollSchedule หน่วงเวลาเพิ่มขึ้นหลังล้ม ช่วงที่ตารางหายอาจยาวเป็นนาที
- แก้: ถ้ามีข้อมูลอยู่แล้ว (trips.length > 0) ให้แสดง error เป็น banner เล็กเหนือตารางพร้อมปุ่มลองใหม่ แล้วคงตารางเดิมไว้ ส่วนกล่อง error แบบเต็มให้ใช้เฉพาะตอนโหลดครั้งแรกที่ยังไม่มีข้อมูล หรือให้ silent poll ไม่ setError เลยก็ได้
- Verifier: fetchTrips เรียก setError ทั้งตอน silent poll และตอนปกติ (silent มีผลแค่ setRefreshing) ส่วน TripsTable บรรทัด 534 เช็ก if (error) ก่อนจะ render ตาราง จึงเอากล่อง error มาแทนตารางทั้งหมดแม้ trips ยังอยู่ใน state ตารางจะกลับมาก็ต่อเมื่อ poll ครั้งถัดไปสำเร็จ เพราะทุกรอบเรียก setError(null) ก่อน และระหว่างนั้น usePollSchedule ก็ back off เพิ่มเวลาไปเรื่อยๆ

### M32. Trip drawer ไม่อัปเดตสถานะและปุ่ม action เมื่อสถานะ trip เปลี่ยนแบบ realtime — CONFIRMED · ✅ แก้แล้ว `703ad77a`
- กลุ่ม: dispatch · code
- ที่: `frontend/components/dispatch/trip-detail-drawer.tsx:78`
- ปัญหา: useTripSubscription ฟังแค่ TimelineUpdated (ส่งเข้า liveTimelineEntry) กับ MissionUpdated ไม่ได้ re-fetch getTripDetails และไม่ได้ฟัง StatusChanged ที่ trip-hub มีให้ ข้อมูล data.status ที่ใช้กับ TripStatusBadge (บรรทัด 175), isTripInFlight และ TripActionBar (บรรทัด 375-413) จึงเป็นค่าตอนเปิด drawer ไปตลอด อีกทั้ง TripsExperience ยังปิด poll ระหว่างที่ drawer เปิด (enabled: !detailTripId)
- ผลกระทบ: ถ้า trip จบหรือถูก cancel โดยหุ่นหรือ OMS ระหว่างที่ drawer เปิดอยู่ timeline จะแสดงสถานะใหม่แล้ว แต่ header ยังแสดง InProgress และปุ่ม Pause/Cancel ยังกดได้ พอกดก็ได้ error จาก backend ข้อมูลในหน้าเดียวกันจึงขัดกันเอง
- แก้: ใน handler TimelineUpdated (หรือเพิ่ม StatusChanged) ให้เรียก refresh() แบบ debounce หรืออย่างน้อยเอา toStatus ใน entry มาอัปเดต data.status ด้วย setData(d => d && {...d, status})
- Verifier: useTripSubscription ใน drawer ส่ง handler ไปแค่ TimelineUpdated กับ MissionUpdated ทั้งที่ trip-hub.ts ประกาศ StatusChanged ไว้ให้ แต่ไม่มีใครใช้ ส่วน refresh() ถูกเรียกเฉพาะหลังกด action ใน drawer เอง data.status ที่ใช้กับ TripStatusBadge, isTripInFlight และ TripActionBar จึงค้างเป็นค่าตอนเปิด drawer อีกทั้ง trips-experience ก็ปิด poll ไว้ตอน detailTripId มีค่า (enabled: !detailTripId)

### M33. Pass dialog ยืนยันด้วย Enter แบบ global จนกด Enter ที่ปุ่ม Cancel แล้วหุ่นถูก PASS ไปด้วย — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/pass-robot-dialog.tsx:41`
- ปัญหา: keydown listener ผูกไว้ที่ window และเรียก handleConfirm() ทุกครั้งที่กด Enter โดยไม่ดูว่า focus อยู่ที่ไหน ถ้าผู้ใช้กด Tab ไปที่ปุ่ม "Cancel" (บรรทัด 136) แล้วกด Enter จะเกิดทั้ง acknowledgeRobotPass และ onClose พร้อมกัน dialog ไม่ย้าย focus เข้าไปข้างในด้วย การกด Enter ค้างหรือกดเร็วสองครั้งก่อน re-render ก็ยิง request ซ้ำได้ เพราะ busy ยังเป็น false ใน closure
- ผลกระทบ: ผู้ใช้ที่ใช้คีย์บอร์ดตั้งใจกดยกเลิก แต่หุ่นกลับเดินออกไปทันที ซึ่งเป็นการเคลื่อนที่จริงของหุ่นที่ dialog นี้มีไว้กันพลาดโดยเฉพาะ
- แก้: เอา Enter handler ออกจาก window แล้วใช้ autoFocus ที่ปุ่ม PASS (หรือครอบด้วย <form onSubmit>) ให้ Enter ทำงานตามปุ่มที่ focus อยู่ และเพิ่ม guard ด้วย ref (inFlightRef) ใน handleConfirm กันการยิงซ้ำ
- Verifier: pass-robot-dialog.tsx:41 ผูก Enter ไว้ที่ window แล้วเรียก handleConfirm() โดยไม่ดู e.target ถ้า Tab ไปที่ปุ่ม Cancel แล้วกด Enter, listener ที่ window จะยิง acknowledgeRobotPass ไปก่อน แล้ว browser จึง click ปุ่ม Cancel ตามมา คำขอ PASS ส่งออกไปแล้วไม่ว่าปุ่ม Cancel จะทำงานหรือไม่ ซึ่งหมายถึงหุ่นเคลื่อนที่จริง handleConfirm ไม่มี guard ด้วย ref, busy ใน closure จะเป็น true ก็ต่อเมื่อ re-render แล้ว และ dialog ไม่มี autoFocus ส่วนการยิงซ้ำจาก key repeat ทำได้แค่ในช่องเวลาสั้นมาก

### M34. กด Esc ใน dialog หรือ lightbox แล้ว Trip drawer ข้างหลังปิดตามไปด้วย — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/trip-detail-drawer.tsx:124`
- ปัญหา: drawer ผูก Escape → onClose ไว้ที่ window และ PassRobotDialog (pass-robot-dialog.tsx:40) กับ Lightbox (manual/trip-pod-section.tsx:127) ก็ผูกที่ window เช่นกันโดยไม่มี stopPropagation หรือเช็กว่าตัวเองเป็น overlay บนสุด ส่วน RaiseExceptionDialog ไม่มี Esc handler ของตัวเองเลย เลยไปเข้า handler ของ drawer แทน
- ผลกระทบ: Esc ปิดทุกชั้นพร้อมกัน ถ้าเปิด Flag dialog แล้วพิมพ์ detail ไว้ การกด Esc จะปิด drawer จน TripActionBar ถูก unmount และข้อความที่พิมพ์หายหมด ผู้ใช้ต้องเปิด trip ใหม่
- แก้: ให้ drawer ข้าม Esc ตอนที่มี overlay ลูกเปิดอยู่ (ส่ง flag ลงไป หรือให้ลูกเรียก e.stopImmediatePropagation() ใน listener แบบ capture) และเพิ่ม Esc handler ให้ RaiseExceptionDialog ที่ไม่ปิดตอน busy ถ้าจะให้ดีควรทำเป็น hook กลางสำหรับ overlay stack
- Verifier: drawer (trip-detail-drawer.tsx:124), PassRobotDialog และ Lightbox (trip-pod-section.tsx:127) ต่างผูก Escape ไว้ที่ window และไม่มีตัวไหน stopPropagation กด Esc ครั้งเดียวจึงปิดทุกชั้นพร้อมกัน RaiseExceptionDialog ไม่มี keydown handler เลย (grep ไม่เจอ) Esc จึงไปปิด drawer แทน แล้ว TripActionBar ที่มี dialog อยู่ข้างในก็ถูก unmount ทำให้ข้อความที่พิมพ์ไว้หาย

### M35. Drawer และ dialog ไม่มี role=dialog, focus trap หรือการคืน focus — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/trip-detail-drawer.tsx:148`
- ปัญหา: motion.aside ของ drawer, panel ของ PassRobotDialog (pass-robot-dialog.tsx:80), RaiseExceptionDialog (raise-exception-dialog.tsx:92), ReassignDialog (manual/operator-board-section.tsx:168) และ DenyDialog (manual/override-queue-section.tsx:177) ไม่มี role="dialog", aria-modal หรือ aria-labelledby ไม่ย้าย focus เข้าเมื่อเปิด ไม่ trap focus และไม่คืน focus เมื่อปิด
- ผลกระทบ: ผู้ใช้คีย์บอร์ดหรือ screen reader กด Tab แล้ว focus ไปโดนแถวตาร็างที่อยู่หลัง backdrop (DataRow มี tabIndex=0) และกด Enter เปิด trip อื่นได้ขณะ drawer ยังเปิด screen reader ก็ไม่ประกาศว่ามี dialog เปิดขึ้นมา
- แก้: เพิ่ม role="dialog" aria-modal="true" aria-labelledby={id ของ h2} ให้ autoFocus ไปที่ปุ่มปิดหรือช่องแรก ใช้ focus trap และคืน focus ให้ปุ่มที่เปิดตอนปิด ควรทำเป็น primitive กลางคู่กับ OverlayBackdrop เพื่อใช้ร่วมกันทุก dialog
- Verifier: grep หา role=, aria-modal และ autoFocus ใน drawer, pass-robot-dialog, raise-exception-dialog และ manual/*.tsx ไม่เจอเลย ไม่มี focus trap และไม่คืน focus ตอนปิด ส่วน DataRow ใช้ tabIndex={interactive ? 0 : undefined} (data-row.tsx:66) แถวตารางที่อยู่หลัง backdrop จึงยังรับ focus ได้ ซึ่งขัดกฎ review-frontend.md:81

### M36. Badge Rejected/Hang ใช้ token --color-wine/--color-flame ที่ไม่ได้ประกาศไว้ ทำให้อ่านไม่ออกใน dark mode — CONFIRMED · ✅ แก้แล้ว `703ad77a`
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/badges.tsx:39`
- ปัญหา: TONE_BG.wine และ flame (บรรทัด 39-40, 50-51) ใช้ var(--color-wine,#991b1b) และ var(--color-flame,#c2410c) แต่ app/globals.css ไม่มี token ทั้งสองตัวนี้ (grep ไม่เจอ) จึงใช้ hex fallback ตลอด ซึ่งไม่มีค่าสำหรับ .dark ที่ retry-history-panel.tsx:132 ก็เป็นแบบเดียวกัน
- ผลกระทบ: ใน dark mode ตัวอักษร #991b1b บนพื้นแดงโปร่ง 10% ที่วางบน surface มืดมี contrast ราว 2:1 ทำให้สถานะ Rejected (ความล้มเหลวที่ต้องรีบเห็น) แทบอ่านไม่ออก ทั้งในตาราง trips และใน drawer
- แก้: ประกาศ --color-wine, --color-wine-soft, --color-flame, --color-flame-soft ใน globals.css ทั้งใน :root และ .dark (dark ใช้โทนสว่าง เช่น #f87171 หรือ #fb923c) แล้วเอา hex fallback ออก (reports/*.tsx ก็ใช้ --color-wine เหมือนกัน)
- Verifier: globals.css และไฟล์ css/ts อื่นไม่ได้ประกาศ --color-wine หรือ --color-flame เลย (grep หาการประกาศแล้วไม่เจอ) จึงใช้ hex fallback ทั้งใน light และ dark คำนวณ contrast ของ #991b1b (L≈0.076) บนพื้นมืดได้ราว 2:1 ต่ำกว่าเกณฑ์ 4.5:1 ใน house rule และยังขัดกฎ raw hex (review-frontend.md:71) ด้วย

### M37. Override ที่ approve/deny ถูกบันทึกเป็นชื่อ Admin คนแรกในรายชื่อ ไม่ใช่คนที่กดจริง — CONFIRMED
- กลุ่ม: dispatch · code
- ที่: `frontend/components/dispatch/manual/override-queue-section.tsx:31`
- ปัญหา: pickDispatcherId เลือก operator คนแรกที่ role เป็น Admin หรือ Supervisor แล้วส่งเป็น decidedBy ไปกับ approveOverride/denyOverride (บรรทัด 53, 163) ไม่ว่าใครจะเป็นคนกด (comment บรรทัด 16-19 ระบุว่าเป็น placeholder ของ MVP)
- ผลกระทบ: audit ของการอนุมัติให้ operator ออกนอก geofence ระบุคนผิด ทุก decision จะไปลงชื่อ supervisor คนเดียวกัน ถ้ายังไม่มี Admin/Supervisor operator ปุ่ม Approve ก็จะ error ทั้งที่ผู้ใช้มี admin JWT อยู่
- แก้: เอาตัวตนของผู้กดจาก session ฝั่ง backend (ให้ endpoint stamp decidedBy จาก JWT เอง) แล้วตัดพารามิเตอร์ decidedBy ฝั่ง client ออก ถ้ายังทำไม่ได้ อย่างน้อยให้ UI แสดงว่าบันทึกในนามใคร
- Verifier: pickDispatcherId เลือก Admin/Supervisor คนแรกในรายชื่อ ฝั่ง backend AdminManualOperatorEndpoints.cs:94/111 ก็รับ body.DecidedByOperatorId ไปใช้ตรงๆ โดยไม่ stamp จาก JWT ทุก decision ใน audit จึงลงชื่อคนเดียวกันที่ผิด แม้ comment จะบอกว่าเป็น placeholder ของ MVP ที่ตั้งใจไว้ ผลก็ยังทำให้ audit ผิดจริง ส่วนกรณีที่ยังไม่มี Admin operator แล้วกด approve ไม่ได้ก็มีอยู่จริง (บรรทัด 45-48)

### M38. หน้า /dispatch/manual ไม่มี shell นำทาง ใช้สี Tailwind ดิบ และใช้ backdrop ที่เขียนเอง — CONFIRMED · ✅ แก้แล้ว `c40cb169`
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/manual/manual-operator-board.tsx:75`
- ปัญหา: app/dispatch/manual/page.tsx render แค่ ManualOperatorBoard โดยไม่มี TopNav/LeftRail แบบที่ app/dispatch/trips/page.tsx มี และไม่มีลิงก์จากที่ไหนในแอปเลย (grep 'dispatch/manual' เจอแค่ตัวเอง) ทั้ง 3 ไฟล์ใน manual/ ใช้ zinc-*, red-*, emerald-*, blue-* แทน token ใน globals.css ส่วน ReassignDialog (operator-board-section.tsx:164) และ DenyDialog (override-queue-section.tsx:173) ทำ backdrop `fixed inset-0 bg-black/60` เอง ไม่ใช้ OverlayBackdrop, ไม่มี Esc และคลิกนอก dialog แล้วปิดได้แม้ตอน busy เวลาในคิวก็ใช้ toLocaleTimeString (override-queue-section.tsx:97) แทน DateTime primitive
- ผลกระทบ: หน้านี้ดูเหมือนคนละแอป เข้าได้ทางพิมพ์ URL เท่านั้น เข้ามาแล้วก็ไม่มีเมนูให้ออก สีสถานะไม่ตรงกับความหมาย amber/coral/success ที่ใช้ในหน้าอื่น และรูปแบบเวลาไม่เหมือนหน้าอื่น (backdrop ไม่ได้อยู่ใน AnimatePresence จึงไม่เกิดปัญหา stranded click แต่ยังผิด house rule)
- แก้: ครอบด้วย TopNav/LeftRail และ main layout แบบเดียวกับ trips page แล้วเพิ่มลิงก์ใน left-rail เปลี่ยนสีเป็น token (--color-success/amber/coral, surface) เปลี่ยน dialog ไปใช้ OverlayBackdrop และ AnimatePresence ตามแบบ RaiseExceptionDialog และแสดงเวลาด้วย <DateTime>
- Verifier: app/dispatch/manual/page.tsx render แค่ ManualOperatorBoard ไม่มี TopNav/LeftRail แบบหน้า trips และ grep 'dispatch/manual' นอก .next ก็เจอแค่ไฟล์ page เอง ใน manual/*.tsx ใช้ zinc/red/emerald/blue ดิบรวมราว 42 จุด ReassignDialog กับ DenyDialog ใช้ div fixed inset-0 bg-black/60 ที่ onClick={onClose} ปิดได้โดยไม่เช็ก busy ส่วน toLocaleTimeString อยู่ที่ override-queue-section.tsx:97-98 ตรงตามที่รายงานทุกข้อ

### M39. Scan dialog: ผล lookup ของรหัสที่สแกนก่อนอาจทับผลของรหัสที่สแกนทีหลัง — CONFIRMED
- กลุ่ม: fleet · code
- ที่: `frontend/components/fleet/carrier-scan-dialog.tsx:103`
- ปัญหา: resolve() ไม่มี request token และไม่มี AbortController ตัวอย่างเช่น สแกน A ที่ไม่อยู่ใน knownCarriers (บนหน้า carriers คือแค่ rows ของหน้าปัจจุบัน 25 แถว จึงพลาดบ่อย) ระบบจะเรียก `await getCarrier(A)` แล้วถ้าสแกน B ที่อยู่ใน knownCarriers ต่อทันที B จะ settle ทันที ต่อมาเมื่อ response ของ A กลับมา settle(A) จะ setState ทับเป็น found=A ปัญหาเดียวกันเกิดได้กับ lookup ผ่าน network สองครั้งที่ตอบกลับสลับลำดับ
- ผลกระทบ: กล่องสีเขียวจะแสดง carrier ที่ไม่ใช่ตัวที่สแกนล่าสุด ถ้าผู้ใช้กด "Use this carrier" จะได้ carrier ผิดตัว ซึ่งขัดกับเจตนาของ dialog ที่ต้องการให้ตรวจสติกเกอร์ผิดได้ก่อน apply
- แก้: เก็บ `latestScan = useRef(0)` และเพิ่มค่าทุกครั้งที่ resolve ถูกเรียก หลัง await ให้เช็กว่ายังเป็นรอบล่าสุดก่อน setState หรือใช้ AbortController ยกเลิก getCarrier ตัวก่อน (getCarrier รับ signal อยู่แล้ว) และ abort ตอนปิด dialog ด้วย
- Verifier: resolve() ใน carrier-scan-dialog.tsx:70-140 ไม่มี token หรือ AbortController จริง ถ้า A ไม่อยู่ใน knownCarriers จะ await getCarrier(A) ระหว่างนั้นถ้าสแกน B ที่อยู่ใน knownCarriers จะ settle(B) ทันที แล้วเมื่อ A ตอบกลับ settle(A) จะ setState ทับ ตัวกัน repeat (lastScan) กันได้แค่รหัสเดิมซ้ำ การสแกนต่อกันเร็วๆ ด้วย wedge เกิดขึ้นได้จริง แต่ผู้ใช้ยังต้องกดยืนยันเอง และกล่องแสดงรหัส carrier ที่ found ให้เห็น จึงคงไว้ที่ MEDIUM

### M40. Dialog ในกลุ่ม fleet/attachments ไม่มี role=dialog, ไม่ปิดด้วย Esc และไม่ย้าย focus — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: fleet · uiux
- ที่: `frontend/components/fleet/carrier-action-dialog.tsx:183`
- ปัญหา: panel ของ CarrierActionDialog (183), CarrierFormDialog (carrier-form-dialog.tsx:106), ModalShell (carrier-type-form-dialog.tsx:52), CarrierTypeDeleteDialog (carrier-type-delete-dialog.tsx:64), PhotosDialog (photos-dialog.tsx:71) และ CarrierMaintenancePanel (carrier-maintenance-panel.tsx:58) เป็น motion.div/aside ธรรมดา ไม่มี role="dialog"/aria-modal และไม่มีตัวจับ Escape ทั้งที่ CarrierScanDialog กับ AttachmentLightbox ในโฟลเดอร์เดียวกันมี นอกจากนี้ไม่มี autoFocus ที่ช่องแรกหรือปุ่ม confirm และไม่คืน focus ให้ปุ่มที่เปิด dialog ตอนปิด
- ผลกระทบ: ผู้ใช้คีย์บอร์ดกด Esc ปิดไม่ได้ (ต่างจาก scan dialog ในหน้าเดียวกัน) focus ค้างอยู่ที่ปุ่มหลัง backdrop และ Tab หลุดไปที่ตารางด้านหลังได้ screen reader ไม่รู้ว่ามี modal เปิดอยู่
- แก้: แยก ModalShell ที่ carrier-type-form-dialog ใช้อยู่แล้วออกมาเป็น primitive กลาง แล้วเพิ่ม role="dialog" aria-modal aria-labelledby, Esc (ข้ามตอน busy), autoFocus ที่ input แรกหรือปุ่มหลัก และคืน focus ตอนปิด จากนั้นให้ทั้ง 6 จุดใช้ primitive นี้
- Verifier: grep หา role=, Escape และ autoFocus ใน components/fleet และ components/attachments พบเฉพาะใน carrier-scan-dialog.tsx (64, 187, 219) และ attachment-lightbox.tsx:75 ส่วน CarrierTypeDeleteDialog, PhotosDialog และ maintenance panel ที่เปิดดูเป็น motion.div/aside ธรรมดา ไม่มี role, ไม่ปิดด้วย Esc และไม่จัดการ focus ซึ่งขัดกับ house rule ใน review-frontend.md:81 ('focus moves in on open, returns on close, Esc closes, focus trapped')

### M41. capture="environment" ทำให้มือถือเลือกรูปที่ถ่ายไว้แล้วไม่ได้ — CONFIRMED
- กลุ่ม: fleet · uiux
- ที่: `frontend/components/attachments/attachment-gallery.tsx:64`
- ปัญหา: input file ใส่ `capture="environment"` คู่กับ `multiple` บน Android Chrome และ iOS Safari ค่า capture จะเปิดกล้องตรงทันทีโดยไม่มีตัวเลือกคลังรูป และ multiple จะถูกเมินไป คอมเมนต์บอกว่า control เดียวรองรับทั้งสองทาง แต่บนมือถือรองรับได้แค่ทางเดียว
- ผลกระทบ: ช่างหรือ operator ที่ถ่ายรูปความเสียหายไว้ก่อนแล้ว อัปโหลดจากมือถือไม่ได้ และอัปโหลดหลายรูปพร้อมกันบนมือถือไม่ได้ ต้องถ่ายใหม่ทีละรูป
- แก้: ถอด capture ออกเพื่อให้ระบบปฏิบัติการแสดงตัวเลือก "กล้อง/คลังรูป" เอง หรือทำเป็นสองปุ่ม "ถ่ายรูป" (มี capture) กับ "เลือกไฟล์" (multiple ไม่มี capture)
- Verifier: attachment-gallery.tsx:64-65 ใส่ทั้ง capture="environment" และ multiple จริง บน Android Chrome และ iOS Safari ค่า capture จะเปิดกล้องตรงทันทีโดยไม่มีตัวเลือกคลังรูปหรือไฟล์ และ multiple จะไม่มีผล คอมเมนต์ที่บอกว่า 'one control serving both entry points' ถูกเฉพาะเมื่อเทียบเดสก์ท็อปกับมือถือ ไม่ได้รองรับรูปเก่าในเครื่องมือถือ ต่างจาก POD (operator-pwa/pod-capture.tsx) ที่ตั้งใจให้ถ่ายสดจริง ส่วน gallery สำหรับรูปความเสียหายควรเลือกรูปเดิมได้

### M42. บันทึก offline สำเร็จแล้ว แต่หน้าจอกลับแสดง error สีแดงแทน 'Saved offline' — CONFIRMED · ✅ แก้แล้ว `d364498e`
- กลุ่ม: operator-pwa · code
- ที่: `frontend/components/operator-pwa/trip-detail.tsx:101`
- ปัญหา: ใน onAcknowledge/onPickup/onDrop/onComplete จะ setActionNote("Saved offline — will sync.") ก่อน แล้วจึง await refresh() แต่ refresh rethrow (บรรทัด 66) ตอน offline fetch จะ throw 'Failed to fetch' ไปเข้า catch แล้ว setActionError ซึ่งบรรทัด 190 (actionNote && !actionError) จะซ่อน note ไว้
- ผลกระทบ: operator ที่ไม่มีสัญญาณเห็นข้อความ error แม้ action ถูกเก็บลงคิวแล้ว อาจกดซ้ำ ถ่ายรูปใหม่ หรือคิดว่างานยังไม่ถูกบันทึก ซึ่งขัดกับหลัก 'pending state visible'
- แก้: ห่อ refresh() หลัง action ด้วย try/catch แยก (หรือ void refresh().catch(()=>{})) และเรียก refresh เฉพาะเมื่อ result.delivered เท่านั้น ให้ error ของ action กับ error ของการ refresh แยกจากกัน
- Verifier: ตอน offline enqueueAction จะคืน delivered:false แล้วโค้ด setActionNote("Saved offline…") ต่อด้วย await refresh() แต่ getAssignedTrips เรียก fetch ซึ่ง throw และ refresh ก็ rethrow ต่อ จึงไปเข้า catch แล้ว setActionError บรรทัด 190 (actionNote && !actionError) เลยซ่อน note ไว้ ผู้ใช้จึงเห็นแบนเนอร์แดงแทนข้อความ 'Saved offline'

### M43. ผลการขอ geofence override ไม่เคยแสดงให้เห็น และช่อง Warehouse ID ให้ไปคัดลอกค่าที่หน้าจอไม่ได้แสดง — CONFIRMED · 🟡 ซ่อนฟอร์มไว้ `d364498e` (geofence ปิดอยู่ — ต้องให้ API หา location จาก trip ก่อนเปิด)
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/trip-detail.tsx:187`
- ปัญหา: ErrorBanner ส่งผลผ่าน onOverrideSubmitted → setActionNote แต่ actionError ยังค้างอยู่ บรรทัด 190 จึงซ่อน note ไว้ทั้งกรณีสำเร็จและล้มเหลว ฟอร์มยังค้างให้กรอกต่อ นอกจากนี้ label บรรทัด 383 เขียนว่า 'Warehouse ID (copy from the trip detail)' แต่ trip detail ไม่ได้แสดง warehouse ID ที่ไหนเลย
- ผลกระทบ: operator ที่ติด geofence ไม่รู้ว่าคำขอ override ส่งถึงหัวหน้าหรือยัง และกรอก Warehouse ID ไม่ได้จริง เส้นทางกู้คืนนี้จึงใช้งานไม่ได้
- แก้: ให้ onOverrideSubmitted ล้าง actionError (setActionError(null)) ก่อน set note หรือแสดงผลภายใน ErrorBanner เอง และเติม expectedWarehouseId จากข้อมูล trip หรือจาก body ของ GEOFENCE_REJECTED แทนการให้พิมพ์เอง
- Verifier: onOverrideSubmitted แค่ setActionNote แต่ไม่ล้าง actionError บรรทัด 190 จึงซ่อน note ไว้ทั้งกรณีสำเร็จและล้มเหลว ส่วน AssignedTrip ก็ไม่มี warehouse field และ TripDetail ไม่ได้แสดง warehouse ID ที่ไหนเลย label ที่บอกให้ 'copy from the trip detail' จึงทำตามไม่ได้

### M44. โหลด trip ไม่สำเร็จ (offline/429/5xx) แต่แสดงว่า 'Trip not found or no longer assigned to you' — CONFIRMED · ✅ แก้แล้ว `d364498e`
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/trip-detail.tsx:78`
- ปัญหา: refresh ตั้ง loading=false ใน finally แต่ไม่มี state สำหรับ error ถ้าการโหลดครั้งแรกล้มเหลว trip จะยังเป็น null และเข้า branch 'not found'
- ผลกระทบ: operator ที่เปิดจาก notification ตอนสัญญาณอ่อนจะเข้าใจว่างานถูกยกเลิกหรือถูกโอนไปแล้ว และไม่มีปุ่ม retry
- แก้: เพิ่ม error state แบบเดียวกับ TripsList (LoadState kind:'error') แสดงข้อความจาก server และปุ่ม 'ลองใหม่' (usePollSchedule().trigger) แยกจากกรณีที่ได้ 200 แต่ไม่พบ trip
- Verifier: refresh ไม่มี error state finally จะ setLoading(false) ถ้าการโหลดครั้งแรก throw ค่า trip จะยังเป็น null แล้วไปแสดง 'Trip not found or no longer assigned to you' และไม่มีปุ่ม retry

### M45. Pool โหลดไม่สำเร็จ แต่แสดงว่า 'Pool is empty' — CONFIRMED · ✅ แก้แล้ว `f0a7c44a` (ซ้ำกับ M16)
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/pool-trips-list.tsx:77`
- ปัญหา: usePoolTrips.refetch (lib/hooks/use-pool-trips.ts:66-68) จับ error แล้ว dispatch trips: [] โดยไม่ตั้งค่า error ส่วน PoolTripsList ก็ไม่ได้อ่าน error เลย ถ้า offline, 401 หรือ 429 จึงขึ้นเป็น empty state
- ผลกระทบ: operator เห็น pool ว่างทั้งที่มีงานรอ งานค้างไม่ถูกรับ และไม่มีทางรู้ว่าต้องลองใหม่
- แก้: ใน hook ให้ dispatch error (และคง trips เดิมไว้) แล้วให้ PoolTripsList แสดงแบนเนอร์ error พร้อมปุ่ม Retry แยกจาก empty state
- Verifier: ใน use-pool-trips.ts refetch ตอน catch จะ dispatch REFETCH trips:[] โดยไม่ set error (reducer ยังตั้ง error:null ด้วย) PoolTripsList ไม่อ่าน error เลยแสดง 'Pool is empty' แย่กว่านั้นคือ refetch ที่ล้มเหลวตอน reconnect จะล้างรายการที่มีอยู่ทิ้งด้วย

### M46. กด claim ตอน offline ระบบเข้าคิวไว้แล้วบอกว่า 'Queued' แต่ถ้าแพ้ CAS ทีหลัง action จะถูกทิ้งเงียบๆ — CONFIRMED · ✅ แก้แล้ว `d364498e`
- กลุ่ม: operator-pwa · code
- ที่: `frontend/components/operator-pwa/pool-trips-list.tsx:50`
- ปัญหา: acknowledgeTrip ส่งผ่าน enqueueAction ตอน offline หรือเจอ network error จึงถูกเก็บลงคิว การ claim จาก pool เป็นการแข่งกัน (CAS) ตอน replay ถ้าคนอื่นรับไปแล้วจะได้ 409 ซึ่ง isPermanentFailure ลบทิ้งโดยไม่มีใครเห็น
- ผลกระทบ: operator เชื่อว่าได้งานแล้วจึงเดินไปที่จุด pickup แต่จริงๆ งานเป็นของคนอื่น ทำให้เสียเที่ยวหรือเกิดงานซ้อน
- แก้: การ claim ควรทำแบบ online เท่านั้น: เรียก fetch ตรงโดยไม่ผ่านคิว ถ้า navigator.onLine===false หรือเจอ network error ให้แสดงว่า 'ต้องออนไลน์เพื่อรับงาน' แทนการเข้าคิว
- Verifier: acknowledgeTrip ส่งผ่าน enqueueAction ตอน offline จึงถูกเข้าคิวและขึ้น toast 'Queued' พอ replay แล้วได้ 409 TRIP_ALREADY_CLAIMED ทั้ง drainQueue และ sw.js จะลบแถวทิ้ง แล้ว header ก็ไม่ได้แสดง lastError การ claim ที่แพ้จึงหายไปเงียบๆ

### M47. Logout ไม่ล้างคิว offline และ push subscription บนเครื่องที่ใช้ร่วมกัน — CONFIRMED · ✅ แก้แล้ว `d364498e`
- กลุ่ม: operator-pwa · code
- ที่: `frontend/components/operator-pwa/settings-panel.tsx:120`
- ปัญหา: onLogout เรียกแค่ /api/auth/logout แถวในคิว IDB 'dtms-operator' ไม่มีข้อมูลว่าเป็นของ user คนไหน และ push subscription ยังผูกกับบัญชีเดิม ถ้า operator คนถัดไป login บนแท็บเล็ตเครื่องเดียวกัน drain จะ replay action ของคนก่อนด้วย cookie ของคนใหม่ (ได้ 4xx แล้วถูกทิ้ง) และ notification ของคนก่อนจะเด้งบนเครื่องคนใหม่
- ผลกระทบ: งานของ operator คนก่อนหาย หรือถูกส่งในชื่อคนอื่น และข้อมูล trip ของคนหนึ่งไปโผล่บนเครื่องที่อีกคนถืออยู่
- แก้: ก่อน logout ให้เตือนถ้า getQueueDepth()>0 (เสนอ Sync now ก่อน) แล้ว clearQueue() + unsubscribeFromPush() หรือเก็บ userId ไว้ในแต่ละแถวของคิว แล้วข้ามแถวที่ไม่ตรงกับ session ปัจจุบัน
- Verifier: onLogout เรียกแค่ fetch /api/auth/logout ไม่มี clearQueue หรือ unsubscribeFromPush ส่วน route logout ก็ไม่ได้จัดการ push แถวในคิวไม่มี userId จึงถูก replay ด้วย cookie ของคนถัดไปได้จริงบนเครื่องที่ใช้ร่วมกัน

### M48. My trips และ Trip detail แสดงแค่ tripId 8 ตัว ไม่มี OrderRef หรือจุด pickup/drop — CONFIRMED · ✅ แก้แล้ว `d193c8de`
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/trip-detail.tsx:175`
- ปัญหา: trip-detail.tsx:175 และ trips-list.tsx:84 แสดงแค่ trip.tripId.slice(0, 8) ในขณะที่หน้า Pool (pool-trips-list.tsx:150,159,163) แสดง orderRef, pickupCode, dropCode เพราะ AssignedTrip ไม่มี field เหล่านี้
- ผลกระทบ: หลัง claim แล้ว operator ไม่เห็นว่าต้องไปรับของที่ไหนหรือเป็น order ไหน ต้องจำเอาจาก pool ซึ่งขัดกับกฎที่ว่า OrderRef คือ reference ที่ผู้ใช้ต้องเห็น
- แก้: เพิ่ม orderRef, pickupCode, dropCode ใน AssignedTripDto (Trip มี frozen location codes อยู่แล้ว) แล้วแสดงแบบเดียวกับ PoolCard/ConfirmClaimDrawer ส่วน tripId ให้เป็นข้อมูลรอง
- Verifier: trip-detail.tsx:175 และ trips-list.tsx:84 แสดงแค่ tripId.slice(0,8) ส่วน AssignedTripDto ไม่มี orderRef/pickupCode/dropCode (คอมเมนต์ใน backend ระบุว่า Phase 4.4 จะ join ภายหลัง) operator จึงมองไม่เห็นจุดรับ/ส่งหลัง claim

### M49. ไอคอนใน manifest และ notification ชี้ไปยังไฟล์ที่ไม่มีอยู่ — CONFIRMED · ✅ แก้แล้ว `6782f815`
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/public/manifest.webmanifest:15`
- ปัญหา: manifest อ้าง /icons/operator-192.png และ operator-512.png ส่วน sw.js:45-46 อ้าง operator-192.png และ operator-badge.png แต่ใน frontend/public มีแค่ manifest.webmanifest กับ sw.js ไม่มีโฟลเดอร์ icons
- ผลกระทบ: Chrome/Android ไม่ถือว่าเป็น PWA ที่ติดตั้งได้ (เกณฑ์ install ต้องโหลดไอคอน 192/512 ได้) จึงไม่มี install prompt หรือได้ไอคอนว่างบนหน้า home และ notification ได้ไอคอน default
- แก้: เพิ่มไฟล์ public/icons/operator-192.png, operator-512.png (maskable แยกจาก any) และ operator-badge.png (monochrome)
- Verifier: ใน frontend/public มีแค่ manifest.webmanifest กับ sw.js ไม่มีโฟลเดอร์ icons ไฟล์ /icons/operator-192.png, operator-512.png และ operator-badge.png ที่อ้างถึงจึงเป็น 404 ทั้งหมด install prompt จึงไม่ขึ้นและ notification ไม่มีไอคอน

### M50. ปุ่ม Settings, pill 'queued' และลิงก์ Back เล็กกว่า 44px — CONFIRMED · ✅ แก้แล้ว `d364498e`
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/operator-shell-header.tsx:70`
- ปัญหา: ลิงก์ Settings ใช้ px-3 py-1.5 text-xs สูงประมาณ 28px pill 'N queued' (บรรทัด 63) ใช้ py-0.5 สูงประมาณ 20px และลิงก์ '← Back to trips' ใน app/m/trips/[id]/page.tsx:20 เป็น inline text-xs สูงประมาณ 16px
- ผลกระทบ: ใช้มือเดียวหรือใส่ถุงมือแล้วกดพลาดบ่อย และลิงก์ Back เป็นทางกลับทางเดียวในโหมด standalone ที่ไม่มีปุ่ม back ของเบราว์เซอร์
- แก้: ให้ทุก target มีขนาดอย่างน้อย min-h-11 min-w-11 (เช่น inline-flex items-center px-3 min-h-11) และทำลิงก์ Back ให้เป็นปุ่มเต็มแถวหรือย้ายไปไว้ใน header
- Verifier: house rule ของ Operator PWA กำหนดให้ touch target ≥ 44px ลิงก์ Settings (px-3 py-1.5 text-xs), pill queued (py-0.5) และลิงก์ Back แบบ inline text-xs ใน app/m/trips/[id]/page.tsx เล็กกว่าที่กำหนดชัดเจน หน้า trip detail ไม่มี tabs nav ลิงก์ Back จึงเป็นทางกลับหลักในโหมด standalone

### M51. ฟอร์ม Create account เป็นของปลอม แต่บอกผู้ใช้ว่าสร้างบัญชีสำเร็จแล้ว — CONFIRMED
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/login/auth-panel.tsx:99`
- ปัญหา: handleSignUpSubmit (บรรทัด 99-122) ไม่เรียก backend เลย มีแค่ setTimeout ที่สลับ status เป็น success แล้วขึ้น banner ว่า "Account created. Sign in with the credentials you just registered" (บรรทัด 332) ทั้งที่ระบบเป็น LDAP-only และไม่มี endpoint สมัครสมาชิก ปุ่ม CTA บนหน้า landing ('Get started', 'Start free trial' ที่ hero.tsx:74, top-nav.tsx:578, cta-banner.tsx:166) ลิงก์ไป /login?mode=signup ซึ่งเปิดฟอร์มนี้เป็นค่าเริ่มต้น (บรรทัด 38) และ SIGNUP_URL ก็ hardcode เป็น IP ภายในไว้ (บรรทัด 28)
- ผลกระทบ: ผู้ใช้กรอกรหัสพนักงานกับรหัสผ่าน แล้วเห็นว่าสร้างบัญชีสำเร็จ จากนั้นพอ sign in ก็เจอ "Wrong username or password" โดยไม่รู้สาเหตุ ทำให้เกิดคำขอ support และฟอร์มยังเก็บรหัสผ่านไปโดยไม่ได้ใช้ทำอะไร
- แก้: ลบ SignUpForm กับ mode "up" ออก ให้ 'Create account' เป็น <a href={SIGNUP_URL} target="_blank" rel="noopener noreferrer"> ธรรมดาที่บอกชัดว่าจะไปที่ portal ภายนอก ย้าย SIGNUP_URL ไปไว้ใน env และเปลี่ยน CTA บน landing ให้ชี้ไปที่ /login
- Verifier: handleSignUpSubmit (auth-panel.tsx:99-122) ไม่เรียก backend จริง มีแค่ setTimeout แล้วขึ้น banner 'Account created' และคอมเมนต์ก็เขียนว่า 'theatrical only' ส่วน CTA บน landing ลิงก์ไป /login?mode=signup จริง (hero.tsx:74, cta-banner.tsx:52, top-nav.tsx:115 ไม่ใช่บรรทัด 578 ตามที่รายงาน) ขอลดเป็น MEDIUM เพราะการกดแท็บ 'Create account' หรือลิงก์ 'Create an account' จะเปิด SIGNUP_URL (portal ภายนอก) ให้ด้วย จึงยังมีทางไปสมัครจริงได้ แต่ถ้าเข้าผ่าน ?mode=signup จะไม่เปิด portal และฟอร์มก็ยังโกหกว่าสร้างบัญชีสำเร็จแล้ว

### M52. โหลด permissions พลาดครั้งเดียว ผู้ใช้จะเห็น 'Insufficient permissions' ถาวร — CONFIRMED
- กลุ่ม: shell-pages · code
- ที่: `frontend/components/auth/auth-provider.tsx:108`
- ปัญหา: .catch(() => setPermissions([])) ทำให้ทุก error กลายเป็น "ไม่มีสิทธิ์" ถาวร ไม่ว่าจะเป็น network ล่ม, API เพิ่ง restart, session หมดอายุ (401) หรือ AbortError ตอน cleanup โค้ดไม่ retry และ effect จะรันใหม่เฉพาะเมื่อ status หรือ employeeCode เปลี่ยน นอกจากนี้ AbortError จาก cleanup ยัง setPermissions([]) ทับค่า null ที่ effect ใหม่เพิ่งตั้งไว้ด้วย
- ผลกระทบ: ทุกหน้าที่ครอบด้วย PermissionGuard (admin/systems, fleet carriers, items ฯลฯ) จะขึ้น "Insufficient permissions … Contact your administrator" จนกว่าจะ reload เต็มหน้า ผู้ใช้จึงไปขอสิทธิ์ทั้งที่มีสิทธิ์อยู่แล้ว และใน dev ที่เปิด StrictMode จะเห็นข้อความนี้แวบขึ้นมา
- แก้: แยก state permissionsError ออกมา ให้ PermissionGuard แสดง 'โหลดสิทธิ์ไม่สำเร็จ' พร้อมปุ่ม retry เมื่อ error เป็น AbortError (ctrl.signal.aborted) ไม่ต้อง set state ถ้าได้ 401 ให้เรียก logout() หรือ redirect ไป /login และพิจารณา retry แบบ backoff 1-2 ครั้ง
- Verifier: auth-provider.tsx:108 ใช้ .catch(() => setPermissions([])) จับทุก error รวมถึง AbortError ด้วย effect มี deps แค่ [status, user?.employeeCode] และไม่มีการ retry PermissionGuard จึงขึ้น 'Insufficient permissions' ถ้าการ fetch ครั้งแรกล้มเหลว จนกว่าจะ reload ส่วน AbortError จะทำให้เห็นแค่แวบเดียว เพราะ fetch ใหม่จะเขียนทับค่าในภายหลัง

### M53. หน้า Profile ส่วนใหญ่เป็นข้อมูล mock ที่แสดงเหมือนเป็นข้อมูลของผู้ใช้เอง — CONFIRMED
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/profile/profile-stats.tsx:24`
- ปัญหา: ส่วนที่เป็นข้อมูลคงที่มีดังนี้: stats ทั้ง 5 ตัว (profile-stats.tsx:24-69), ใบรับรอง CDL/Hazmat, เบอร์โทร, depot และกะ (tab-overview.tsx:10-51), timeline ใน Activity (tab-activity.tsx:17-53), 'Eastern Seaboard · Sector 04' กับ 'clocked in 06:12' (profile-hero.tsx:108-115) และ footer 'Profile last synced 12 sec ago' กับ build 4aa1133 (app/profile/page.tsx:21-25) ปุ่ม Share/Edit profile (profile-hero.tsx:128-138) และ 'View full log' ไม่มี onClick ส่วนตัวเลือกภาษาและ toggle แจ้งเตือน (tab-preferences.tsx:44-51, 159) ไม่ได้บันทึกที่ไหน และ error จาก useProfile ก็ไม่ถูกแสดงเลย
- ผลกระทบ: ผู้ใช้ทุกคนเห็นประวัติ ใบรับรอง และตัวเลขผลงานชุดเดียวกันซึ่งไม่ใช่ของตัวเอง ปุ่มกดแล้วไม่มีอะไรเกิดขึ้น และเมื่อ /api/profile ล้มเหลวก็ไม่มีอะไรบอก
- แก้: เก็บไว้เฉพาะข้อมูลจริง คือชื่อ, employeeId, role, email และรูปจาก /api/profile รวมถึง ThemeSwitcher ส่วนที่เหลือให้ลบหรือติดป้ายว่าเป็นตัวอย่าง และให้ ProfileHero แสดง profile.error เมื่อ status === "error"
- Verifier: app/profile/page.tsx hardcode 'Profile last synced 12 sec ago' กับ build 4aa1133 ไว้ ปุ่ม Share/Edit profile ใน profile-hero.tsx:128,132 ไม่มี onClick และ toggle ใน tab-preferences.tsx:159 เป็นแค่ useState ภายใน ส่วน profile-hero ใช้ข้อมูลเฉพาะเมื่อ status==='ready' โดยไม่แสดง error เลย

### M54. ปุ่ม 'Try again' ในหน้า error อ่านไม่ออกใน dark mode — CONFIRMED
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/app/error.tsx:39`
- ปัญหา: ปุ่มใช้ bg-[var(--color-ink-900)] คู่กับ text-white แต่ใน .dark ค่า --color-ink-900 คือ #f4f7fe (globals.css:130) และตอน hover ค่า ink-800 คือ #e1e7f4 จึงกลายเป็นตัวอักษรขาวบนพื้นเกือบขาว
- ผลกระทบ: เมื่อเกิด route error ใน dark mode ปุ่มหลักสำหรับกู้หน้าแทบมองไม่เห็นข้อความ ผู้ใช้จึงไม่รู้ว่าต้องกดอะไร
- แก้: เปลี่ยนเป็น bg-[var(--color-brand-900)] text-white แบบเดียวกับปุ่มหลักอื่นๆ (เช่น auth-panel SubmitButton) หรือเพิ่ม dark:text-[var(--color-surface)]
- Verifier: app/error.tsx:39 ใช้ bg-[var(--color-ink-900)] text-white แต่ในบล็อก .dark ของ globals.css:130 ค่า --color-ink-900 คือ #f4f7fe และ ink-800 คือ #e1e7f4 ใน dark mode จึงกลายเป็นตัวอักษรขาวบนพื้นเกือบขาว

### M55. ลิงก์ Order ในคิว Jobs ใช้ ?orderId= ที่หน้า list ไม่อ่าน และแสดง GUID แทน OrderRef — CONFIRMED
- กลุ่ม: templates · code
- ที่: `frontend/components/planning/jobs-experience.tsx:344`
- ปัญหา: ลิงก์ทั้งใน row (บรรทัด 344) และใน drawer (บรรทัด 583) ชี้ไป `/delivery-orders/list?orderId=${job.deliveryOrderId}` แต่ orders-experience.tsx อ่าน query param แค่ priority/transport/q/hasFailedTrip/hasActiveJob/page/size/sortBy/sortDir ไม่มี orderId ข้อความลิงก์ยังเป็น `deliveryOrderId.slice(0,8)…` ซึ่งเป็น GUID ภายใน เพราะ JobDto (lib/api/jobs.ts:20) มีแค่ deliveryOrderId
- ผลกระทบ: operator กดจาก job ที่ fail เพื่อไปดู order แต่ได้หน้า order list ทั้งหมดที่ไม่ได้กรอง ต้องไปค้นเอง และค้นจาก GUID 8 ตัวก็ไม่ได้ ผิด house rule ที่ให้ OrderRef เป็นตัวอ้างอิงที่แสดงต่อผู้ใช้
- แก้: ให้ JobDto คืน orderRef มาด้วยแล้วลิงก์ไป `/delivery-orders/list?q=<orderRef>` ซึ่งหน้า list รองรับอยู่แล้ว พร้อมแสดง orderRef แทน GUID อีกทางคือเพิ่มการอ่าน `orderId` ใน orders-experience ให้เปิด drawer ของ order นั้น
- Verifier: jobs-experience.tsx:344 และ :583 ลิงก์ไป `?orderId=` แต่ orders-experience.tsx อ่านแค่ priority/transport/q/hasFailedTrip/hasActiveJob/page/size/sortBy/sortDir ไม่มีที่ไหนอ่าน orderId ทั้งใน orders-experience และ app/delivery-orders/list ข้อความลิงก์เป็น deliveryOrderId.slice(0,8) ซึ่งเป็น GUID ไม่ใช่ OrderRef

### M56. สลับ map เร็วๆ แล้ว station ของ map เก่าอาจมาทับ map ที่เลือกอยู่ — CONFIRMED
- กลุ่ม: templates · code
- ที่: `frontend/components/facility/maps-experience.tsx:257`
- ปัญหา: refreshStations(mapId) เรียก getStations แล้ว setStations(all) ทันทีที่ได้ผล ไม่มี AbortController และไม่ได้เช็กว่า mapId ยังตรงกับ map ที่เลือกอยู่ effect บรรทัด 270-272 ก็ไม่มี cleanup ถ้ากด map A แล้วตามด้วย B และ response ของ A ช้ากว่า ผลของ A จะมาทับทีหลัง
- ผลกระทบ: Station directory, KPI และ edit drawer จะแสดง station ของ map A ทั้งที่ tab ที่เลือกอยู่คือ map B ถ้าผู้ใช้ force-offline หรือแก้ code จากตารางนี้ จะไปแก้ station ผิด map
- แก้: ใน effect ให้สร้าง AbortController หรือ flag `cancelled` ส่ง signal เข้า getStations และไม่ setStations/setLoading เมื่อ request ถูกยกเลิก หรือเทียบ mapId กับ ref ของ map ที่เลือกล่าสุดก่อน set state
- Verifier: refreshStations (maps-experience.tsx:257-268) เรียก setStations(all) โดยไม่เทียบ mapId กับ map ที่เลือกอยู่ ไม่มี abort และ effect บรรทัด 270-272 ไม่มี cleanup ถ้า response มาไม่ตรงลำดับ station ของ map เก่าจะมาทับได้จริง ทั้งนี้ต้องสลับ map เร็วและ response ต้องสลับลำดับกันด้วย

### M57. Drawer/dialog หลายตัวกด Esc ปิดไม่ได้ และไม่มี role="dialog"/aria-modal — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: templates · uiux
- ที่: `frontend/components/order-templates/create-from-template-dialog.tsx:228`
- ปัญหา: CreateFromTemplateDialog, OrderTemplateDrawer (order-templates/detail-drawer.tsx:62), ConfirmDeleteDialog ทั้งสองตัว (order-templates-experience.tsx:530, action-templates/templates-experience.tsx:581) และ StationEditDrawer (facility/station-edit-drawer.tsx:155) ไม่มี keydown handler สำหรับ Escape ไม่มี role="dialog" aria-modal และไม่ย้าย focus เข้า panel ตอนเปิด ขณะที่ ActionTemplateDialog กับ JobDetailDrawer ใน slice เดียวกันปิดด้วย Esc ได้ และ components/delivery-orders/reopen-dialog.tsx:46,74 ใส่ทั้ง Esc และ role/aria-modal ไว้แล้ว
- ผลกระทบ: ผู้ใช้คีย์บอร์ดปิดได้แค่การ tab ไปหาปุ่ม X focus ยังค้างอยู่หลัง backdrop ทำให้ tab ไปโดนแถวในตารางด้านหลังได้ screen reader ก็ไม่รู้ว่ามี modal เปิดอยู่ พฤติกรรมยังไม่เหมือนกันในแต่ละหน้าใน slice เดียวกันอีกด้วย
- แก้: ใช้ pattern เดียวกับ reopen-dialog.tsx ทุกตัว คือเพิ่ม useEffect ที่ฟัง Escape (ข้ามเมื่อ busy) ใส่ role="dialog" aria-modal="true" aria-labelledby บน panel และ focus ปุ่มหลักหรือ input แรกตอนเปิด
- Verifier: grep ไม่เจอ Escape, role="dialog" หรือ aria-modal ใน create-from-template-dialog, order-templates/detail-drawer, ConfirmDeleteDialog ทั้งสองไฟล์ และ station-edit-drawer ตัว OverlayBackdrop primitive ก็ไม่ได้จัดการ Esc ให้ ขณะที่ jobs-experience.tsx:516, action-templates/create-dialog.tsx:66 และ reopen-dialog.tsx:46,74 มี Esc แล้ว จึงไม่สอดคล้องกันจริง

### M58. ข้อความสี amber บนพื้นอ่อนใน light mode contrast ต่ำ (~2:1) — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/planning/badges.tsx:34`
- ปัญหา: TONE_BG.amber ใช้ `text-[var(--color-amber)]` (#f59e0b) บน --color-amber-soft (#fef0d4) กับ badge Assigned/Committed ขนาด 10.5px ใน globals.css:52-54 เขียนไว้ชัดว่า amber บนพื้นขาวได้ประมาณ 2:1 และมี --color-amber-600 ไว้ใช้กับข้อความ ปัญหาเดียวกันอยู่ใน create-from-template-dialog.tsx:408 (กล่อง pending 'still confirming') และ :434 ('outcome unknown')
- ผลกระทบ: สถานะ Assigned/Committed ในคิว Jobs และข้อความ 'กำลังยืนยัน dispatch' อ่านยากใน light mode ซึ่งเป็นข้อความที่ operator ต้องอ่านก่อนตัดสินใจกด dispatch ซ้ำ
- แก้: ใน light mode เปลี่ยนสีข้อความเป็น `text-[var(--color-amber-600)]` (หรือ #8a4a07 แบบที่ PriorityChip ใช้) และคง `dark:text-[var(--color-amber)]` ไว้สำหรับ dark mode
- Verifier: badges.tsx:34 ใช้ text-[var(--color-amber)] (#f59e0b) บน --color-amber-soft (#fef0d4) และ globals.css บอกไว้เองว่า #f59e0b บนพื้นขาวได้ประมาณ 2:1 พร้อมมี --color-amber-600 ไว้ใช้กับข้อความ create-from-template-dialog ก็ใช้ text-[var(--color-amber)] บนพื้น amber/10 ด้วย ข้อเสนอเปลี่ยนเป็น amber-600 สมเหตุสมผล แม้ #d97706 เองจะได้ contrast ประมาณ 3:1 เท่านั้น

## LOW

### L1. Replay dialog ติดป้าย "(UTC)" แต่ค่าที่กรอกเป็นเวลาท้องถิ่นของ browser — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/replay-dialog.tsx:164`
- ปัญหา: label เขียนว่า "From (UTC)" และ "To (UTC)" แต่ค่าเริ่มต้นมาจาก `toDateTimeLocalInput` (lib/datetime.ts:76 ใช้ getHours ซึ่งเป็นเวลาท้องถิ่น) และตอนส่งก็ใช้ `fromDateTimeLocalInput` (:83 ใช้ `new Date(local)` ซึ่งตีความเป็นเวลาท้องถิ่น) ข้อความยืนยันที่ :240 ก็แสดงเวลาท้องถิ่นแต่สื่อว่าเป็น UTC
- ผลกระทบ: operator ที่เชื่อป้ายแล้วกรอกเวลา UTC เช่นจาก log จะได้ช่วง replay เลื่อนไป 7 ชั่วโมงในโซน Bangkok ทำให้ rebuild read model ผิดช่วง
- แก้: เปลี่ยนป้ายเป็น "From (local time)" หรือถ้าต้องการรับค่าเป็น UTC จริงก็แปลงด้วย `Date.UTC` และใช้ helper ฝั่ง UTC ทั้งตอนตั้งค่าเริ่มต้นและตอนส่ง
- Verifier: ป้ายผิดจริง: ป้ายเขียน "From (UTC)"/"To (UTC)" แต่ toDateTimeLocalInput ใช้ getHours (เวลาท้องถิ่น, datetime.ts:33) และ fromDateTimeLocalInput ใช้ new Date(local) ซึ่งตีความเป็นเวลาท้องถิ่น แต่ลดเหลือ LOW เพราะ IProjectionReplayService ยัง register เป็น NotImplementedReplayService (ProjectionServiceCollectionExtensions.cs:18) และ endpoint ตอบ 501 ทุกครั้ง ตอนนี้จึงยังไม่มีการ replay ผิดช่วงเกิดขึ้นได้

### L2. กด toggle permission หลายแถวติดกัน แล้ว load() ทับกันจน checkbox แสดงสถานะผิด — PLAUSIBLE
- กลุ่ม: admin · code
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:258`
- ปัญหา: `onTogglePermission` เรียก `await load()` ทุกครั้งที่ toggle สำเร็จ และ `load` (:137) ไม่มี sequence guard หรือ abort ถ้ากด grant A แล้วกด B ทันที `getSystem` ของรอบแรกซึ่งยังไม่เห็น B อาจ resolve ทีหลังแล้ว `setData` ทับข้อมูลใหม่ ทำให้ B แสดงว่ายังไม่ได้ grant (PLAUSIBLE เพราะขึ้นกับ timing จริงตอน runtime)
- ผลกระทบ: checkbox แสดงสถานะไม่ตรงกับ DB และการคลิกครั้งถัดไปส่ง op ผิดทาง (`currentlyGranted` ผิด) เช่นจะ grant ซ้ำแทนที่จะ revoke ผู้ดูแลอาจเข้าใจผิดว่า revoke สิทธิ์ของ source system ไปแล้ว
- แก้: ใส่ request counter ใน load (ref ที่เพิ่มค่าทุกครั้งที่เรียก และ setData เฉพาะถ้าเป็นรอบล่าสุด) หรืออัปเดต `data.permissions` แบบ local จากผล grant/revoke แทนการ refetch ทั้งก้อน
- Verifier: โค้ดตรงตามที่อธิบาย: onTogglePermission เรียก await load() และ load (:137-148) ไม่มี guard ส่วน checkbox disable แค่ทีละ code (togglingCodes) จึงกดแถวอื่นต่อได้ทันที แต่การที่ getSystem รอบแรกจะ resolve หลังรอบที่สองขึ้นกับ timing ของเครือข่าย และถ้าเกิดก็แค่แสดงผลค้าง กด Refresh ก็หาย (grant ซ้ำน่าจะ idempotent) จึงลดเหลือ LOW

### L3. ใช้ token ที่ไม่มีอยู่จริง (--color-brand-600, --color-surface-1) ทำให้ลิงก์ system key ไม่มีสี — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-systems-experience.tsx:243`
- ปัญหา: globals.css นิยาม brand แค่ 900/800/500/400/200 และไม่มี `--color-surface-1` แต่โค้ดใช้ `text-[var(--color-brand-600)]` (ที่นี่และ iam-system-detail-experience.tsx:349) กับ `bg-[var(--color-surface-1)]` (detail :1075, :1120) ค่าจึง invalid และ browser ไปใช้สีที่สืบทอดมาหรือพื้นโปร่งใสแทน
- ผลกระทบ: system key ในตารางซึ่งเป็นทางเดียวที่จะเข้าหน้า detail ได้ ดูเหมือนข้อความธรรมดา จะรู้ว่าเป็นลิงก์ก็ต่อเมื่อ hover ส่วนกล่อง token ที่ reveal แล้วก็ไม่มีพื้นหลัง
- แก้: เปลี่ยนเป็น `--color-brand-500` และ `--color-surface-soft` (permission-guard.tsx:37 และ :44 ซึ่งอยู่นอก slice ก็ใช้ surface-1 และ surface-2 ที่ไม่มีอยู่เหมือนกัน)
- Verifier: ยืนยันแล้ว: globals.css นิยามไว้แค่ brand-900/800/500/400/200 และ surface/surface-soft/surface-raised ไม่มี --color-brand-600 และ --color-surface-1 ดังนั้น iam-systems-experience.tsx:243 (ลิงก์ system key), detail:349 (ลิงก์ back), :1075 (badge) และ :1120 (กล่อง token) จึงได้ค่า invalid แล้วใช้สีที่สืบทอดมาหรือพื้นโปร่งใสแทน

### L4. วันเวลาใช้ toLocaleString แทน DateTime primitive — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:627`
- ปัญหา: วันที่ทั้งหมดในหน้า systems, detail และ subscriptions ใช้ `new Date(x).toLocaleString()` หรือ `toLocaleDateString()` จุดที่เกิด: detail :384, 451, 533, 627, 640, 1058, 1329, iam-systems-experience.tsx:263, iam-system-subscriptions-experience.tsx:193 ขณะที่ audit log และ projections ใช้ `<DateTime>` หรือ lib/datetime ตาม house rule
- ผลกระทบ: browser ที่ตั้ง locale เป็น th-TH จะแสดงปีเป็นพุทธศักราช (2569) ในรูปแบบ D/M/YYYY ส่วน en-US แสดง M/D/YYYY วันหมดอายุของ token จึงอ่านกำกวม และไม่ตรงกับรูปแบบ YYYY-MM-DD HH:mm ของหน้าอื่น
- แก้: เปลี่ยนเป็น `<DateTime value={...} />` หรือ `formatDateTime()` จาก lib/datetime.ts ในจุดที่เป็น string template เช่น helpText และ verdict
- Verifier: ยืนยันแล้ว: detail :384, 451, 1058, 1329, systems:263 และ subscriptions:193 ใช้ toLocaleString/toLocaleDateString โดยไม่ระบุ locale ซึ่งขัดกับ house rule (review-frontend.md:48 ให้วันที่ผ่าน lib/datetime.ts หรือ primitives/date-time.tsx) รูปแบบจึงต่างกันไปตาม locale ของ browser เช่น th-TH แสดงเป็นปี พ.ศ.

### L5. ปุ่ม write แสดงให้ผู้ที่มีแค่สิทธิ์ read และบางหน้าไม่มี PermissionGuard — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-systems-experience.tsx:166`
- ปัญหา: หน้า systems, detail และ subscriptions guard แค่ SystemRead หรือ SubscriptionRead แต่แสดงปุ่ม New system, Rotate, Delete, Issue JWT, checkbox grant และ Add subscription ทุกครั้ง ทั้งที่ backend มี `dtms:iam:system:write` และ `subscription:write` แยกไว้ (Permissions.cs:141, :144) ส่วนหน้า audit-log และ permissions (app/admin/audit-log/page.tsx, permissions/page.tsx) ไม่มี PermissionGuard เลย ทั้งที่ backend ต้องการ AuditRead และ PermissionRead
- ผลกระทบ: ผู้ใช้แบบ read-only กด Rotate แล้วผ่าน confirm ที่เขียนว่า "key จะหยุดทำงานทันที" ก่อนจะเจอ 403 ส่วนผู้ไม่มีสิทธิ์ audit จะเห็น "Failed to load audit log: 403" แทน panel บอกว่าต้องขอ permission อะไร (backend ยังบังคับสิทธิ์ถูกต้อง จึงไม่ใช่ช่องโหว่)
- แก้: mirror SystemWrite, SubscriptionWrite, AuditRead และ PermissionRead ลงใน lib/auth/permissions.ts แล้ว gate ปุ่มด้วย `useAuth().hasPermission` แบบ carriers-experience.tsx:45-46 และครอบ 2 หน้านั้นด้วย PermissionGuard พร้อมใส่ `requires` ให้รายการใน left-rail.tsx
- Verifier: ยืนยันแล้ว: app/admin/audit-log/page.tsx และ permissions/page.tsx ไม่มี PermissionGuard ขณะที่ backend ต้องการ AuditRead (IamEndpoints.cs:88) และ PermissionRead (:66) ส่วน lib/auth/permissions.ts mirror ไว้แค่ SystemRead และ SubscriptionRead และไม่มี component ใน admin เรียก hasPermission เลย ปุ่ม write จึงแสดงให้ผู้ใช้ read-only แต่ backend ยังบังคับสิทธิ์ถูกต้อง ระดับ LOW จึงเหมาะสม

### L6. Token source modal: ปุ่มขึ้นว่า "Saving…" ทั้งที่ยังไม่ได้เลือก system — CONFIRMED
- กลุ่ม: admin · uiux
- ที่: `frontend/components/admin/iam-system-detail-experience.tsx:1714`
- ปัญหา: ส่ง `saving={submitting || (mode === "borrow" && !sourceKey)}` ให้ ModalFooter เพื่อใช้ disable ปุ่ม แต่ ModalFooter ใช้ prop เดียวกันเลือก label ด้วย พอเลือก Borrow โดยยังไม่เลือก owner ปุ่มจึงขึ้น "Saving…"
- ผลกระทบ: ผู้ใช้เข้าใจว่ากำลังบันทึกอยู่หรือระบบค้าง ทั้งที่จริงแค่ยังกรอกไม่ครบ
- แก้: เพิ่ม prop `disabled` แยกจาก `saving` ใน ModalFooter แล้วส่ง `disabled={mode === "borrow" && !sourceKey}`
- Verifier: ยืนยันแล้ว: :1714 ส่ง saving={submitting || (mode === "borrow" && !sourceKey)} และ ModalFooter (:1900-1905) ใช้ prop เดียวกันทั้ง disable ปุ่มและเลือก label {saving ? savingLabel : saveLabel} พอเลือก Borrow โดยยังไม่เลือก owner ปุ่มจึงขึ้น "Saving…"

### L7. กราฟ stacked ของ SLA breach สูงเกินจำนวน order จริง — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/reports/sla-breach-report.tsx:48`
- ปัญหา: `onTime = total - max(confirmBreached, completeBreached)` แล้ว stack `confirmBreached + completeBreached` ต่อ (บรรทัด 117-129) ความสูงรวมจึงเป็น `total + min(c, p)` ซึ่งเกิน total ทุกครั้งที่ breach ทั้งสองแบบมากกว่า 0 ถ้าสองชุดนี้ซ้อนกัน order ที่ breach ทั้งคู่จะถูกนับสองครั้ง ถ้าไม่ซ้อนกัน onTime ก็คำนวณผิด
- ผลกระทบ: แท่งกราฟของแต่ละ priority สูงกว่าคอลัมน์ Total ในตาราง และสัดส่วน on-time ที่เห็นจากกราฟผิด
- แก้: ให้ backend ส่ง `onTime` กับ `bothBreached` มา หรือเปลี่ยนเป็น grouped bar (ไม่ใช้ stackId) แล้วแสดง total เป็นแท่งแยก
- Verifier: สูตรคำนวณตรงกับที่ reviewer บอก: onTime = total - max(c,p) แล้ว stack c+p ต่อ ความสูงรวมจึงเป็น total + min(c,p) และเกิน total ทุกครั้งที่ breach ทั้งสองแบบมากกว่า 0 แต่ปัญหานี้กระทบแค่สัดส่วนของกราฟ tile และตารางยังแสดงตัวเลขถูก จึงลดเป็น LOW

### L8. กราฟใน Reports ใช้สี tick และ tooltip ค่า default ของ recharts อ่านยากใน dark mode — PLAUSIBLE
- กลุ่ม: analytics · uiux
- ที่: `frontend/components/reports/job-failures-report.tsx:106`
- ปัญหา: `tick={{ fontSize: 11 }}` ไม่กำหนด fill จึงได้ค่า default #666 และ `<Tooltip />` ไม่มี contentStyle จึงได้กล่องขาว default เป็นแบบนี้ทั้ง lead-time-report.tsx:94-96, sla-breach-report.tsx:113-115, top-failures-report.tsx:102-109, vehicle-performance-report.tsx:116-123 และ manual-operators-report.tsx:93-95 ขณะที่ order-status-chart.tsx:77-96 กำหนด `fill: var(--color-ink-500)` และ tooltip ที่ใช้ token ไว้ถูกต้องแล้ว
- ผลกระทบ: บนพื้นเข้มของ dark mode ตัวอักษรแกน #666 มี contrast ราว 3.3:1 ต่ำกว่าเกณฑ์ 4.5:1 และ tooltip ขาวจ้าขึ้นมากลางจอมืด (PLAUSIBLE เพราะยังไม่ได้ดูตอนรันจริง)
- แก้: แยก props ของแกนและ tooltip ที่ใช้ token (จาก order-status-chart.tsx) ออกมาเป็น helper ที่ใช้ร่วมกัน แล้วใช้ในทุก report
- Verifier: ในโค้ดจริงตามที่บอก: report ใช้ tick={{fontSize:11}} ไม่มี fill และใช้ <Tooltip /> แบบ default ส่วน order-status-chart ใช้ token แต่ contrast ใน dark mode ต้องดูตอนรันจริงถึงจะยืนยันได้ จึงให้ PLAUSIBLE ผลกระทบเป็นเรื่องอ่านยาก ไม่ได้ขวางการใช้งาน จึงให้ LOW

### L9. แกน X ของกราฟ order/fleet แสดงแค่เวลา ไม่มีวันที่ แม้เลือกช่วง 7d หรือ 30d — CONFIRMED
- กลุ่ม: analytics · uiux
- ที่: `frontend/components/dashboard/order-status-chart.tsx:49`
- ปัญหา: `label: formatTime(b.bucketHour)` ถูกใช้เป็น `dataKey` ของ XAxis (บรรทัด 76) fleet-utilization-chart.tsx:44 และ :71 ก็เหมือนกัน หน้า Order analysis ตั้งค่าเริ่มต้นเป็น 7d (orders-analysis-experience.tsx:36)
- ผลกระทบ: tick บนแกนเป็น "08:00" ซ้ำกันหลายวัน ดูไม่ออกว่าจุดไหนเป็นวันไหน ต้อง hover tooltip ทีละจุด
- แก้: ใส่ `tickFormatter` ที่แสดงวันที่ (เช่น dd/MM) เมื่อช่วงข้อมูลเกิน 24 ชม. โดยใช้ helper ใน lib/datetime.ts
- Verifier: ยืนยันแล้ว: formatTime คืนค่าแค่ HH:mm และถูกใช้เป็น dataKey="label" ของ XAxis ทั้งใน order-status-chart และ fleet-utilization-chart:44/71 ส่วน orders-analysis ตั้งค่าเริ่มต้นเป็น 7d เลยเห็นเวลาซ้ำกันหลายวัน มีแค่ tooltip ที่ใช้ formatDateTime

### L10. StatusTimelineSection ไม่ล้างข้อมูลเก่าเมื่อเปลี่ยน entity — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/projection/status-timeline-section.tsx:77`
- ปัญหา: effect ที่ทำงานเมื่อ `entityId` เปลี่ยนเรียก `setLoading(true)` แต่ไม่ได้ `setData(null)` และ skeleton จะแสดงเฉพาะตอน `loading && data === null` (บรรทัด 135) trip-detail-drawer สลับ trip ได้ผ่าน `onOpenTrip`
- ผลกระทบ: ระหว่างโหลด timeline ของ trip ใหม่ จะยังเห็น timeline ของ trip เดิมอยู่ใต้หัวข้อของ trip ใหม่ ถ้าโหลดล้มเหลว timeline เก่าจะค้างอยู่คู่กับข้อความ error
- แก้: เพิ่ม `setData(null)` ตอนต้น effect ในเคสที่ `entityId` มีค่า
- Verifier: ยืนยันแล้ว: effect ของ StatusTimelineSection เรียก setLoading(true) แต่ไม่เรียก setData(null) และ skeleton แสดงเฉพาะตอน loading && data===null trip-detail-drawer ไม่ล้าง data เมื่อเปลี่ยน trip ส่วนนี้จึงไม่ unmount เมื่อ data.id เปลี่ยน timeline เก่าจะค้างอยู่ชั่วครู่ (หรือค้างคู่กับ error) jobs-experience ก็เป็นแบบเดียวกัน แต่เกิดแค่ช่วงสั้นๆ

### L11. Reports API แสดง error แบบ generic และไม่เคารพ Retry-After — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/lib/api/reports.ts:93`
- ปัญหา: `if (!res.ok) throw new Error(`Failed: ${res.status}`)` ไม่ได้เรียก `throwIfRateLimited` และไม่อ่าน body จาก server (ที่บรรทัด 49 และทุก getter ในไฟล์ก็เหมือนกัน) ขณะที่ `fetchJson` ใน lib/api/dashboard.ts:44-60 ทำทั้งสองอย่างแล้ว
- ผลกระทบ: ผู้ใช้เห็นแค่ "Failed: 500" ไม่เห็นสาเหตุ และพอโดน 429 (ตอนนี้ rate limit enforce แล้ว) usePollSchedule จะ back-off ตามค่าที่เดาเอง ไม่ได้ใช้ค่าที่ server บอก
- แก้: ย้าย `fetchJson` ของ dashboard.ts ไปไว้ที่ที่ใช้ร่วมกันได้ แล้วใช้ใน reports.ts ด้วย
- Verifier: ยืนยันแล้ว: getter ใน reports.ts throw Error(`Failed: ${status}`) โดยไม่เรียก throwIfRateLimited ขณะที่ usePollSchedule:93-94 ใช้ retryAfterSeconds เฉพาะเมื่อเป็น RateLimitError และ fetchJson ใน dashboard.ts ทำทั้งสองอย่างไว้แล้ว แต่ report poll ทุก 5 นาทีอยู่แล้ว ผลกระทบจริงจึงน้อย

### L12. WindowToggle และ buildWindow ถูก copy ไว้หลายที่ สี series ใช้ hex และ token ที่ไม่มีอยู่จริง — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/dashboard/orders-analysis-experience.tsx:160`
- ปัญหา: `WindowToggle` และ `buildWindow` ถูก copy ไว้ที่ orders-analysis:26/160, robots-analysis:30/204, kpi-rail:38 และ dispatch-funnel:43 ซ้ำกับ components/reports/window-toggle.tsx ส่วนสี series ใช้ raw hex (order-status-chart.tsx:26-33, fleet-utilization-chart.tsx:27-31) และ report อ้าง `--color-mint`, `--color-amber-500`, `--color-wine` (sla-breach-report.tsx:117,122, vehicle-performance-report.tsx:125-127) ซึ่งไม่มีใน app/globals.css จึงตกไปใช้ hex fallback
- ผลกระทบ: แก้ toggle หรือการคำนวณช่วงเวลา (เช่นสอง finding ข้างบน) ต้องแก้ 5 ที่ และสี Completed/On-time ไม่ตรงกับ `--color-success` และไม่เปลี่ยนตาม dark mode
- แก้: ใช้ `WindowToggle`/`buildWindowRange` จาก components/reports/window-toggle.tsx (หรือย้ายไปไว้ใน primitives) และเปลี่ยนสี series ไปใช้ token ที่มีจริง (`--color-success`, `--color-amber`, `--color-coral`, `--color-ink-400`)
- Verifier: ส่วนใหญ่ถูก: buildWindow/defaultWindow และ WindowToggle มีหลายชุดซ้ำกันจริง สี series เป็น raw hex ส่วน --color-mint และ --color-wine ไม่มีใน globals.css จึงตกไปใช้ fallback แต่มีจุดที่ผิดอยู่หนึ่งจุด: --color-amber-500 มีอยู่จริงจาก default palette ของ Tailwind v4 (เจอใน .next build CSS)

### L13. ปุ่ม toggle ช่วงเวลาและแท็บของ Reports ไม่บอกสถานะที่เลือกให้ screen reader — CONFIRMED
- กลุ่ม: analytics · uiux
- ที่: `frontend/components/reports/reports-experience.tsx:57`
- ปัญหา: แถบแท็บเป็น `<nav>` ที่มีแต่ `<button>` ไม่มี `role="tablist"`/`aria-selected` และ window-toggle.tsx:42-54 ก็ไม่มี `aria-pressed` บอกสถานะได้ทางสีอย่างเดียว
- ผลกระทบ: ผู้ใช้ screen reader ไม่รู้ว่าตอนนี้อยู่แท็บไหนหรือเลือกช่วงเวลาไหนอยู่
- แก้: ใส่ `aria-pressed={value === opt}` ที่ปุ่ม toggle และใส่ `role="tab"`/`aria-selected` ที่ปุ่มแท็บ
- Verifier: ยืนยันแล้ว: reports-experience.tsx และ window-toggle.tsx ไม่มี aria-* หรือ role เลย สถานะที่เลือกบอกได้ทางสีอย่างเดียว

### L14. ProjectionLagBanner กับ ConnectionIndicator เป็น dead code — CONFIRMED
- กลุ่ม: analytics · code
- ที่: `frontend/components/projection/projection-lag-banner.tsx:18`
- ปัญหา: grep ทั้ง frontend แล้วไม่มีที่ไหน import `ProjectionLagBanner` หรือ `ConnectionIndicator` (components/projection/connection-indicator.tsx) เจอแค่ใน comment ของ use-hub-subscription.ts:58
- ผลกระทบ: ทำให้คนอ่านเข้าใจผิดว่าระบบเตือนเรื่อง projection lag อยู่แล้ว ทั้งที่จริงไม่มี และต้องดูแลโค้ดที่ไม่ได้ใช้
- แก้: ลบทิ้ง หรือเอา ProjectionLagBanner ไปใช้ในหน้า dashboard และ reports (ร่วมกับการแก้ freshness chip)
- Verifier: ยืนยันแล้ว: grep ใน app/components/lib เจอ ProjectionLagBanner และ ConnectionIndicator แค่ในไฟล์ของตัวเองกับ comment ใน use-hub-subscription.ts ไม่มีที่ไหน import ไปใช้

### L15. client หลายตัวทิ้งข้อความจาก server และไม่เรียก throwIfRateLimited ทั้งที่ใช้กับ poller — CONFIRMED
- กลุ่ม: api-layer · code
- ที่: `frontend/lib/api/reports.ts:188`
- ปัญหา: getOrdersReportSummary, getSlaBreachReport และฟังก์ชัน report อื่นใน reports.ts:188-371, getProjectionStatus (admin-projections.ts:37), getVendorHealth (admin-system-status.ts:34), listPermissions/queryAuditLog (iam.ts:44,68) และ listSystems (iam-systems.ts:140) throw แค่ `Failed: ${status}` ไม่อ่าน body และไม่เรียก throwIfRateLimited ทั้งที่ projections-experience, system-status-experience และ report ต่างๆ เรียกผ่าน useProjectionPoll → usePollSchedule ซึ่งมีโค้ดรองรับ RateLimitError ไว้แล้วโดยเฉพาะ (use-poll-schedule.ts:93)
- ผลกระทบ: ตอนนี้ RateLimit__Enforce=true แล้ว เมื่อโดน 429 หน้าเหล่านี้แสดงแค่ 'Failed: 429' แทนข้อความ 'Try again in N seconds' และ poller backoff ตามสูตร exponential ไม่ใช่ตาม Retry-After ของ server กรณี 403 ผู้ใช้ก็ไม่รู้ว่าขาด permission อะไร เพราะข้อความจาก backend หายไป
- แก้: ให้ทุก client ใช้ helper กลางตัวเดียวกับ finding ก่อนหน้า (await throwIfRateLimited(res) แล้วอ่าน message/detail) หรือย้ายไปใช้รูปแบบ api() ใน lib/api/trips.ts:87 ที่ทำครบอยู่แล้ว
- Verifier: ใช่ reports.ts (บรรทัดจริงคือ 49-231 ไม่ใช่ 188-371), admin-projections.ts:37, admin-system-status.ts:34, iam.ts:44/68, iam-systems.ts:141 throw แค่ status ไม่เรียก throwIfRateLimited ขณะที่ usePollSchedule:93 ใช้ RateLimitError.retryAfterSeconds ถ้ามี แต่ลดระดับเป็น LOW เพราะ quota ต่อผู้ใช้คือ 600/min จึงแทบไม่โดน 429 จาก poller และถ้าโดน exponential backoff ก็ยังถอยให้อยู่แล้ว ผลกระทบคือข้อความไม่ชัดเท่านั้น

### L16. route /api/admin/iam/systems/[key]/test-key ทำงานได้โดยไม่ต้องมี session — CONFIRMED
- กลุ่ม: api-layer · code
- ที่: `frontend/app/api/admin/iam/systems/[key]/test-key/route.ts:27`
- ปัญหา: route นี้ไม่ได้ใช้ proxyToBackend และไม่เรียก getServerToken เลย ใครก็ POST ได้ ทั้งที่ proxy.ts ไม่ guard /api (ตั้งใจไว้แบบนั้น) route จะเอา secret ไปแลกที่ /oauth/token แล้วเรียก /source/whoami ให้ จากนั้นคืน principal และ permissions ของ system กลับไป ส่วน route อื่นทุกตัวใต้ /api/admin ส่ง bearer ของผู้ใช้ไปและให้ backend ตรวจสิทธิ์
- ผลกระทบ: เครื่องใดก็ได้ใน LAN ที่ไม่ได้ login สามารถใช้ endpoint admin นี้ทดสอบ client secret ได้ไม่จำกัดจำนวนครั้ง (เป็น oracle สำหรับเดา secret) และ traffic ทั้งหมดมาจาก IP ของ Next จึงกิน quota anonymous ที่ใช้ร่วมกันทั้งระบบ (300/min)
- แก้: ตรวจ session ก่อนทำอย่างอื่น: `if (!(await getServerToken())) return NextResponse.json({ ok:false, message:'Not signed in.' }, { status: 401 })` แบบเดียวกับ app/api/storage/[...path]/route.ts:49 ถ้าต้องการให้เฉพาะผู้มีสิทธิ์ IAM ใช้ได้ ควรให้ backend มี endpoint test ที่มี RequirePermission แทนการเรียกตรงจาก Next
- Verifier: route นี้ไม่เรียก getServerToken และไม่ใช้ proxyToBackend จริง และ proxy.ts ไม่ guard /api (ตั้งใจ) ใครก็ POST ได้โดยไม่ต้องมี session แต่ลดเป็น LOW เพราะ backend /oauth/token เป็น endpoint ที่ partner อย่าง OMS เรียกได้โดยตรงอยู่แล้ว route นี้จึงไม่ได้เปิด oracle ใหม่ สิ่งที่เพิ่มคือการเดาผ่าน IP ของ Next ซึ่งกิน quota anonymous ที่ใช้ร่วมกัน และ endpoint ใต้ /api/admin ที่ไม่มี auth ซึ่งไม่สอดคล้องกับ route อื่น ควรเพิ่มการตรวจ session

### L17. passthroughErrors ข้ามการกรอง body ของ 5xx และทิ้ง Retry-After — PLAUSIBLE
- กลุ่ม: api-layer · code
- ที่: `frontend/lib/api/proxy-helpers.ts:126`
- ปัญหา: เมื่อ passthroughErrors=true (ใช้ใน admin/projections/[name]/replay และ operator/trips/[id]/acknowledge) โค้ดข้ามบล็อกบรรทัด 126-170 ทั้งหมด ข้อความดิบของ 5xx (HTML/stack trace) ที่บรรทัด 139-143 ตั้งใจไม่ให้หลุดจึงถูกส่งต่อ และ header Retry-After ของ 429 (บรรทัด 164) ไม่ถูกส่งต่อ
- ผลกระทบ: ReplayDialog อาจแสดง stack trace ดิบของ backend (replayProjector ใช้ text เป็น message) และ ack ใน PWA ผ่าน offline queue จะไม่รู้ว่าต้องรอนานเท่าไรเมื่อโดน rate limit
- แก้: ใน passthrough mode ยังควรแทน body string ของ 5xx ด้วย 'Server error' และ copy Retry-After เมื่อ status 429 ทั้งคงสถานะและ body JSON เดิมไว้
- Verifier: โค้ดเป็นไปตามที่บอก passthroughErrors ข้ามบล็อก 126-170 ทั้ง sanitize 5xx และ Retry-After แต่ผลกระทบเกินจริงไปบางส่วน: ack ใน PWA ผ่าน enqueueAction/sw.js ซึ่งไม่อ่าน Retry-After อยู่แล้ว (เจอ 429 ก็ทิ้งไว้ retry รอบ sync หน้า) การไม่ส่งต่อ header จึงไม่เปลี่ยนพฤติกรรม ส่วนการที่ ReplayDialog โชว์ stack trace ขึ้นกับว่า backend คืน body 5xx เป็น string ดิบหรือไม่ ซึ่ง ASP.NET ปกติคืน ProblemDetails จึงเป็นแค่ความเป็นไปได้ (มี route ที่สามคือ fleet/carriers/[code] ที่ใช้ passthrough ด้วย)

### L18. useHubSubscription ปล่อย onreconnected callback ค้าง แล้ว re-subscribe กลุ่มเก่าหลัง unmount — CONFIRMED
- กลุ่ม: core · code
- ที่: `frontend/lib/hooks/use-hub-subscription.ts:148`
- ปัญหา: connection.onreconnected/onreconnecting/onclose เป็น API แบบ append-only (SignalR ไม่มี off) effect run ทุกรอบจะเพิ่ม callback เข้าไปบน connection ที่ใช้ร่วมกันแบบ singleton และไม่เคยถูกเอาออก onReconnected เรียก subscribe() ซึ่ง invoke(subscribeMethod, ...subscribeArgs) โดยไม่เช็ก cancelled (บรรทัด 133-140) useTripSubscription (lib/realtime/hubs/trip-hub.ts) ส่ง tripId เป็น args ทุกครั้งที่เปิด drawer
- ผลกระทบ: ทุกครั้งที่ reconnect client จะกลับไป join group ของทุก trip/order ที่เคยเปิดใน session นั้นอีก server ยังคง push event ของกลุ่มเหล่านั้นมาเรื่อยๆ และ closure ก็สะสมขึ้นตามเวลาที่เปิดหน้าทิ้งไว้
- แก้: ให้ทั้ง onReconnected และ subscribe() return ทันทีเมื่อ cancelled เป็น true วิธีที่ดีกว่าคือให้ signalr-client.ts ลงทะเบียน onreconnected ครั้งเดียวต่อ connection แล้ว dispatch CustomEvent (แบบเดียวกับ DRAIN_CYCLE_EVENT) ให้ hook ใช้ addEventListener/removeEventListener ใน cleanup
- Verifier: onReconnected (148-152) เรียก subscribe() ที่ invoke โดยไม่เช็ก cancelled และ onreconnected/onclose ของ SignalR ไม่มี API off จึงสะสมทุกรอบ effect จริง แต่ cleanup ถอด connection.off handlers ของ event แล้ว ผลจริงคือ server push event ของกลุ่มเก่ามาโดยไม่มี handler รับ + closure รั่วเล็กน้อย ไม่เกิดบั๊กที่ผู้ใช้เห็น จึงลดเป็น LOW

### L19. Left rail ตอนหุบ: เปิด group ได้ด้วย hover อย่างเดียว คีย์บอร์ดเข้าเมนูย่อยไม่ได้ — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/shell/left-rail.tsx:607`
- ปัญหา: ตอน expanded=false (ค่า default) ปุ่ม trigger ของ RailGroup มี onClick={undefined} และ flyout เปิดผ่าน onMouseEnter อย่างเดียว ไม่มี onFocus/onKeyDown ทั้งที่ประกาศ aria-haspopup="menu" ไว้
- ผลกระทบ: ผู้ใช้คีย์บอร์ดเข้า Dashboard/Delivery order/Dispatch/Fleet/Access control จาก rail ไม่ได้ บนอุปกรณ์ touch ขนาด lg ก็ขึ้นกับการ emulate mouseenter ของ browser
- แก้: ตอนหุบให้ onClick เป็น toggle flyoutOpen เปิด flyout เมื่อ focus-within และปิดเมื่อกด Escape หรือ blur ออกนอก group
- Verifier: บรรทัด 599-607 ตอนหุบ onClick={undefined} และ flyout เปิดด้วย onMouseEnter อย่างเดียว ไม่มี focus/keydown แต่ผู้ใช้คีย์บอร์ดยังกด PanelToggle ขยาย rail แล้วใช้ accordion ได้ (expanded ค่าเริ่มต้น false แต่ persisted) จึงมีทางออก ลดเป็น LOW

### L20. usePollSchedule: trigger() ไม่ล้าง timer ที่รออยู่และยกเลิก request ที่กำลังวิ่ง — CONFIRMED
- กลุ่ม: core · code
- ที่: `frontend/lib/hooks/use-poll-schedule.ts:76`
- ปัญหา: run() ทำ abort request ก่อนหน้าแล้วยิงใหม่ แต่ไม่ clearTimer() ตอนเริ่ม ถ้า trigger ถูกเรียกก่อน interval จะครบไม่นาน timer เดิมจะยิงตามมาแล้ว abort request ที่ trigger เพิ่งเริ่ม ทั้งที่ comment บอกว่า 'while one is running it schedules the next run' แต่ code ไม่ได้ทำแบบนั้น
- ผลกระทบ: เสีย request ไปฟรีๆ (นับเข้า quota rate limit ต่อ user) และถ้า fetch ช้ากว่า debounce 500ms ของ hub hint ตอนหุ่นยนต์ทำงานหนาแน่น list อาจไม่เคยได้ผลตอบกลับเลย (PLAUSIBLE)
- แก้: ให้เรียก clearTimer() ตอนต้นของ run() และถ้ามี request กำลังวิ่งอยู่ (abortRef ยังไม่ settle) ให้ตั้ง flag pendingRerun แทนการ abort แล้วค่อยรันอีกรอบหลัง request เดิม settle
- Verifier: run() (61-101) abort request ก่อนหน้าแต่ไม่ clearTimer timer ที่ schedule ไว้จาก success ครั้งก่อนจะยังยิงแล้ว abort request ของ trigger จริง ทำให้เสีย request และในช่วง hint ถี่ trigger ที่ abort กันเองอาจไม่ได้ผลเลย (ส่วนนี้ PLAUSIBLE)

### L21. Left rail: 'Overview' ถูกไฮไลต์พร้อมหน้าย่อยของ /dashboard — CONFIRMED
- กลุ่ม: core · uiux
- ที่: `frontend/components/shell/left-rail.tsx:675`
- ปัญหา: ใช้ pathname.startsWith(c.href) ทำให้ href '/dashboard' match '/dashboard/orders' และ '/dashboard/robots' ด้วย บรรทัด 778 ก็มีปัญหาเดียวกัน
- ผลกระทบ: เมนูไฮไลต์ active 2 รายการพร้อมกัน ผู้ใช้สับสนว่าอยู่หน้าไหน
- แก้: ใช้เงื่อนไข pathname === c.href || pathname.startsWith(c.href + '/') และเลือกเฉพาะ child ที่ match ยาวที่สุด
- Verifier: กลุ่ม Dashboard มี children '/dashboard', '/dashboard/orders', '/dashboard/robots' (บรรทัด 73-85) และ active = pathname?.startsWith(c.href) ที่ 675 และ 778 ทำให้ Overview ไฮไลต์พร้อมหน้าย่อยจริง

### L22. NumberTicker ค้างที่ 0 ถ้า element อยู่ในขอบล่าง 20% ของ viewport ที่ไม่ scroll — PLAUSIBLE
- กลุ่ม: core · uiux
- ที่: `frontend/components/primitives/number-ticker.tsx:16`
- ปัญหา: useInView ใช้ { once: true, margin: "-20%" } ซึ่งหด root ลง 20% ทุกด้าน KPI tile ที่อยู่ในแถบล่างของหน้าที่ไม่มี scroll จะไม่เคย intersect และจะแสดงค่า 0 ตลอด ไม่ได้เคารพ prefers-reduced-motion ด้วย
- ผลกระทบ: KPI แสดง 0 ทั้งที่ค่าจริงไม่ใช่ 0 (PLAUSIBLE ขึ้นกับ layout และขนาดจอ ยังไม่ได้ดูตอนรันจริง)
- แก้: ใช้ margin "0px" หรือ amount: 0.1 และถ้า useReducedMotion() เป็น true ให้ set display = value ทันที
- Verifier: useInView ใช้ margin '-20%' ซึ่งเป็น rootMargin ลบ หด root 20% ทุกด้านจริง element ที่อยู่ในแถบล่าง 20% ของหน้าที่ไม่ scroll จะไม่ intersect และค้างที่ 0 ไม่มี MotionConfig/reduced-motion ในโปรเจกต์ แต่จะเกิดหรือไม่ขึ้นกับ layout ของหน้าที่ใช้ (kpi-rail, fleet-pulse-strip ฯลฯ) และขนาดจอ ซึ่งสังเกตแบบ runtime ไม่ได้

### L23. Signature POD ยอมให้ส่งลายเซ็นว่าง และ item จะถูก mark Delivered — CONFIRMED
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/pod-scan-dialog.tsx:137`
- ปัญหา: canSubmit เป็น true สำหรับ method === "Signature" เสมอ โดยไม่เช็กว่ามีการวาดลงบน canvas หรือไม่ collectReference จึงส่ง hash ของ canvas เปล่าออกไป ซึ่ง comment ที่บรรทัด 56-58 ระบุเองว่า RecordDropPod เก็บค่าที่ได้รับและ mark Delivered ทันที
- ผลกระทบ: POD ที่ไม่มีลายเซ็นจริงถูกบันทึกเป็นหลักฐานการส่งมอบ ทั้งที่ audit ระบุว่าเป็น Signature
- แก้: เก็บ flag hasInk (set true ใน pointer move handler) แล้วเพิ่มเงื่อนไข method === "Signature" && hasInk ใน canSubmit, reset flag เมื่อเปลี่ยน method หรือเปิด dialog ใหม่ และควรมีปุ่ม Clear ด้วย
- Verifier: canSubmit (pod-scan-dialog.tsx:135-138) เป็น true สำหรับ Signature โดยไม่เช็กว่ามีการวาด collectReference จึงส่ง hash ของ canvas เปล่าออกไป แต่ลดระดับเป็น LOW เพราะ method Confirm ก็ยอมให้บันทึก POD โดยไม่มี reference อยู่แล้วโดยเจตนา (ต้องมีแค่ scannedBy) ปัญหาจึงอยู่ที่ audit ระบุว่าเป็น Signature ทั้งที่ไม่มีลายเซ็น ไม่ได้เปิดช่องให้ข้ามการยืนยันที่ปกติบังคับไว้

### L24. ปุ่ม Undo ยังกดได้ 0.5 วินาทีหลังยิง DELETE ไปแล้ว และกดแล้วไม่มีผลใด ๆ — CONFIRMED
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/toast.tsx:47`
- ปัญหา: toast ที่มี action มีอายุ 6500ms แต่ timer ของการ cancel (orders-experience.tsx:649) ยิงที่ 6000ms เมื่อ flushCancel ลบ entry ออกจาก pendingCancelsRef แล้ว undoCancel จะ return ทันที ขณะที่ toast ยังเรียก dismiss() เหมือนกด undo สำเร็จ
- ผลกระทบ: ผู้ใช้ที่กด Undo ในช่วงท้ายจะเข้าใจว่ายกเลิกการ cancel สำเร็จ แต่ order ถูก cancel แล้ว และ robot อาจถูกสั่งหยุดผ่าน cascade
- แก้: ให้อายุ toast สั้นกว่า timer (เช่น 5500ms) หรือดีกว่านั้นคือให้ undoCancel คืนค่า boolean แล้ว toast แสดงข้อความ error 'สายเกินไป ยกเลิกแล้ว' เมื่อได้ false
- Verifier: toast ที่มี action มีอายุ 6500ms (toast.tsx:47) ส่วน timer ของ flushCancel ยิงที่ 6000ms (orders-experience.tsx:649) เมื่อ flushCancel ลบ entry แล้ว undoCancel (บรรทัด 464-466) จะ return เงียบ ขณะที่ปุ่มใน toast ยัง dismiss ตามปกติ ปัญหามีจริงแต่ช่วงเวลาแค่ประมาณ 0.5 วินาที และ row ไม่กลับมาในตาราง ผู้ใช้จึงยังพอสังเกตได้ว่า undo ไม่สำเร็จ ลดเป็น LOW

### L25. Saved filters ใช้ token ที่ไม่มีอยู่จริง ทำให้ menu โปร่งใสใน dark mode — CONFIRMED
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/saved-filters.tsx:119`
- ปัญหา: dark:bg-[var(--color-ink-950)]/95 และ text-[var(--color-coral-500)] (บรรทัด 157) อ้าง token ที่ไม่มีใน app/globals.css ค่าจึง invalid ตอน compute ทำให้ background กลายเป็น transparent และไอคอนลบไม่มีสี coral นอกจากนี้ปุ่มลบเป็น opacity-0 group-hover เท่านั้น และไม่มี aria-label
- ผลกระทบ: ใน dark mode dropdown จะโปร่งทับตาราง อ่านยาก บนจอสัมผัสและคีย์บอร์ดมองไม่เห็นปุ่มลบ
- แก้: เปลี่ยนเป็น dark:bg-[var(--color-popover)] และ text-[var(--color-coral)] และเพิ่ม focus-visible:opacity-100 กับ aria-label="Delete saved filter"
- Verifier: grep ใน globals.css ไม่พบ --color-ink-950 และ --color-coral-500 (มีแค่ ink-50 ถึง ink-900 และ --color-coral) dark:bg-[var(--color-ink-950)]/95 จึงเป็นค่า invalid และพื้น menu โปร่ง (ยังมี backdrop-blur ช่วยอยู่บ้าง) ไอคอนลบไม่มีสี และปุ่มลบเป็น opacity-0 group-hover ไม่มี aria-label จริง (มีแค่ title)

### L26. Clear filters ไม่ล้าง chip Failed trip/Active job และ empty state บอกผิด — CONFIRMED
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:841`
- ปัญหา: hasFilters และ onClearFilters ไม่นับและไม่ reset hasFailedTrip/hasActiveJob
- ผลกระทบ: ถ้าเปิดเฉพาะ chip 'Failed trip' แล้วไม่มีผลลัพธ์ จะเห็นข้อความ 'No orders yet — create your first delivery' และปุ่ม Clear filters จะไม่ปิด chip นั้น
- แก้: รวม hasFailedTrip || hasActiveJob ใน hasFilters และเรียก setHasFailedTrip(false) กับ setHasActiveJob(false) ใน onClearFilters
- Verifier: hasFilters (บรรทัด 841-845) นับแค่ priority, transportMode และ search ส่วน onClearFilters (บรรทัด 846-850) ก็ไม่ reset hasFailedTrip/hasActiveJob เมื่อ resolveEmptyStateVariant(search, hasFilters) ได้ false จะแสดง 'No orders yet' (orders-table.tsx:481)

### L27. Reopen/Redispatch ใช้ fetch ดิบ อ่าน error แค่ body.message — PLAUSIBLE
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/delivery-orders/orders-experience.tsx:1016`
- ปัญหา: เขียน fetch ซ้ำเองทั้งที่ lib/api/delivery-orders.ts มี reopenOrder/redispatchOrder ที่ใช้ unwrap (อ่านทั้ง message และ detail และจัดการ 429 ผ่าน throwIfRateLimited) อยู่แล้ว
- ผลกระทบ: ถ้า backend ตอบเป็น ProblemDetails ({detail}) หรือ 429 ผู้ใช้จะเห็นแค่ 'Reopen failed (409)' แทนเหตุผลจริง (PLAUSIBLE: ขึ้นกับรูปแบบ error body ของ endpoint นี้)
- แก้: เรียก reopenOrder(reopenTarget.id, {...}) และ redispatchOrder(...) แทน fetch ดิบ
- Verifier: โค้ดใช้ fetch ดิบซ้ำกับ reopenOrder/redispatchOrder ใน lib/api จริง แต่ backend ตอบ Results.BadRequest(result.Error) (DeliveryOrderEndpoints.cs:115) ซึ่งน่าจะมี key message ทางหลักจึงยังแสดงเหตุผลได้ ที่เสียจริงคือ response แบบ ProblemDetails จาก filter (idempotency/permission) และ 429 ที่ไม่ผ่าน throwIfRateLimited เป็นเรื่อง maintainability มากกว่า bug ที่ผู้ใช้เจอบ่อย

### L28. Item detail drawer มี race เมื่อคลิกหลายแถวเร็ว ๆ และกด Esc ปิดไม่ได้ — CONFIRMED
- กลุ่ม: delivery-orders · code
- ที่: `frontend/components/items/items-experience.tsx:328`
- ปัญหา: effect ที่เรียก getItem ไม่มี cancelled flag หรือ AbortController ถ้า response ของ item ก่อนหน้ามาช้ากว่า drawer จะแสดง item ผิดตัว อีกทั้ง drawer นี้ไม่มี Escape handler และไม่มี role="dialog"
- ผลกระทบ: เปิด item B แต่อาจเห็นข้อมูลของ item A และผู้ใช้คีย์บอร์ดปิด drawer ไม่ได้
- แก้: เพิ่ม let cancelled = false แล้วเช็กใน then/catch/finally พร้อม return () => { cancelled = true } และเพิ่ม keydown Escape listener กับ role="dialog" aria-modal
- Verifier: effect ที่บรรทัด 336-344 เรียก getItem(itemId).then(setDetail) โดยไม่มี cancelled flag ถ้า response ของ item ก่อนหน้ามาช้ากว่าจะทับข้อมูลของ item ปัจจุบัน grep ไม่พบทั้ง Escape และ role="dialog" ในไฟล์นี้

### L29. Cancel/Create dialog ไม่มี role=dialog และไม่มี focus trap หรือ focus restore — CONFIRMED
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/create-dialog.tsx:389`
- ปัญหา: panel ของ CreateOrderDialog และ CancelOrderDialog (cancel-dialog.tsx:63) ไม่มี role="dialog", aria-modal หรือ aria-labelledby และ dialog ทุกตัวใน slice ไม่ trap focus และไม่คืน focus เมื่อปิด ส่วนปุ่มลบ item (create-dialog.tsx:676) เป็น icon-only และไม่มี aria-label
- ผลกระทบ: screen reader ไม่รู้ว่าอยู่ใน modal และ Tab หลุดไปที่ตารางด้านหลังได้
- แก้: เพิ่ม role="dialog" aria-modal="true" aria-labelledby ชี้ไปที่ h2 และ aria-label="Remove item" ระยะยาวควรทำ primitive ModalPanel ที่ดูแล focus trap และ restore ร่วมกับ OverlayBackdrop
- Verifier: grep ไม่พบ role="dialog" หรือ aria-modal ใน create-dialog.tsx และ cancel-dialog.tsx panel เป็น motion.div เปล่า ปุ่มลบ item (create-dialog.tsx:676-687) มีแค่ไอคอน Trash2 ไม่มี aria-label และไม่มี focus trap หรือ focus restore

### L30. POD Barcode tab แนะนำให้ใช้กล้อง ขัดกับ rule ว่าสแกนผ่าน USB wedge เท่านั้น — CONFIRMED
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/pod-scan-dialog.tsx:241`
- ปัญหา: placeholder เขียนว่า 'scan with USB scanner / camera' และ hint ที่บรรทัด 253-254 บอกให้เปิดกล้องแล้ว copy มาวาง และ docstring ที่บรรทัด 15 เรียกว่า 'camera scan' ทั้งที่ไซต์ใช้ USB 2D wedge เท่านั้น
- ผลกระทบ: operator อาจไปพยายามใช้กล้องมือถือ ซึ่งไม่ใช่ flow ที่รองรับ
- แก้: ตัดข้อความเรื่องกล้องออก เหลือ 'สแกนด้วยเครื่องสแกน USB แล้วตรวจรหัสก่อนกด Confirm POD'
- Verifier: placeholder บรรทัด 241 เขียนว่า 'scan with USB scanner / camera' และ hint บรรทัด 253-254 บอกให้ใช้ 'open camera in your browser's default scanner app' ส่วน docstring บรรทัด 15 เรียกว่า 'camera scan' ซึ่งขัดกับ house rule ที่ว่าสแกนผ่าน USB 2D wedge เท่านั้น ไม่ใช้กล้อง

### L31. ช่อง Qty ลบให้ว่างไม่ได้ (เด้งกลับเป็น 1) — CONFIRMED
- กลุ่ม: delivery-orders · uiux
- ที่: `frontend/components/delivery-orders/create-dialog.tsx:752`
- ปัญหา: onChange ใช้ Number(e.target.value) || 1 พอผู้ใช้ลบเลขจนช่องว่าง ค่าจะกลับเป็น 1 ทันที
- ผลกระทบ: ถ้าจะพิมพ์ 25 ผู้ใช้ลบ 1 แล้วพิมพ์ 2 จะได้ 12 แทน ทำให้ใส่จำนวนผิดได้ง่าย
- แก้: เก็บ qty เป็น string ใน form state (แบบเดียวกับ weightKg) แล้ว validate qty > 0 ใน canAdvance และแปลงเป็น number ใน buildPayload
- Verifier: create-dialog.tsx:750-753 ใช้ Number(e.target.value) || 1 พอช่องว่าง Number('') ได้ 0 แล้วเด้งกลับเป็น 1 ทันที ถ้าลบหลักเดียวแล้วพิมพ์ใหม่จะได้ค่าต่อท้าย 1 เช่น 12 ถ้าเลือกทั้งหมดแล้วพิมพ์ทับยังใช้ได้ จึงเป็น LOW

### L32. Manual board ไม่มีสถานะ loading จึงขึ้นว่า 'No operators yet' ระหว่างโหลด — CONFIRMED
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/manual/operator-board-section.tsx:34`
- ปัญหา: ManualOperatorBoard ตั้งค่าเริ่มต้นของ operators/trips/overrides เป็น [] และไม่มี flag loading ทำให้ OperatorBoardSection แสดงข้อความ empty state 'No operators yet. Operators are created automatically...' และ OverrideQueueSection แสดง 'No pending override requests.' ตั้งแต่ render แรก จนกว่า 3 request จะกลับมาครบ
- ผลกระทบ: ระหว่างโหลดหรือเมื่อโหลดล้ม dispatcher จะเห็นว่าไม่มี override รออยู่ ซึ่งอาจไม่จริง แล้วอาจปิดหน้าไปก่อนข้อมูลจริงจะมา
- แก้: เพิ่ม state loading (true จนกว่า refreshAll สำเร็จครั้งแรก) แล้วแสดง TableSkeleton จาก primitives/data-table แทน empty state ช่วงนั้น
- Verifier: ManualOperatorBoard ตั้ง operators/trips/overrides เริ่มต้นเป็น [] และไม่มี state loading ส่วน OperatorBoardSection แสดง 'No operators yet...' ทันทีที่ operators.length===0 และ override queue ก็แสดง 'No pending override requests.' (บรรทัด 78) ตอนโหลดล้มยังพอมี error state ช่วยบอกอยู่บ้าง แต่ช่วงที่กำลังโหลดจะเห็น empty state ที่ไม่ตรงกับความจริง

### L33. ช่อง filter ในหน้า Trips ไม่มี label และปุ่มสลับทิศ sort มีแค่ไอคอน — CONFIRMED
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/trips-experience.tsx:423`
- ปัญหา: input ค้นหา (บรรทัด 423), vehicle key (บรรทัด 437) และ date สองช่อง (บรรทัด 454, 461) มีแค่ placeholder หรือ span ข้างๆ ไม่มี <label> หรือ aria-label ปุ่มสลับ sort (บรรทัด 485) มีแค่ไอคอนกับ title ไม่มี aria-label และ status chip ไม่มี aria-pressed
- ผลกระทบ: screen reader อ่านช่องวันที่สองช่องเหมือนกันว่า 'date' โดยไม่บอกว่าช่องไหนคือเริ่มหรือสิ้นสุด และไม่รู้ว่า chip ไหนกำลังถูกเลือก
- แก้: เพิ่ม aria-label ให้ทุกช่อง เช่น "Created from" และ "Created to" เพิ่ม aria-label={sortDir === 'asc' ? 'Sort ascending' : 'Sort descending'} และใส่ aria-pressed={statusFilter === f} ให้ chip
- Verifier: input ค้นหา, vehicle และ date ทั้งสองช่องไม่มี <label> หรือ aria-label มีแค่ placeholder และ span 'Created' ที่ไม่ได้ผูกกับ input ปุ่ม sort มีแค่ title ส่วน status chip ไม่มี aria-pressed (บรรทัด 403-412) และช่องวันที่ยังใส่ focus:outline-none ไว้ด้วย

### L34. RetryTriggerLine กับ NextRetryHint เป็นโค้ดที่ไม่มีใครใช้ ข้อมูลว่าใคร retry และทำไมจึงไม่ถูกแสดง — CONFIRMED
- กลุ่ม: dispatch · code
- ที่: `frontend/components/dispatch/retry-history-panel.tsx:205`
- ปัญหา: RetryTriggerLine ถูก export ไว้แต่ไม่มีใคร import (grep เจอแค่ในไฟล์นี้) ส่วน NextRetryHint (บรรทัด 195) return null เสมอ ทั้งที่ doc comment บรรทัด 15-17 บอกว่า panel แสดง 'the audit trigger that produced it (operator action, reason, timestamp)'
- ผลกระทบ: operator ไม่รู้ว่าแต่ละ attempt ถูก retry โดยใครและด้วยเหตุผลอะไร ถึง DTO จะส่ง retryTrigger มาให้แล้วก็ตาม คนอ่านโค้ดก็เข้าใจผิดว่าฟีเจอร์นี้ทำงานอยู่
- แก้: render <RetryTriggerLine attempt={attempt} /> ใน AttemptRow ใต้กล่อง attempt แล้วลบ NextRetryHint กับเงื่อนไข showConnector ที่ไม่มีผลออก
- Verifier: grep RetryTriggerLine เจอแค่ตำแหน่งที่ประกาศในไฟล์นี้ ไม่มีใคร render ส่วน NextRetryHint ก็ return null เสมอ ใน AttemptRow ไม่มีโค้ดที่แสดง retryTrigger เลย doc comment บรรทัด 15-17 ที่บอกว่าแสดง audit trigger จึงไม่ตรงกับโค้ด

### L35. Empty state ของ Trip items บอกให้ retry แต่ไม่มีปุ่ม retry — CONFIRMED
- กลุ่ม: dispatch · uiux
- ที่: `frontend/components/dispatch/trip-items-section.tsx:96`
- ปัญหา: TableEmptyState บอกว่า 'The vendor adapter may still be binding them — retry in a moment.' แต่มีปุ่ม Retry เฉพาะตอน error (บรรทัด 86) ใน empty state ไม่มีให้กด
- ผลกระทบ: ผู้ใช้ทำตามข้อความไม่ได้ ต้องปิดแล้วเปิด drawer ใหม่เพื่อโหลด items อีกครั้ง
- แก้: ส่ง action={{ label: 'Retry', onClick: retry }} ให้ TableEmptyState (primitive รองรับ action อยู่แล้วตาม table-empty-state.tsx:28)
- Verifier: TableEmptyState บอกให้ 'retry in a moment' แต่ไม่ได้ส่ง action มา ทั้งที่ primitive รองรับ action (table-empty-state.tsx:31,69) และฟังก์ชัน retry ก็มีอยู่ใน scope แล้ว ปุ่ม Retry มีให้กดเฉพาะตอน error เท่านั้น

### L36. Maintenance panel เรียก list attachments แยกหนึ่งครั้งต่อทุก episode — CONFIRMED
- กลุ่ม: fleet · code
- ที่: `frontend/components/fleet/carrier-maintenance-panel.tsx:144`
- ปัญหา: แต่ละ <li> ของ entries mount <AttachmentGallery> ของตัวเอง ซึ่ง useAttachments จะ listAttachments ทันทีตอน mount เปิด panel หนึ่งครั้งจึงยิง request เท่ากับจำนวนครั้งที่เคยซ่อม และจำนวนนี้โตขึ้นเรื่อยๆ ไม่มีเพดาน ซึ่งขัดกับหลักที่เขียนไว้ใน photos-dialog.tsx:28-29 ว่า "What the table must not do is load a gallery per row"
- ผลกระทบ: carrier ที่ซ่อมบ่อยจะยิง request ขนานกันหลายสิบตัวทุกครั้งที่เปิดประวัติ กินโควตา rate-limit 600/min ของผู้ใช้ และ panel โหลดช้า
- แก้: ให้ CarrierMaintenanceEntry ส่ง photoCount/coverAttachmentId มาเหมือน Carrier แล้วแสดง PhotoCell ต่อ entry และเปิด PhotosDialog (owner="maintenance") เมื่อกด หรืออย่างน้อยให้ lazy-mount gallery เมื่อผู้ใช้กดขยาย entry นั้น
- Verifier: carrier-maintenance-panel.tsx:144-151 mount AttachmentGallery หนึ่งตัวต่อ episode จริง และ use-attachments.ts:38-45 เรียก listAttachments ตอน mount จึงขัดกับหลักที่ photos-dialog.tsx:28-29 เขียนไว้เอง แต่ปกติ carrier หนึ่งตัวมีประวัติซ่อมไม่กี่ครั้ง และจะยิงเฉพาะตอนผู้ใช้เปิด panel เท่านั้น การบอกว่ากินโควตา 600/min หรือมี request หลายสิบตัวจึงเกินจริง ควรลดเป็น LOW (ประสิทธิภาพ/ความสม่ำเสมอ ไม่ใช่บั๊กที่ผู้ใช้เจอ)

### L37. CarrierTypeDeleteDialog ไม่ล้าง error เก่าเมื่อเปิดกับ type ใหม่ — CONFIRMED
- กลุ่ม: fleet · code
- ที่: `frontend/components/fleet/carrier-type-delete-dialog.tsx:31`
- ปัญหา: error จะถูกล้างเฉพาะตอนกด run เท่านั้น ไม่มี effect รีเซ็ตเมื่อ carrierType เปลี่ยน ต่างจาก dialog ตัวอื่นในโฟลเดอร์ที่รีเซ็ตใน useEffect([open, …]) ถ้าลบ X แล้วได้ 409 กด Cancel แล้วเปิดลบ Y ข้อความ error ของ X จะยังแสดงอยู่
- ผลกระทบ: ผู้ใช้เห็นเหตุผลที่ลบไม่ได้ของอีก type หนึ่ง และอาจเข้าใจผิดว่า Y ลบไม่ได้
- แก้: เพิ่ม `useEffect(() => { if (carrierType) { setError(null); setBusy(false); } }, [carrierType])` ให้เหมือน CarrierActionDialog
- Verifier: carrier-type-delete-dialog.tsx:30-31 มีแค่ useState และไม่มี effect รีเซ็ต ส่วน component ถูก render ตลอดเวลาใน carrier-types-experience.tsx:269 (เปิดปิดด้วย carrierType=null) state error จึงค้างข้ามการเปิดแต่ละครั้ง ถ้า X ได้ 409 แล้วกด Cancel และไปเปิดลบ Y จะเห็นข้อความของ X จริง

### L38. วันที่ใช้ toLocaleString() ตรงๆ แทน lib/datetime — CONFIRMED
- กลุ่ม: fleet · code
- ที่: `frontend/components/fleet/carriers-experience.tsx:324`
- ปัญหา: `new Date(c.lastSeenAt).toLocaleString()` ที่นี่ รวมถึง carrier-maintenance-panel.tsx:124,128 และ attachment-lightbox.tsx:303 จัดรูปแบบวันที่ตาม locale ของเบราว์เซอร์ ไม่ผ่าน formatDateTime ใน lib/datetime.ts ตาม house rule
- ผลกระทบ: รูปแบบวันที่ต่างจากหน้าอื่นของระบบ และต่างกันไปตามเครื่อง (เครื่อง locale ไทยจะแสดงปี พ.ศ.)
- แก้: เปลี่ยนเป็น formatDateTime(...) จาก @/lib/datetime หรือใช้ primitive จาก components/primitives/date-time.tsx
- Verifier: พบ new Date(...).toLocaleString() จริงที่ carriers-experience.tsx:324, carrier-maintenance-panel.tsx:124 และ 128 และ attachment-lightbox.tsx:303 ขณะที่ lib/datetime.ts มี formatDateTime อยู่แล้ว จึงขัดกับ house rule ใน review-frontend.md:48 ปัญหาเป็นเรื่องความสม่ำเสมอของรูปแบบวันที่ และเสี่ยง hydration mismatch เล็กน้อย

### L39. ปุ่ม Print/SVG/PNG บนการ์ด label แสดงเฉพาะตอน hover — CONFIRMED
- กลุ่ม: fleet · uiux
- ที่: `frontend/components/fleet/carrier-labels-experience.tsx:299`
- ปัญหา: กลุ่มปุ่มเป็น `opacity-0 group-hover:opacity-100` และจะแสดงเมื่อ focus-visible หรือ png ไม่อยู่สถานะ idle เท่านั้น บนแท็บเล็ตหรือจอสัมผัสไม่มี hover จึงมองไม่เห็นปุ่มจนกว่าจะแตะโดนพอดี
- ผลกระทบ: ผู้ใช้บนอุปกรณ์สัมผัสไม่รู้ว่าพิมพ์หรือดาวน์โหลด label ทีละใบได้
- แก้: เพิ่ม `[@media(hover:none)]:opacity-100` หรือแสดงปุ่มถาวรด้วยโทนจางแทน opacity-0
- Verifier: carrier-labels-experience.tsx:297-300 ใช้ `opacity-0 group-hover:opacity-100 has-[:focus-visible]:opacity-100` เมื่อ png === 'idle' จริง ซึ่งเป็น hover-only affordance ที่ house rule ใน review-frontend.md:86 ห้ามไว้ แต่หน้าพิมพ์ label น่าจะใช้บนเดสก์ท็อปเป็นหลัก และ mobile browser บางตัวจำลอง hover เมื่อแตะ ผลกระทบจึงจำกัด คงไว้ที่ LOW

### L40. คนที่ claim สำเร็จอาจเห็น toast 'ถูก claim ไปแล้ว' ผิดๆ — PLAUSIBLE
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/pool-trips-list.tsx:32`
- ปัญหา: เมื่อ claim สำเร็จ server จะ broadcast PoolTripClaimed ซึ่งไปลบแถวออกจาก reducer ทำให้ previewTrip กลายเป็น null ขณะที่ previewTripId ยังถูก set อยู่ (เส้นทางที่สำเร็จไม่ได้ clear) effect จึงยิง toast 'Trip กว่าจะกด confirm ก็ถูก claim ไปแล้ว' ระหว่างที่ router.push ยังไม่เสร็จ เรื่องลำดับเวลาเป็น PLAUSIBLE เพราะยังไม่ได้ทดสอบตอนรันจริง
- ผลกระทบ: operator ที่ได้งานแล้วเห็นข้อความว่าไม่ได้ จึงสับสนว่าตัวเองได้งานหรือไม่
- แก้: ใน handleConfirmClaim ให้ setPreviewTripId(null) ก่อน await หรือใช้ ref 'claimingTripId' เพื่อข้าม toast ใน effect เมื่อแถวที่หายไปคือแถวที่ตัวเองกำลัง claim
- Verifier: เส้นทางที่สำเร็จไม่ได้ clear previewTripId ถ้า PoolTripClaimed broadcast มาถึงก่อน router.push จะ unmount หน้า effect จะยิง toast ผิด แต่ขึ้นกับลำดับเวลาตอน runtime และเห็นแค่ชั่วครู่ก่อนเปลี่ยนหน้า จึงลดเป็น LOW

### L41. Drawer ยืนยันการ claim ทำ backdrop เอง ไม่รองรับ Esc และไม่ย้าย focus — CONFIRMED · ✅ แก้แล้ว (layer stack)
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/components/operator-pwa/pool-trips-list.tsx:196`
- ปัญหา: ConfirmClaimDrawer เขียน <button className="fixed inset-0 bg-black/60"> เองแทนการใช้ components/primitives/overlay-backdrop.tsx ไม่มี role="dialog"/aria-modal ไม่มี Esc ไม่ย้าย focus เข้า drawer และไม่ trap focus (ตอนนี้ไม่ได้อยู่ใน AnimatePresence จึงยังไม่เกิดปัญหา div ที่ค้างบังการคลิก)
- ผลกระทบ: ผิด house rule เรื่อง overlay ถ้ามีคนใส่ animation ทีหลังจะเกิดปัญหาตามที่บันทึกไว้ทันที ผู้ใช้ screen reader หรือคีย์บอร์ดภายนอกบนแท็บเล็ตใช้งานไม่ได้
- แก้: เปลี่ยนไปใช้ OverlayBackdrop และใส่ role="dialog" aria-modal aria-labelledby ให้ h2, autoFocus ที่ปุ่ม Cancel และปิดด้วย Esc
- Verifier: backdrop ทำเองแทนการใช้ OverlayBackdrop ผิด house rule จริง แต่ไม่ได้อยู่ใน AnimatePresence จึงยังไม่มีปัญหา div ค้างบังการคลิก (rule จัดเป็น HIGH เฉพาะกรณีนั้น) ส่วนที่ขาด role=dialog/Esc/focus มีผลน้อยบนแท็บเล็ตแบบสัมผัส จึงลดเป็น LOW

### L42. Handler ใน Settings ไม่มี try/finally ทำให้ busy ค้างเมื่อเกิด error — CONFIRMED
- กลุ่ม: operator-pwa · code
- ที่: `frontend/components/operator-pwa/settings-panel.tsx:76`
- ปัญหา: onDisablePush (76), onClearQueue (111), onLogout (120), onSyncNow (98) และ onEnablePush (subscribeToPush อาจ throw จาก pushManager.subscribe หรือ registerPushSubscription) ต่างก็ setBusy(false) หลัง await โดยไม่มี try/finally ตัวอย่างเช่น logout ตอน offline จะ throw
- ผลกระทบ: ทุกปุ่มในหน้า Settings ถูก disable ค้างจนกว่าจะ reload และไม่มีข้อความบอกสาเหตุ
- แก้: ห่อทุก handler ด้วย try/catch/finally แบบเดียวกับ onTestPush แล้ว setNote(err.message)
- Verifier: onDisablePush, onSyncNow, onClearQueue และ onLogout ไม่มี try/finally ใน subscribeToPush ตัว pushManager.subscribe กับ serviceWorker.ready ก็อยู่นอก try ถ้า throw ค่า busy จะค้างเป็น true และ logout ตอน offline จะ throw จริง

### L43. viewport maximumScale: 1 ทำให้ซูมไม่ได้ — CONFIRMED
- กลุ่ม: operator-pwa · uiux
- ที่: `frontend/app/m/layout.tsx:27`
- ปัญหา: maximumScale: 1 ปิด pinch-zoom บน Android Chrome
- ผลกระทบ: operator ที่สายตาไม่ดีหรือต้องอ่านรหัส mono ขนาด 11px (pool-trips-list.tsx:158) ขยายดูไม่ได้ ซึ่งขัดกับ WCAG 1.4.4
- แก้: เอา maximumScale ออก ป้องกันการ auto-zoom ของ input ได้อยู่แล้วเพราะ input ใช้ text-base (16px)
- Verifier: ใน app/m/layout.tsx viewport ตั้ง maximumScale: 1 จริง ซึ่งปิดการซูม แต่ผลกระทบต่ำ

### L44. HeroLiveMap กลืน error แล้วแสดง 'awaiting map' คู่กับป้าย live สีเขียวไปตลอด — CONFIRMED
- กลุ่ม: shell-pages · code
- ที่: `frontend/components/home/hero-live-map.tsx:36`
- ปัญหา: เมื่อ listMaps ล้มเหลว catch (บรรทัด 36-38) ทำแค่ setLoading(false) และเมื่อ getStations ล้มเหลว (บรรทัด 54-56) ก็ปล่อยผ่านไปเฉยๆ แล้ว CanvasCard (maps-experience.tsx:1177-1199) แสดงชื่อ '—' กับ 'awaiting map' แต่ยังมี StatusPulse สีเขียวพร้อมคำว่า 'live'
- ผลกระทบ: เมื่อ backend หรือ facility API ล่ม หน้า /home จะดูเหมือนกำลังรอข้อมูลอยู่ตามปกติแทนที่จะบอกว่ามีปัญหา operator ไม่รู้ว่าแผนที่โหลดไม่ขึ้น และไม่มีปุ่ม retry
- แก้: เพิ่ม error state ใน HeroLiveMap แสดงข้อความจาก lib/api/errors.ts พร้อมปุ่มลองใหม่ และให้ CanvasCard รับ prop error ไปซ่อนป้าย 'live' เมื่อไม่มี map
- Verifier: hero-live-map.tsx กลืน error ของทั้ง listMaps และ getStations จริง ส่วน CanvasCard ใน maps-experience.tsx แสดง '—' กับ 'awaiting map' ร่วมกับ StatusPulse และคำว่า 'live' ตลอดเวลา แต่ขอลดเป็น LOW เพราะเป็นแค่แผนที่ประกอบบนหน้า /home ไม่ได้ขวาง workflow และไม่ทำให้ข้อมูลเสีย

### L45. หน้า Login มี 'Remember me' และ 'Forgot password?' ที่ไม่ทำงาน — CONFIRMED
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/login/auth-panel.tsx:382`
- ปัญหา: CustomCheckbox (บรรทัด 644-686) เก็บ state ไว้ภายในเท่านั้น ไม่ได้เป็น field ใน form และไม่มีผลกับ session ส่วนลิงก์ 'Forgot password?' เป็น href="#" (บรรทัด 385) นอกจากนี้การคลิกที่ข้อความใน <label> ก็ไม่ได้ toggle span role=checkbox
- ผลกระทบ: ผู้ใช้ที่ลืมรหัส AD กดแล้วไม่มีอะไรเกิดขึ้น และเข้าใจผิดว่าระบบจะจำการ login ไว้ให้
- แก้: ลบ Remember me ออก แล้วชี้ Forgot password ไปที่ portal reset รหัส AD (กำหนด URL ผ่าน env) หรือแสดงข้อความให้ติดต่อ IT
- Verifier: CustomCheckbox ใช้แค่ useState ภายใน (role=checkbox) ไม่มี name จึงไม่ถูกส่งไปกับ form ส่วนลิงก์ 'Forgot password?' เป็น href="#" (auth-panel.tsx:385)

### L46. Landing มีลิงก์ placeholder และข้อความโฆษณาที่ไม่จริงจำนวนมาก — CONFIRMED
- กลุ่ม: shell-pages · uiux
- ที่: `frontend/components/landing/footer.tsx:264`
- ปัญหา: footer มีลิงก์ href="#" ราว 24 จุด (บรรทัด 242-270) มี 'Talk to sales' href="#" (cta-banner.tsx:174) และปุ่ม 'Watch the 90s demo' (hero.tsx:82) กับ 'Launch/Download' (platforms.tsx:126) ที่ไม่มี onClick นอกจากนี้ยังมีข้อความอ้าง เช่น 'Now live in 14 countries', '30-day free trial', '99.98% uptime' ซึ่งไม่ตรงกับระบบภายในที่ login ผ่าน LDAP
- ผลกระทบ: ผู้ใช้ภายในที่เข้า / จะเจอปุ่มที่กดแล้วไม่ทำอะไร และข้อความที่ไม่จริง ทำให้ระบบดูไม่น่าเชื่อถือ
- แก้: ตัด footer columns และปุ่มที่ไม่มีปลายทางออก เปลี่ยน CTA เป็น 'Sign in' อย่างเดียว และลบตัวเลขหรือข้อความอ้างที่ไม่จริง
- Verifier: เจอข้อความอ้างจริง ได้แก่ 'Now live in 14 countries' (hero.tsx:24), '30-day free trial' (cta-banner.tsx:40), '99.98% uptime' (footer.tsx:99) และ 'Watch the 90s demo' (hero.tsx:84) แต่จำนวน href="#" แบบ literal ใน footer มีแค่ 2 จุด ส่วนที่เหลือน่าจะมาจาก array ตัวเลข '~24' จึงอาจไม่แม่น แต่สาระของ finding ถูกต้อง

### L47. PermissionGuard อ้าง token สีที่ไม่มีอยู่จริง — CONFIRMED
- กลุ่ม: shell-pages · code
- ที่: `frontend/components/auth/permission-guard.tsx:37`
- ปัญหา: มีการใช้ bg-[var(--color-surface-1)] (บรรทัด 37) และ bg-[var(--color-surface-2)] (บรรทัด 44) แต่ globals.css ไม่ได้นิยาม --color-surface-1/-2 (มีแค่ --color-surface, -soft, -raised) ทำให้ background เป็นค่าที่ใช้ไม่ได้และกลายเป็นโปร่งใส
- ผลกระทบ: การ์ด 'Insufficient permissions' กับกล่อง code ไม่มีพื้นหลัง จึงจางหายไปกับ canvas โดยเฉพาะใน dark mode
- แก้: เปลี่ยนเป็น var(--color-surface) และ var(--color-surface-soft) หรือใช้ GlassCard
- Verifier: permission-guard.tsx ใช้ var(--color-surface-1) และ var(--color-surface-2) แต่ globals.css นิยามไว้แค่ --color-surface, -soft และ -raised ซึ่ง grep หา definition ของ --color-surface-1 ใน app แล้วไม่เจอ background จึงเป็นค่าที่ใช้ไม่ได้และกลายเป็นโปร่งใส

### L48. Component ใน components/home ที่ไม่มีใครใช้แล้ว 3 ไฟล์ — CONFIRMED
- กลุ่ม: shell-pages · code
- ที่: `frontend/components/home/home-hero.tsx:137`
- ปัญหา: ไม่มีไฟล์ไหน import HomeHero, FleetPulseStrip หรือ QuickActions (ตรวจด้วย grep ทั้ง repo) ใน home-hero.tsx ฟังก์ชัน useGreeting อ่าน window และ new Date() ระหว่าง render ซึ่งทำให้ hydration mismatch ระหว่าง SSR ('Welcome back') กับ client และ fleet-pulse-strip ยังเปิด robot poll กับ setInterval ไว้ด้วย
- ผลกระทบ: มีโค้ดตายราว 460 บรรทัด ถ้ามีคนนำกลับมาใช้จะได้ hydration warning และ poll ที่ไม่จำเป็นติดมาด้วย
- แก้: ลบทั้ง 3 ไฟล์ หรือถ้าจะนำกลับมาใช้ ให้ย้าย greeting ไปคำนวณใน useEffect หรือใช้ primitives/date-time
- Verifier: grep ทั้ง repo แล้วพบ HomeHero, FleetPulseStrip และ QuickActions เฉพาะในไฟล์ของตัวเอง (รวม 461 บรรทัด) useGreeting ใน home-hero.tsx:137-145 อ่าน window และ new Date() ระหว่าง render จริง และ fleet-pulse-strip ก็มี useRobotPositions กับ setInterval อยู่จริง เป็นโค้ดตาย

### L49. refetch ที่ไม่มี signal (จาก hub และหลัง mutation) อาจเอาข้อมูลเก่ามาทับ tab/หน้าใหม่ — CONFIRMED
- กลุ่ม: templates · code
- ที่: `frontend/components/planning/jobs-experience.tsx:133`
- ปัญหา: debounce ของ JobUpdated เรียก `fetch()` แบบไม่ส่ง signal (บรรทัด 133) onRetried ก็เช่นกัน (บรรทัด 188, 204) ถ้า request นี้ยังค้างอยู่แล้วผู้ใช้เปลี่ยน tab, sort หรือหน้า request ใหม่ที่มี signal อาจตอบกลับก่อน แล้ว request เก่าที่ไม่มี signal ค่อยตอบตามมาทับ data ด้วยรายการของ tab เดิม แบบเดียวกันนี้อยู่ใน order-templates-experience.tsx:161 (refresh → fetchList() ไม่มี signal) และ action-templates/templates-experience.tsx:194 นอกจากนี้สองไฟล์หลังยังเรียก setLoading(false) ใน finally แม้ request ถูก abort ไปแล้ว ทำให้ loading หายไปทั้งที่ request ใหม่ยังไม่เสร็จ
- ผลกระทบ: ระหว่างที่หุ่นทำงาน hub ยิง JobUpdated ถี่มาก tab 'Failed' อาจแสดง job ของ tab 'All' (มี Retry ปนกับ job ที่ไม่ควรมี) จนกว่าจะมี event ถัดไปมาแก้
- แก้: เก็บ AbortController ล่าสุดไว้ใน ref และให้ทุกเส้นทาง (effect, hub, หลัง mutation) abort ตัวก่อนหน้าก่อนเริ่มตัวใหม่ หรือใช้ request-id counter แล้ว set state เฉพาะเมื่อ id ตรงกับครั้งล่าสุด ใน template experiences ให้ข้าม setLoading(false) เมื่อ signal.aborted
- Verifier: กลไกมีอยู่จริง: ใน jobs-experience เส้นทาง hub debounce (บรรทัด 133) และ onRetried เรียก fetch() โดยไม่มี signal ทำให้ setData ทำงานทุกครั้ง (ส่วน getJobsQueue ไม่รับ signal อยู่แล้ว) ใน order-templates refresh เรียก fetchList() โดยไม่มี signal ส่วน fetchList ทั้งสองไฟล์ return ใน catch ตอน AbortError แต่ finally ยังเรียก setLoading(false) อยู่ดี อย่างไรก็ตาม race นี้ต้องเกิดในช่วงเวลาแคบมาก และจะแก้ตัวเองเมื่อมี event หรือ refetch รอบถัดไป จึงลดเป็น LOW

### L50. label ใน Station edit drawer ไม่ผูกกับ input และข้อความ error ไม่ได้ link ด้วย aria-describedby — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/facility/station-edit-drawer.tsx:471`
- ปัญหา: Field render `<label>` ที่ไม่มี htmlFor และไม่ได้ห่อ input (input อยู่ใน div ข้างๆ) ทำให้ช่อง Code, Reason และ Duration ไม่มี accessible name ข้อความ error `errors.reason`/`errors.duration` (บรรทัด 479-483) ก็ไม่ได้ผูกกับ input ด้วย aria-describedby และไม่มี aria-invalid
- ผลกระทบ: screen reader อ่านเป็นแค่ 'edit text' โดยไม่บอกว่าเป็นช่องอะไร คลิกที่ label ก็ไม่ focus ช่อง ตอน validation fail (เช่น ไม่ใส่ reason ตอน force offline) ผู้ใช้ที่ใช้เทคโนโลยีช่วยเหลือจะไม่รู้ว่าผิดตรงไหน
- แก้: ให้ Field รับ id (หรือใช้ useId) แล้วใส่ htmlFor บน label, id บน input/textarea, `aria-describedby` ชี้ไปที่ <p> ของ error/hint และใส่ `aria-invalid` เมื่อมี error
- Verifier: Field (station-edit-drawer.tsx:457-486) render `<label>` ที่ไม่มี htmlFor และไม่ได้ห่อ input ส่วน input/textarea ไม่มี id, aria-label, aria-describedby หรือ aria-invalid จริง แต่ช่องเหล่านี้มี placeholder ซึ่ง browser ใช้เป็น accessible name สำรองได้ ปัญหาจริงจึงเหลือเรื่องคลิก label แล้วไม่ focus และ error ไม่ได้ผูกกับช่อง ลดเป็น LOW

### L51. แผนที่ดัก wheel ทุกครั้ง ขัดกับคำอธิบาย 'Ctrl + Scroll' และ scroll หน้า /home ผ่านไม่ได้ — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/facility/maps-experience.tsx:1052`
- ปัญหา: wheel handler ของ CanvasCard เรียก e.preventDefault() แล้ว zoom ทุกครั้งที่มี wheel event ไม่ได้เช็ก e.ctrlKey แต่ popup Operation instructions (บรรทัด 1764-1769) บอกว่าให้ zoom ด้วย Ctrl + Scroll ส่วน hint chip (บรรทัด 1673) บอกว่าแค่ 'scroll' ซึ่งขัดกันเอง CanvasCard ถูกใช้บน /home (home/hero-live-map.tsx) แบบ flex-1 ขนาดใหญ่
- ผลกระทบ: ผู้ใช้ mouse ที่ scroll หน้า /home แล้ว cursor ไปอยู่บนแผนที่จะ zoom แผนที่แทนการเลื่อนหน้า ทำให้เลื่อนผ่านไปส่วนล่างไม่ได้ และคำแนะนำในแอปก็ไม่ตรงกับพฤติกรรมจริง
- แก้: เลือกให้เหลือแบบเดียว ถ้าจะทำตามคำอธิบาย ให้ preventDefault/zoom เฉพาะเมื่อ e.ctrlKey || e.metaKey (trackpad pinch ก็ส่ง ctrlKey มาอยู่แล้ว) แล้วแก้ hint chip ให้ตรง ถ้าตั้งใจให้ scroll zoom ได้เลย ให้แก้ข้อความใน popup แทน
- Verifier: wheel handler (maps-experience.tsx:1051) เรียก preventDefault และ zoom ทุกครั้งโดยไม่เช็ก ctrlKey ขณะที่ popup instructions (บรรทัด 1764-1769) บอกว่าให้ใช้ Ctrl + Scroll และ hint chip (1673) บอกว่า 'scroll' ซึ่งขัดกันจริง CanvasCard ถูกใช้บน /home ที่ความสูง lg:h-[680px] ใน home-overview แต่ไม่ได้กินเต็มความกว้าง (col-span-8 มีคอลัมน์ KPI อยู่ข้างๆ) ผู้ใช้จึงยังเลื่อนหน้าได้จากนอกแผนที่ ผลกระทบหลักคือข้อความไม่ตรงกับพฤติกรรม ลดเป็น LOW

### L52. ปุ่ม Retry job สั่ง dispatch trip ใหม่ทันทีโดยไม่ถามยืนยัน — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/planning/jobs-experience.tsx:384`
- ปัญหา: ปุ่ม Retry ใน row (บรรทัด 380-415) และใน drawer (บรรทัด 624-660) เรียก retryJob ทันทีที่คลิก การ retry คือการ dispatch trip ใหม่ไปให้หุ่น ตามที่ header บรรทัด 160 บอกไว้ ปุ่มอยู่ในแถวตาราง ซึ่งแถวนั้นเองก็คลิกได้ (เปิด drawer) ด้วย
- ผลกระทบ: กดพลาดตอนจะเปิด drawer หรือกดตอนที่อีกคนเพิ่ง retry ไปแล้ว จะส่งหุ่นออกไปโดยไม่ตั้งใจ checklist ระบุว่า action แบบ resend ต้องยืนยันและบอกว่าจะเกิดอะไรขึ้น
- แก้: เพิ่ม confirm dialog สั้นๆ (ใช้ pattern เดียวกับ redispatch-dialog.tsx) ที่บอกว่า 'จะสร้าง Trip ใหม่และส่งไป RIOT3 บน job นี้' หรืออย่างน้อยให้ปุ่มใน row ต้องกดสองครั้งเพื่อยืนยัน
- Verifier: ปุ่ม Retry เรียก retryJob ทันทีโดยไม่ถามยืนยันจริง แต่ข้อกังวลที่ว่าจะกดพลาดตอนจะเปิด drawer ไม่ถูก เพราะ onClick มี e.stopPropagation() และคลิกที่แถวไม่ได้สั่ง retry ปุ่มแสดงเฉพาะ job ที่ retriable (Failed) และการ retry job ที่ fail ก็คือ action หลักที่ตั้งใจให้ทำในหน้านี้ ส่วนกรณีที่อีกคน retry ไปแล้ว job จะไม่อยู่สถานะ Failed อีก ขัด checklist ข้อ 'resend ต้อง confirm' อยู่บ้าง จึงให้เป็น LOW

### L53. Toast ที่เขียนเองใน slice ไม่มี role="status"/aria-live — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/order-templates/order-templates-experience.tsx:418`
- ปัญหา: toast ใน order-templates-experience.tsx:418, action-templates/templates-experience.tsx:471 และ facility/maps-experience.tsx:2205 เป็น motion.div เปล่าๆ ทั้งที่ components/delivery-orders/toast.tsx (ToastProvider/useToast ซึ่ง jobs-experience ใช้อยู่) ใส่ role="status" aria-live="polite" ไว้แล้ว
- ผลกระทบ: screen reader ไม่อ่านผลอย่าง 'Template deactivated', 'Delete failed' หรือ 'Synced N stations' ทำให้ผู้ใช้ไม่รู้ว่า action สำเร็จหรือไม่ และมี toast ซ้ำกัน 3 แบบที่ต้องดูแลแยกกัน
- แก้: เปลี่ยนไปใช้ ToastProvider/useToast จาก components/delivery-orders/toast.tsx หรืออย่างน้อยใส่ role="status" aria-live="polite" บน container
- Verifier: toast ใน order-templates-experience.tsx:418, templates-experience.tsx:471 และ maps-experience.tsx:2205 เป็น motion.div ที่ไม่มี role/aria-live (grep ไม่เจอ) ขณะที่ delivery-orders/toast.tsx:63-64 ใส่ role="status" aria-live="polite" ไว้แล้ว

### L54. order-templates-table เขียน IdCell ซ้ำกับ primitive ที่มีอยู่แล้ว — CONFIRMED
- กลุ่ม: templates · code
- ที่: `frontend/components/order-templates/order-templates-table.tsx:309`
- ปัญหา: IdCell ในไฟล์นี้เป็นตัวที่เขียนขึ้นเอง ใช้ setTimeout ที่ไม่ clear เมื่อ unmount และ aria-label ไม่บอก id ขณะที่ components/primitives/id-cell.tsx ทำงานแบบเดียวกัน (stopPropagation, copyText fallback, timer มี cleanup, focus ring) และใช้กันใน table อื่นอยู่แล้ว
- ผลกระทบ: ID cell หน้าตาและพฤติกรรมไม่เหมือนตารางอื่น ถ้าแก้ primitive ภายหลังตรงนี้ก็จะไม่ได้แก้ตาม
- แก้: เปลี่ยนเป็น `<IdCell id={t.id} label="template id" />` จาก @/components/primitives/id-cell แล้วลบตัว local ทิ้ง
- Verifier: order-templates-table.tsx:309 มี IdCell ที่เขียนเองซ้ำกับ components/primitives/id-cell.tsx ใช้ setTimeout ที่ไม่ clear และ aria-label 'Copy full ID' ไม่บอกว่าเป็น id ของอะไร ขณะที่ primitive รับ label ได้และมี cleanup ครบ

### L55. หน้า /facility/maps แสดง 'Topology live' ด้วยจุด pulse ทั้งที่ข้อมูลไม่ได้อัปเดตแบบ realtime — CONFIRMED
- กลุ่ม: templates · uiux
- ที่: `frontend/components/facility/maps-experience.tsx:450`
- ปัญหา: HeaderStrip แสดง StatusPulse tone success พร้อมข้อความ 'Topology live · v4.maps' ตลอดเวลา แต่ MapsExperience โหลด stations ครั้งเดียวต่อ map และโหลดใหม่หลังกด sync/edit เท่านั้น ไม่มี poll หรือ hub (canvas live ถูกย้ายไป /home แล้วตามคอมเมนต์บรรทัด 383-386)
- ผลกระทบ: operator อาจเข้าใจว่าสถานะ station (เช่น force-offline ที่คนอื่นตั้งไว้) อัปเดตเอง ทั้งที่ต้องรีเฟรช ความหมายของสัญลักษณ์ 'live' ก็ไม่ตรงกับหน้าอื่น
- แก้: เอา chip นี้ออก หรือเปลี่ยนเป็นข้อความ 'last sync …' ที่ไม่มี pulse ใช้ pulse/live เฉพาะกับข้อมูลที่ poll หรือมาทาง hub จริงๆ
- Verifier: HeaderStrip แสดง StatusPulse tone success พร้อม 'Topology live · v4.maps' แบบ hard-code สถานะ station โหลดแค่ตอนเลือก map และหลัง sync/edit ใน maps-experience มี setInterval แค่ตัวที่ re-render label เวลา (บรรทัด 198) กับตัวนับ cooldown ไม่มี poll หรือ hub ของ station คำอธิบาย 'Live station topology' จึงทำให้เข้าใจผิดได้

### L56. liveTimelineEntry ของ job ก่อนหน้าค้างไปโผล่ใน timeline ของ job ถัดไป — CONFIRMED
- กลุ่ม: templates · code
- ที่: `frontend/components/planning/jobs-experience.tsx:482`
- ปัญหา: liveTimelineEntry ไม่ถูก reset ตอน jobId เปลี่ยนหรือตอนปิด drawer พอเปิด job ใหม่ StatusTimelineSection ถูก mount ใหม่ และ effect ของ liveEntry (status-timeline-section.tsx:60-75) จะใส่ entry ของ job เก่าลงไปทันที จนกว่า fetch ของ job ใหม่จะเสร็จแล้วมาทับ
- ผลกระทบ: timeline ของ job ใหม่แสดง event ของ job อื่นแวบหนึ่ง และถ้า fetch history fail entry ผิดตัวนั้นจะค้างอยู่พร้อมข้อความ error
- แก้: ใน effect ที่ขึ้นกับ [jobId] ให้เรียก setLiveTimelineEntry(null) หรือส่ง liveEntry ลงไปเฉพาะเมื่อ entry.entityId ตรงกับ data.id
- Verifier: JobDetailDrawer mount ค้างไว้ตลอด และไม่มีที่ไหน reset liveTimelineEntry (มี setter แค่ใน TimelineUpdated) พอปิด drawer data จะเป็น null ทำให้ StatusTimelineSection unmount ไปด้วย เมื่อเปิด job ใหม่ section จะ mount ใหม่ แล้ว effect liveEntry (status-timeline-section.tsx:59-75) ใส่ entry เก่าเข้าไปทันทีจนกว่า fetcher จะ setData ทับ ถ้า fetch fail ตัว data จะยังเป็น entry เก่าค้างอยู่

## สิ่งที่ทำได้ดี

- [core] OverlayBackdrop (components/primitives/overlay-backdrop.tsx) mount ค้างไว้ตลอดแล้วคุม pointer-events ด้วย state ทำให้ exit ค้างแล้วเหลือแค่สีจางๆ ไม่มีทางกลายเป็นตัวบล็อกคลิก ควรใช้ pattern นี้กับ overlay ทุกตัว (rail drawer ยังไม่ได้ใช้)
- [core] proxy.ts ใช้ matcher เป็น allowlist แบบ literal คู่กับ scripts/check-route-protection.mjs ที่ fail ตอน prebuild ถ้ามี route group ใหม่ที่ไม่ถูกป้องกัน และมี routePath() จัดการ .rsc/.segments รวมถึงตัด _rsc ออกจาก ?from= ซึ่งเป็นตัวอย่าง guard ที่ดี
- [core] usePollSchedule/useHubSubscription เก็บ callback ไว้ใน ref (tickRef/handlersRef) ทำให้ caller ส่ง inline closure ได้โดยไม่ resubscribe และไม่เจอ stale closure รวมถึงเคารพ Retry-After จาก RateLimitError
- [delivery-orders] overlay ทุกตัวใน slice ใช้ OverlayBackdrop แยกจาก AnimatePresence ส่วน cancel/create dialog ใช้ wrapper pointer-events-none ตามด้วย panel pointer-events-auto ทำให้ไม่มีทางเกิด click-blocker ค้างอยู่บนหน้า
- [delivery-orders] pod-scan-dialog ใช้ useWedgeScanner เฉพาะ tab Barcode ช่องชื่อที่พิมพ์ภาษาไทยยังพิมพ์ได้ปกติ และ scan ไม่ auto-commit ต้องกดยืนยันเอง ตรงตาม house rule
- [delivery-orders] fetchOrders ยกเลิก request เก่าด้วย AbortController (fetchAbortRef) และ poll ผ่าน usePollSchedule ที่ back-off เมื่อ API ตอบ 429 ส่วน SignalR hint ถูก debounce ก่อนเข้า schedule
- [dispatch] trips-experience.tsx:251-268 ส่ง SignalR hint ผ่าน debounce 500ms ไปที่ usePollSchedule.trigger() แทนการ fetch ตรงๆ ทำให้ hub burst ไม่ทะลุ back-off หรือ rate limit และ refetch แทนการ merge delta เพื่อให้ filter/sort ฝั่ง server เป็นตัวตัดสิน
- [dispatch] overlay ทุกตัวใน trip drawer (drawer, PassRobotDialog, RaiseExceptionDialog, POD Lightbox) ใช้ OverlayBackdrop แยกเป็น sibling ก่อน AnimatePresence และให้ wrapper เป็น pointer-events-none ตาม house rule
- [dispatch] trip-detail-drawer.tsx:213-228 เก็บ live MissionUpdated แยกจาก data ที่ fetch มา แล้วค่อย merge ตอน render โดย dedup ด้วย (missionKey,state) ตรงกับ unique index ของ backend และคำนวณ failingMissions ครั้งเดียวแล้วส่งให้ banner, chip และปุ่ม Resume ใช้ร่วมกัน
- [operator-pwa] offline-queue.ts isPermanentFailure: คง 429/408 ไว้ในคิวแทนการทิ้ง และเขียนคอมเมนต์บอกให้แก้คู่กับ sw.js ให้ตรงกันเสมอ (ควรใช้ pattern นี้ต่อไปกับ 401 ด้วย)
- [operator-pwa] pod-capture.tsx แยก phase เป็น preparing/uploading เพื่อไม่ให้ผู้ใช้เข้าใจผิดว่าเน็ตช้าระหว่างที่บีบอัดรูป และ uploadPodBytes แปลง status ของ MinIO เป็นข้อความที่ operator เอาไปทำต่อได้
- [operator-pwa] geolocation.ts tryGetCurrentPosition: ไม่ throw แต่คืนเหตุผลกลับมา แล้วให้ server เป็นผู้ตัดสินว่าต้องใช้พิกัดหรือไม่ (explainLegFailure จะใช้เหตุผลนั้นเมื่อ server ตอบ GEOFENCE_REQUIRED)
- [fleet] carriers-experience.tsx refresh(): ใช้ AbortController ตัวเดียวคุม request ล่าสุด และเช็ก ac.signal.aborted ทั้งใน then/catch/finally ทำให้ทั้ง filter change และ reload หลัง save ไม่มีทางทับกัน เป็นแบบอย่างที่ดีสำหรับหน้า list อื่น
- [fleet] onItemsChanged + ref ใน useAttachments: patch cover/count เฉพาะแถวในตารางแทนการ refetch ทั้งหน้า และส่งเฉพาะตอนสำเร็จ ทำให้ load ที่ล้มเหลวไม่ทำให้ตารางเข้าใจว่าไม่มีรูป
- [fleet] CarrierScanDialog: ทำตามกฎ wedge-scanner ครบ คือเป็น modal, ใช้ useWedgeScanner, validate pattern ฝั่ง client, resolve จาก rows ในหน้าก่อนยิง network, กันการสแกนซ้ำในช่วง REPEAT_WINDOW และไม่ auto-commit (ต้องกด "Use this carrier")
- [admin] iam-system-detail-experience.tsx:122-135 และ :1622-1630 — effect ที่ fetch จะสร้าง AbortController ทุกครั้ง แล้วเช็ก `abort.signal.aborted` ก่อน setError เพื่อไม่ให้ error หลัง unmount โผล่ขึ้นมา ควรใช้เป็นแบบอย่าง
- [admin] replay-dialog.tsx — ใช้ OverlayBackdrop + AnimatePresence เฉพาะ panel, role=dialog/aria-modal, ปิด Esc และปิด backdrop ระหว่าง submitting และ confirm 2 ขั้น (form → confirm) สำหรับ action ที่ทำลายข้อมูล
- [admin] ConfigureTokenRefreshModal (iam-system-detail-experience.tsx:1491-1524) — บันทึกแล้ว verify ทันที และแสดง verdict ตาม `status` ไม่ใช่ตาม message พร้อมซ่อน Cancel เมื่อบันทึกไปแล้วเพื่อไม่ให้สื่อว่า undo ได้ ส่วน OneTimeSecretBanner กันการ rotate ซ้ำขณะยังมี secret ค้างบนจอ
- [analytics] debounce ref + clearTimeout ตอน unmount รอบ useDashboardSubscription (kpi-rail.tsx:59-72): รวม hub push ที่มาเป็นชุดให้เหลือ refetch ครั้งเดียว และไม่ทิ้ง timer ค้างไว้
- [analytics] ChartMount (components/primitives/chart-mount.tsx) ห่อ recharts ไว้ไม่ให้ mount ตอน parent ยังมีขนาดเป็นศูนย์ ใช้ครบทุก report
- [analytics] order-status-chart.tsx:77-96 ใช้ token กับแกน tick และ tooltip (`var(--color-ink-500)`, `var(--color-surface)`) และปิด animation ของ series ซึ่งควรใช้เป็นแบบให้กราฟใน reports
- [shell-pages] auth-panel.tsx:83-92 ผ่านค่า ?from= ด้วย sanitizeReturnPath ก่อน router.push เพื่อกัน open redirect และมี bounceTimer เป็นตัวสำรองเมื่อ session cookie ไม่ติด ปุ่มจึงไม่ค้างที่สถานะ success
- [shell-pages] auth-provider.tsx ใช้ initialClaims จาก server เป็นค่าเริ่มต้นของ state (ไม่มีจังหวะที่หน้าแสดงเป็นยังไม่ login) แล้วค่อยเติมข้อมูลจาก localStorage ใน useEffect หลัง mount ทำให้ไม่มี hydration mismatch และแยก permissions null (กำลังโหลด) ออกจาก [] (ไม่มีสิทธิ์) ชัดเจน
- [shell-pages] profile-tabs.tsx:35-50,96-98 ใส่ ARIA ของ tabs ครบ คือ role=tablist/tab/tabpanel, aria-selected, aria-controls และ aria-labelledby ควรใช้เป็นแบบอย่างให้ tab strip อื่น เช่นแท็บ Sign in/Create account ใน auth-panel ที่ยังไม่มี role
- [templates] create-from-template-dialog.tsx:90-214 เก็บ idempotency key ไว้ใน ref เฉพาะช่วงที่ request ยังไม่จบ ถ้าเจอ DispatchInProgressError ก็ใช้ key เดิม ถ้าถูกปฏิเสธก็ทิ้ง key แล้วยกเลิก request ที่ค้างตอนปิด dialog ควรใช้เป็นแบบอย่างของปุ่มที่สั่งหุ่นทุกตัว
- [templates] overlay ทุกตัวใน slice ใช้ OverlayBackdrop เป็น sibling ที่อยู่นอก AnimatePresence และห่อ panel ด้วย wrapper ที่เป็น pointer-events-none แล้วให้ panel เปิด pointer-events เอง จึงไม่มี backdrop ค้างมาบังการคลิก
- [templates] useRobotPositions ใช้ usePollSchedule ที่ abort ทุก tick และข้ามตอน tab ซ่อนอยู่ ส่วน RobotMarker เขียน SVG transform ตรงจาก spring ผ่าน .on('change') เลยไม่ต้อง re-render React ทุกเฟรม
- [api-layer] lib/api/proxy-helpers.ts: proxy กลางที่ normalize error (message/Error/error/detail), ซ่อน 5xx string body, ส่งต่อ traceId และ Retry-After, และ rewrite storage URL ภายในให้อัตโนมัติทุก response — route ใหม่ไม่ต้องจำ
- [api-layer] lib/api/attachment-image-relay.ts + attachment-owners.ts: validate GUID/owner ก่อนต่อ path, ตาม redirect เฉพาะเข้า STORAGE_INTERNAL_ORIGIN/ เท่านั้น, cache immutable เฉพาะ 200 ที่เป็น image จริง และทุก failure เป็น no-store
- [api-layer] lib/api/trips.ts api() helper: เรียก throwIfRateLimited, อ่าน message จาก server, รองรับ 204/empty body — ควรยกเป็น helper กลางให้ไฟล์อื่นใช้แทน unwrap/readError ~12 ชุดที่แยกกันอยู่

## ไม่ได้ตรวจ

- [core] ไม่ได้รัน npm run typecheck (task นี้สั่งให้ read-only เคร่งครัด)
- [core] ผลจริงของการเปิด/ปิด rail drawer เร็วๆ, dark mode ของ WmsLocationCombobox และ NumberTicker ที่ขอบล่าง viewport ต้องดูตอนรันจริง
- [core] พฤติกรรมของ Background Sync และ 401 ตอน JWT หมดอายุระหว่าง offline บนอุปกรณ์จริงยังไม่ได้ทดสอบ (สรุปจาก code path)
- [core] app/globals.css อ่านเฉพาะ token ของ light/dark และ body ไม่ได้ตรวจ contrast ของทุก utility
- [core] lib/realtime/hubs/* อ่านแค่ trip-hub.ts เต็มไฟล์ ไฟล์ hub อื่นเป็น wrapper บาง pattern เดียวกัน
- [core] ไม่ได้ตรวจ components/auth/auth-provider.tsx (permissions/logout) ที่ shell import เข้ามา เพราะอยู่นอก slice
- [core] ไม่ได้ตรวจว่า backend เช็ก permission ของหน้า admin ที่ rail ไม่ได้ gate ไว้ (Permissions, Audit log, Projection health, System status)
- [delivery-orders] ไม่ได้รัน typecheck หรือ build (คำสั่งงานกำหนดให้อ่านอย่างเดียว) จึงยืนยันไม่ได้ว่า useSearchParams ใน OrdersExperience ต้องมี Suspense boundary ใน Next 16 หรือไม่ หน้า app/delivery-orders/list/page.tsx ไม่มี Suspense ครอบ
- [delivery-orders] backdrop ทุกตัวในทั้ง codebase ใช้ bg-[var(--color-ink-900)]/50 ซึ่งใน dark mode ink-900 คือ #f4f7fe จึงกลายเป็นม่านสีขาว ต้องดูของจริงว่าเป็น design ที่ตั้งใจหรือไม่ (เป็นปัญหาข้าม slice)
- [delivery-orders] ไม่ได้ตรวจว่า API proxy ของ /api/delivery-orders/* (reopen, redispatch, abandon, pod-scan) เช็ก permission ฝั่ง server ด้วยหรือไม่ เพราะ slice นี้ไม่ได้ gate ปุ่มด้วย permissions.ts เลย ปุ่มทุกปุ่มแสดงตามสถานะ order อย่างเดียว
- [delivery-orders] ช่องชื่อผู้ทำ (scannedBy/heldBy/reopenedBy) เป็น free text โดยส่ง currentUser={null} เสมอ ไม่ได้ตรวจว่า backend stamp actor จาก JWT ทับค่านี้หรือใช้ค่าที่พิมพ์ตรง ๆ
- [delivery-orders] confirmItemPod (lib/api, นอก slice) โยน error เป็น res.text() ดิบ ข้อความใน POD dialog อาจเป็น JSON ทั้งก้อน ต้องดูตอนรันจริง
- [delivery-orders] หน้า order-templates/action-templates/jobs ใต้ app/delivery-orders เป็น wrapper บาง ๆ ที่ render component จาก slice อื่น จึงไม่ได้ตรวจ component ปลายทาง
- [delivery-orders] contrast จริงของ glass-strong และ bg-white/X บนพื้นหลังภาพหรือ blur ต้องดูด้วยตาทั้งสองธีม
- [dispatch] ตรวจไม่ได้ว่า dialog และ lightbox ที่เป็น position:fixed และ render อยู่ใน motion.aside ของ drawer (z-[70], overflow-hidden) จะถูก clip อยู่ในกรอบ drawer หรือไม่ ขึ้นกับว่า motion 12 ทิ้ง transform ไว้บน aside หลัง animate x:0 หรือเปล่า ต้องดูตอนรันจริง
- [dispatch] ไม่ได้รัน npm run typecheck เพราะ task กำหนดให้ read-only ห้ามรัน npm
- [dispatch] ยังไม่ได้ยืนยันว่า backend ตรวจสิทธิ์ cancel/retry/pause/pass/reassign/approve จริง ฝั่ง frontend ไม่ได้ซ่อนปุ่มตามสิทธิ์ (Permissions.Dispatch มีแค่ TripRead) และไม่ได้อ่าน lib/api/trips.ts กับ admin-manual.ts ว่า error message ที่ได้มาจาก server ผ่าน lib/api/errors.ts หรือไม่
- [dispatch] ไม่ได้อ่าน OmsNotificationSection, StatusTimelineSection, Pagination, IdCell และ lib/mission-order เพราะอยู่นอก slice
- [dispatch] ผลจริงตอน SignalR หลุดแล้วต่อใหม่ และพฤติกรรม Esc หรือ focus เมื่อมี Order drawer ซ้อนบน Trip drawer (onOpenOrder) ต้องทดสอบตอนรันจริง
- [dispatch] contrast ใน dark mode ของ --color-ink-300/400 บน surface ประเมินจากการอ่านโค้ดอย่างเดียว ยังไม่ได้วัดค่าจริง
- [operator-pwa] ไม่ได้รัน npm run typecheck (task สั่งให้อ่านอย่างเดียว ห้ามรัน npm)
- [operator-pwa] พฤติกรรมจริงของ SW, Background Sync และ IDB บน iOS Safari และแท็บเล็ต Android ผ่าน HTTP LAN ไม่ได้ทดสอบ รวมถึงลำดับเวลาระหว่าง PoolTripClaimed กับ 204 ของคนที่ claim
- [operator-pwa] backend ตอบ 401 หรือ 403 จริงเมื่อ cookie หมดอายุบน /api/operator/* หรือไม่ (อนุมานจาก proxy-helpers ที่ไม่แนบ Bearer เมื่อไม่มี token)
- [operator-pwa] contrast จริงของ zinc-500 บน zinc-950/zinc-900 และการใช้สี zinc/cyan ดิบแทน token ใน globals.css (PWA ตั้งใจใช้ธีมมืดแยกต่างหาก จึงไม่ได้นับเป็น finding)
- [operator-pwa] sw.js ไม่มี fetch handler หรือ cache เลย จึงเปิด PWA ตอน offline ไม่ได้ (คอมเมนต์ระบุว่ายังไม่ทำ) ส่วน start_url '/m' ก็อยู่นอก SW scope '/m/'
- [operator-pwa] public/sw.js อยู่นอก slice อ่านเฉพาะส่วนที่เกี่ยวกับคิว
- [fleet] ไม่ได้รัน npm run typecheck เพราะ task สั่งให้ read-only ห้ามรัน npm
- [fleet] ข้อ HIGH เรื่อง backdrop-filter สร้าง containing block ให้ lightbox ยังต้องเปิดดูจริงใน Edge/Chrome เพื่อยืนยันขนาดและตำแหน่งที่ถูกตัด
- [fleet] หน้าตาตอนพิมพ์ (print CSS ของ carrier-label-sheet/carrier-label-chrome/data-print-solo) และพฤติกรรม afterprint ของแต่ละเบราว์เซอร์
- [fleet] route ใน app/api/fleet/** กับ lib/api/fleet-attachments.ts ส่วน upload (presign/confirm) ไม่ได้อ่านละเอียดว่าตรวจสิทธิ์ CarrierWrite/CarrierMaintain ฝั่ง server ตรงกับการซ่อนปุ่มใน UI หรือไม่
- [fleet] contrast ของ ink-400 บน glass ในโหมด dark และการทำงานของ beep() ภายใต้ autoplay policy บนเครื่องจริง
- [admin] lib/iam/** ว่างเปล่า (ไม่มีไฟล์) จึงไม่มีอะไรให้ตรวจ
- [admin] ไม่ได้รัน typecheck หรือดูหน้าจริงใน dark mode ค่า contrast คำนวณจากค่า token ใน globals.css
- [admin] race ของ permission toggle (finding PLAUSIBLE) ขึ้นกับ timing ของ getSystem ตอน runtime ยังไม่ได้ทดสอบจริง
- [admin] lib/api/iam-systems.ts, admin-projections.ts และ app/api/admin/** proxy routes อยู่นอก slice ดูเฉพาะส่วนที่ต้องใช้ตัดสิน finding
- [admin] พฤติกรรม SignalR reconnect ของ useDashboardSubscription และ vendorHealthStore (lib/realtime) ไม่ได้ตรวจ
- [analytics] ยังไม่ได้รันจริงเพื่อดู contrast ของกราฟ recharts ใน dark mode และ tooltip default จึงยังเป็น PLAUSIBLE
- [analytics] ไม่ได้ตรวจ app/api/reports/* และ app/api/dashboard/* (route proxy) ว่าเช็ค permission ครบไหม เพราะอยู่นอก slice
- [analytics] ไม่ได้ตรวจว่า backend รับช่วง 90d ของ reports ได้จริงหรือมี cap ไว้
- [analytics] ไม่ได้รัน npm run typecheck (slice นี้สั่งให้อ่านอย่างเดียว)
- [analytics] ไม่ได้ตรวจว่า SLA confirm breach กับ complete breach ซ้อนกันจริงในข้อมูลหรือไม่ แต่ไม่ว่าจะซ้อนหรือไม่ กราฟก็ผิดอยู่ดี
- [shell-pages] ยังไม่ได้ repro การค้างของ backdrop ใน landing top-nav บนอุปกรณ์จริง ประเมินจาก pattern ที่ overlay-backdrop.tsx บันทึกไว้
- [shell-pages] ไม่ได้รัน npm run typecheck เพราะ task นี้กำหนดให้อ่านอย่างเดียว ห้าม run npm
- [shell-pages] ยังไม่ได้วัด contrast จริง เช่น ink-400 บน glass หรือ pastel และไม่ได้ดู dark mode ของ hero gradient ใน profile ด้วยตาจริง
- [shell-pages] ไฟล์ตกแต่งของ landing (bento-features, faux-3d-visual, testimonials, brand-scene) อ่านแบบ skim หา hook, link และ key เท่านั้น ไม่ได้ตรวจ layout ทุกบรรทัด
- [shell-pages] app/api/profile/route.ts, app/api/auth/logout และ components/shell (TopNav/LeftRail) อยู่นอก slice อ่านเฉพาะส่วนที่ต้องใช้ตัดสิน finding
- [shell-pages] AuthPanel ถูก mount สองชุด (desktop และ mobile ที่ซ่อนด้วย CSS) ใน login-experience.tsx ผลต่อ password manager หรือ autofill ต้องทดสอบตอนรันจริง
- [shell-pages] LandingIntro บังหน้า / ราว 2.3 วินาทีทุกครั้งที่เข้า เป็นการตัดสินใจด้าน UX ที่ไม่ได้ประเมินว่าเป็นปัญหา
- [templates] ไม่ได้รัน typecheck หรือ build ตามที่สั่งให้ทำแบบอ่านอย่างเดียว
- [templates] ประสิทธิภาพของ CanvasCard ตอนมี station หลายร้อยจุด: แต่ละจุดมี motion.circle ที่วน animation ไม่สิ้นสุด และทุก pointermove ตอน pan จะ re-render station ทั้งหมด ต้องวัดตอนรันจริง
- [templates] contrast ใน dark mode และการแสดงผลจริงของ glass/backdrop-blur บนเครื่องที่ GPU อ่อน
- [templates] ไม่ได้ตรวจ route ใน app/delivery-orders/{order-templates,action-templates,jobs}/** ที่ render component เหล่านี้ (อยู่นอก slice) และไม่ได้ตรวจ API proxy ใน app/api
- [templates] ไม่ได้ตรวจว่าหน้าที่ใช้ Retry/Delete/Force-offline ควรซ่อนปุ่มตาม permission หรือไม่ ใน slice นี้ไม่มีจุดไหนใช้ lib/auth/permissions เลย แต่ backend มี RequirePermission คุมอยู่
- [templates] พฤติกรรมจริงของ Reorder (drag) ใน template editor บนจอสัมผัส
- [api-layer] อ่านจริงเฉพาะ route ที่เบี่ยงจาก pattern proxyToBackend (11 ไฟล์) และ sample ตัวแทนแต่ละกลุ่ม ส่วน route ที่เหลือตรวจด้วย grep (inbound forwarding, encodeURIComponent, passthroughErrors) — path param ส่วนใหญ่เป็น GUID จึงไม่นับ unencoded ${id} เป็น finding
- [api-layer] พฤติกรรม Next 16 ในการ decode %2F ใน dynamic params (path traversal ไป backend ด้วย token ของผู้ใช้เอง) — ต้องทดสอบตอน runtime
- [api-layer] ไม่ได้รัน typecheck (task กำหนดให้ read-only ห้ามรัน npm)
- [api-layer] การจัดการ 401/session หมดอายุทั่วทั้งแอป (redirect ไป login) อยู่นอก slice
- [api-layer] ผลกระทบ proxy timeout 20s ต่อ mutation ที่ backend ยังทำงานต่อ (เช่น retryTrip ไม่มี Idempotency-Key) — ต้องดูพฤติกรรม backend ตอนรันจริง
- [api-layer] report components (sla-breach/top-failures/lead-time) ชี้ปุ่ม CSV ไปที่ orders-export — อยู่นอก slice ควรตรวจในรอบ components/reports
