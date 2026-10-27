# แผน: Frontend review — animation ที่ทำให้ UI เร็ว ชัด และมีชีวิต

> **สถานะ**: ✅ DEPLOYED 2026-10-23 (`d6c7d610`, frontend only) — typecheck ✓ · `next build` ✓ · overlay/route guard ✓ · **ยังไม่ได้ทดสอบบน browser/tablet** — ดู checklist ท้ายไฟล์ — 2026-10-23
> **สิ่งที่ขอ**: review frontend และเพิ่ม animation ให้ UI มีประสิทธิภาพขึ้น ทั้งด้าน feedback ที่ชัด ความเร็ว/ลื่น และความสวย
> **ขอบเขต**: Ops (`/delivery-orders`, `/dispatch`), Admin/Facility/Fleet/Templates/Reports, Dashboard/Home/Landing/Login/Profile — **ไม่รวม `/m` (tablet PWA)**
> **ประเภท / ขนาด**: refactor + bug fix + feature · L · frontend อย่างเดียว · 5 phase (P0–P4)
> **ตรวจสอบ**: `npm run typecheck` (ไม่มี eslint — ตรวจ dep array ด้วยตา) + `npm run build` (prebuild guard) · ทดสอบบน tablet/desktop รวมไว้ใน checklist เดียวตอนท้าย

## สิ่งที่พบ (สรุป)

**ต้นเหตุหลักที่ทำให้รู้สึกช้า — animation เอง**
1. Shell (`<TopNav/><LeftRail/>`) mount ในแต่ละ page (27 หน้า) ไม่มี shared layout → ทุกครั้งที่กดเปลี่ยนหน้า header fade 0.55s + rail stagger + rail ที่ขยายไว้สปริงจาก 56→232px (`left-rail.tsx:270`, `top-nav.tsx:25`)
2. Rail toggle animate `width` + `<main>` `transition-[padding] duration-300` → reflow ตารางทั้งหน้าทุกเฟรม
3. ตาราง: `AnimatePresence` บน `<tr>` + exit 0.18s + stagger สูงสุด 0.4s → เปลี่ยนหน้า/sort/filter แล้ว tbody สูงสองเท่าชั่วขณะ (`data-row.tsx:56-63`, `orders-table.tsx:185`, `mobile-card-row.tsx:146`)
4. ไม่มี `MotionConfig reducedMotion` — CSS rule `globals.css:577` ไม่ครอบ motion JS; มี infinite loop JS หลายจุด (skeleton, empty state, badge ต่อแถว, map, login)
5. Facility map re-render ทั้ง SVG ทุก 1s poll และทุกเฟรม pan; pulse JS ทุก station; drawer `backdrop-blur` ทับ canvas สด
6. Login: panel opacity 0 จน ~0.55s, settle ~1.4s; รอ 650ms หลัง login; AuthPanel mount สองชุด
7. Duration กระจาย 0.16–1.4s, 243 inline transition, ไม่มี token กลาง

**Bug ที่ไม่ใช่ animation** — ดู P0

## P0 — แก้ bug

| # | ปัญหา | ที่ | วิธีแก้ |
|---|---|---|---|
| B1 | order ที่กด cancel (รอ undo 6s) โผล่กลับมาเมื่อ realtime/refresh | `orders-experience.tsx:374` | filter `res.items` ด้วย `pendingCancelsRef` |
| B2 | toast Undo อยู่ 6.5s แต่ cancel ยิง 6s | `toast.tsx:47` / `orders-experience.tsx:702` | ใช้ค่าเดียวกัน (6s) + progress bar (P3) |
| B3 | Retry ส่ง reason ตายตัว | `trip-action-bar.tsx:90` | ส่ง `reason` จาก dialog |
| B4 | โหลดแรกพัง แสดง "No orders yet" | `orders-table.tsx:118` / `orders-experience.tsx:845` | ส่ง error เข้า table แสดง error state + retry |
| B5 | `hasFilters`/clear ไม่รวม failed-trip / active-job | `orders-experience.tsx:895-904` | รวมทั้งสอง |
| B6 | drawer เปลี่ยน order ยังโชว์ข้อมูลเก่า; status ไม่อัปเดต live | `detail-drawer.tsx:104-131` | clear data เมื่อ id เปลี่ยน + debounced reload บน `ActivityUpdated` (แบบ trip drawer) |
| B7 | login form render สองชุด | `login-experience.tsx:38,54` | render ชุดเดียว |
| B8 | toggle "Include raw" ล้าง mission events และ title กลายเป็น skeleton | `trip-detail-drawer.tsx:120-130` | reset เฉพาะเมื่อ `tripId` เปลี่ยน |
| B9 | map toast "Saved" ก่อน refresh | `maps-experience.tsx:323-334` | ย้าย toast หลัง refresh |
| B10 | IAM ใช้ `confirm()` native | `iam-*-experience.tsx` | ModalLayer confirm + toast |
| B11 | trips empty state บอก "match your filters" แม้ไม่มี filter | `trips-experience.tsx:564` | แยกข้อความ |
| B12 | `app/error.tsx` `reset()` ไม่ refetch server component | `app/error.tsx:38` | `startTransition(() => { router.refresh(); reset(); })` |

## P1 — Foundation

- `lib/motion.ts`: `dur`, `ease`, `spring`, `T`, variants (`fade`, `pop`, `dialog`, `drawerRight`, `toast`), `stagger(i)` cap ≤ 0.15s รวม
- `app/providers.tsx`: `<MotionConfig reducedMotion="user">`
- Shell ย้ายเข้า shared layout — route group `app/(app)/layout.tsx` (URL ไม่เปลี่ยน; ต้องตรวจ `proxy.ts` allowlist + prebuild guard), rail state อ่านจาก cookie บน server → render width ถูกตั้งแต่แรก, header/rail `initial={false}`
- เอา `transition-[padding]` ออกจาก `<main>`
- CSS: `.skeleton-pulse`, `.row-flash` keyframes + `--dur-*` vars (โดน reduced-motion rule อัตโนมัติ)
- Toast กลาง `components/primitives/toast.tsx` mount ใน providers — error = `role="alert"` ไม่ auto-dismiss, pause on hover/focus, รองรับ action + progress bar; ย้าย 4 ตัวเดิมมาใช้
- `app/(app)/loading.tsx`

## P2 — เร็ว/ลื่น

- Table rows: เลิก `AnimatePresence`/exit บนแถว; tbody key ตาม query signature fade 120ms; row entrance เฉพาะโหลดแรก 0.18s stagger ≤ 0.12s
- Cap duration/delay ทั้งหมดบน UI ที่กดได้ (header 0.5s, pagination delay, drawer content 0.4s, stagger ไม่มี cap ใน drawer)
- `transition-all` + `whileHover/whileTap` → `transition-[background-color,box-shadow,color]`
- Blur: toast/drawer/backdrop เลิก animate บน blur, panel ทึบ
- Infinite JS loops → CSS (skeleton, empty state, badge ต่อแถว, compass, marquee, faux-3d)
- Map: แยก `<LiveRobots>` ถือ poll, memo station layer, pulse เฉพาะ station ที่ pin/robot error, stagger station เฉพาะโหลดแรก
- Login: form โชว์ทันที, `autoFocus`, ไม่มี blur text, prefetch + push ทันทีหลัง login
- Landing intro: ครั้งเดียวต่อ session + กดข้ามได้
- Dashboard: stagger รวม ≤ 0.3s, width/height → scaleX/scaleY, funnel ticks 130 → 1 ต่อคอลัมน์
- `NumberTicker`: เขียน `textContent` ผ่าน motion value, reduced-motion jump

## P3 — Feedback ชัดขึ้น

- Realtime row flash (`useFlashOnChange`) เมื่อ status/updatedAt เปลี่ยนจาก realtime (ไม่ใช่ตอนเปลี่ยนหน้า)
- Status badge เปลี่ยนแบบ crossfade; `animate-ping` ในตาราง → breathe เบาๆ
- `primitives/button.tsx` มี `pending` (spinner, `aria-busy`, ล็อก width); trip action bar ใช้ `Loader2`
- Table dim (opacity 0.6) ระหว่าง refetch ไม่ silent; carrier screens เลิกสลับเป็น spinner
- Bulk bar count เลื่อน, toast ยกขึ้นเมื่อ bulk bar เปิด
- Drawer content crossfade เมื่อสลับ id

## P4 — Polish

- Trip status step track + `layoutId` marker
- Number ticker บน total count
- Confirm/cancel dialog: check morph ก่อนปิด
- Login error shake, leaderboard `layout` reorder, live feed insert, chart reveal ครั้งแรก
- Window toggle `layoutId` pill + `aria-pressed`, audit log chip pill

## ผลการทำ (2026-10-23)

**เปลี่ยนจากแผน**
- B3 (Retry reason) **ไม่ใช่ bug** — dialog Retry ไม่มีช่องเหตุผล; ถ้าส่ง `reason` จะได้ข้อความ default ของ Cancel ไปแทน → คงเดิม
- Login ไม่ใส่ `router.prefetch` ก่อน login — `proxy.ts` จะตอบ redirect ไป `/login` แล้ว router อาจ replay redirect นั้นหลัง login
- Reports ไม่ dim ข้อมูลเก่าตอนเปลี่ยน window — `useProjectionPoll` ล้างข้อมูลโดยตั้งใจ (ห้ามโชว์ตัวเลขของ window เก่า)
- P4 ข้าม: trip status step track (สถานะไม่เป็นเส้นตรง), ticker บน total count (SectionLabel รับแค่ string), check-morph ใน order cancel dialog (เป็น delayed write + Undo), feed insert บน dashboard (mock ไม่เปลี่ยน)

**ของใหม่ที่ควรรู้**
- `app/(app)/` route group + `layout.tsx` (shell ถาวร), `loading.tsx`, `error.tsx` — URL เดิมทุกหน้า; `check-route-protection.mjs` สแกนเข้า route group แล้ว
- Rail state ย้ายจาก localStorage → cookie `tms-rail-expanded` (`lib/shell.ts`) — มี migrate ครั้งเดียว
- `lib/motion.ts`, `components/primitives/{toast,confirm-dialog,button}.tsx`, `data-table/row-group.tsx`, `lib/hooks/{use-flash-on-change,use-media-query}.ts`
- `components/delivery-orders/toast.tsx` ถูกลบ → ใช้ `@/components/primitives/toast` (mount ใน providers)

## Sidebar (ปรับตามผู้ใช้ 2026-10-23 → 2026-10-27)

- ปุ่มขาวทรงแคปซูลแยกทีละอัน ไม่มีแผงพื้นหลัง ไม่มีเส้นคั่น ไม่มีชื่อหมวด — เรียงต่อกันแถวเดียวทั้งตอนกางและหด
- ทั้งแถบ scroll เป็นรายการเดียว ไม่มีส่วนที่ปักไว้ (Admin อยู่ท้ายสุด)
- หด/ขยายใช้รูปทรงปุ่มเดียวกัน — spring ของความกว้างทำหน้าที่ morph ทั้งสองทาง
- โลโก้ TopNav กึ่งกลางตรงคอลัมน์ไอคอน (x = 44px); TopNav เต็มความกว้างบน lg+

**ตัดสินใจกับผู้ใช้ 2026-10-27**
- ชื่อหมวดตอนกาง sidebar → เอาออก
- Login บนมือถือ autofocus ช่อง username (คีย์บอร์ดเด้งทันที) → ยอมรับได้
- จอ > 1472px ปุ่มฝั่งขวาของ TopNav ชิดขอบจอ → โอเค

## Checklist ทดสอบ (desktop + tablet — ยังไม่ได้ทำ)

**Shell / ทั่วไป**
- [ ] Sidebar: หด/ขยายลื่นทั้งสองทาง, โลโก้ตรงแนวปุ่ม, scroll ได้ทั้งแถบ, ตอนหดชี้ไอคอนมี tooltip และกลุ่มมีเมนูเด้งข้าง
- [ ] กดเปลี่ยนหน้าไปมา: top bar + rail ไม่ fade/เลื่อนใหม่; rail ที่ขยายไว้ reload แล้วไม่ "เด้ง" จากแคบ
- [ ] toggle rail: เนื้อหาขยับทันที ไม่กระตุก; ตั้งค่าเดิมจาก localStorage ยังอยู่หลัง deploy ครั้งแรก
- [ ] เปิด OS "Reduce motion": ไม่มีอะไรเด้ง/ลอย/pulse
- [ ] Toast: ซ้อนได้, ข้ามหน้าแล้วยังอยู่, error ไม่หายเอง, hover แล้วหยุดนับ

**Orders / Trips**
- [ ] cancel order → แถวหายทันที, ระหว่าง 6s มี realtime เข้า แถวไม่โผล่กลับ; Undo มีแถบหดตามเวลา, กดช่วงท้ายยังได้ผล
- [ ] เปลี่ยนหน้า/sort/filter: fade ครั้งเดียว ตารางไม่สูงสองเท่า
- [ ] status เปลี่ยนจาก realtime: แถวกระพริบ + badge crossfade; เปลี่ยนหน้าแล้วไม่กระพริบ
- [ ] filter "failed trip"/"active job" ไม่มีผล → มีปุ่ม Clear filters; API ล่มตอนโหลดแรก → error + Retry (ไม่ใช่ "No orders yet")
- [ ] เปิด order drawer แล้วสลับ order: ไม่เห็นข้อมูล order เก่า; status ใน header อัปเดต live
- [ ] trip drawer: toggle "Include raw" ไม่ล้าง mission events / title
- [ ] Bulk bar: toast อยู่เหนือ bar, ตัวเลขเลื่อน, ปุ่มที่กดหมุน
- [ ] trip Cancel/Retry: ติ๊กสั้นๆ ก่อนปิด, ปุ่มไม่เปลี่ยนความกว้างตอน busy

**Facility map (tablet สำคัญสุด)**
- [ ] tooltip ตามหุ่นที่กำลังวิ่ง ทั้งตอน pan และ rotate 45°/90°
- [ ] ลากเมาส์ผ่าน aisle เร็วๆ ไม่กระพริบ; tap = pin + โชว์ card
- [ ] หุ่นนอก cluster ยังขยายกรอบแผนที่; stations animate เฉพาะครั้งแรกของแต่ละ map
- [ ] ping เฉพาะ station ที่ pin และหุ่น ERROR; chip "Robots offline · retrying" ตอน poll ล้ม
- [ ] station drawer ทึบ อ่านง่ายทั้ง light/dark

**Admin / Fleet**
- [ ] IAM: deactivate/delete/rotate/revoke ใช้ dialog ในแอป (Esc ปิดได้, focus เริ่มที่ Cancel) + toast สำเร็จ
- [ ] Carriers: บันทึก/เปลี่ยน filter ไม่กลายเป็น spinner ทั้งตาราง, scroll ไม่หาย, แถวที่แก้กระพริบ
- [ ] Carrier types: reload ล้ม → banner (ไม่ทับทั้งหน้า)

**Login / Landing / Dashboard**
- [ ] /login: ฟอร์มขึ้นทันที, focus ที่ username, Chrome autofill แล้ว label ไม่ทับ
- [ ] รหัสผิด: สั่นครั้งเดียว ข้อความที่พิมพ์ไม่หาย; พิมพ์ใหม่ error หาย; login สำเร็จไปหน้าถัดไปทันที
- [ ] /: intro เล่นครั้งเดียวต่อ session, กด/คีย์ข้ามได้, reload ไม่กระพริบ
- [ ] /dashboard: เข้าครั้งที่สองไม่เล่น entrance; ticker format ถูก (`+18%`); Profile route สีถูกใน dark

## สิ่งที่ตั้งใจไม่ทำ

- `LazyMotion` (ประหยัด ~15–25 kB gz แต่ LAN tool, ต้อง `domMax` สำหรับ layoutId) — ไม่คุ้มตอนนี้
- View Transitions / `experimental.viewTransition` — ยัง experimental
- mock data บน dashboard/home/profile — คงไว้ตามที่ตกลง
