# Facility map 3D view — implementation plan

> **Status**: 🔵 Not started — drafted 2026-10-23
> **Ask**: ผู้ใช้กดสลับแผนที่ live (เช่น FAN1_MAP_06242025A) จาก 2D เป็น 3D ได้ เห็น station และหุ่นยนต์ AMR เคลื่อนที่จริงในมุม 3D บน PC แท็บเล็ต และทีวี ส่วนแก้ไข station ยังทำใน 2D
> **Kind / size**: feature · L, ~23h (4 phases)
> **Parent**: none (มาจากการรีวิว prototype 3D ของผู้ใช้ 2026-10-23)
> **Assumptions**:
> - "สลับ 2D/3D ในหน้าเดิม" หมายถึงการ์ดแผนที่ live ซึ่งตอนนี้คือ `CanvasCard` บน `/home` (ผ่าน `HeroLiveMap`) ไม่ใช่ `/facility/maps` เพราะหน้านั้นไม่ได้ render แผนที่แล้ว ([maps-experience.tsx:443-446](../../frontend/components/facility/maps-experience.tsx#L443-L446)) ปุ่มสลับจะอยู่ใน `CanvasCard` จึงใช้ได้ทุกที่ที่การ์ดนี้ถูกใช้
> - พิกัดเป็นมิลลิเมตร (ผู้ใช้ยืนยัน 2026-10-23) แปลงเป็นเมตรด้วย ÷1000
> - `theta` ของหุ่นยนต์เป็น radian ([robot-layer.tsx:265](../../frontend/components/facility/robot-layer.tsx#L265) แปลง rad→deg) ส่วน `theta` ของ station ยังไม่ยืนยันหน่วย ดู Open questions

## The change in one sentence

การ์ดแผนที่ live มีปุ่ม **2D | 3D** และในโหมด 3D จะแสดงพื้นตาม extent ของแผนที่, station เป็นแท่นสีตามประเภท, หุ่นยนต์เป็นโมเดล low-poly ที่เลื่อนอย่างนุ่มตามตำแหน่งจริงทุก 1 วินาที, ปรับคุณภาพตามเครื่องอัตโนมัติ และถอยกลับเป็น 2D เองถ้าเครื่องไม่รองรับ WebGL

## Goal / non-goal

**Goal**:
- สลับ 2D ↔ 3D ได้ทันทีโดยไม่โหลดหน้าใหม่ ระบบจำโหมดที่เลือกไว้ต่อเครื่อง
- หน้าที่ไม่ได้เปิด 3D ไม่โหลด three.js เลย เพราะ chunk แยกและโหลดผ่าน `next/dynamic`
- หุ่นยนต์ใน 3D อยู่ตำแหน่งเดียวกับ 2D (ต่างกันไม่เกิน 1 tick) และหมุนหัวตาม `theta`
- ทีวีเปิดโหมด 3D ค้าง 8 ชั่วโมงแล้ว JS heap กับ GPU memory ไม่โตขึ้นเรื่อย ๆ
- แท็บเล็ตได้อย่างน้อย ~30 fps หรือลดคุณภาพเองจนถึงระดับนั้น

**Non-goal**:
- **โครงสร้างอาคาร** (ผนัง ชั้นวาง สายพาน เส้นทางวิ่ง) ผู้ใช้เลือก "station + หุ่นยนต์อย่างเดียว" และ DTMS ไม่มีข้อมูลนี้ RIOT3 import ดึงมาแค่ชื่อแผนที่กับ station ([IRiot3FacilityClient.cs:5-10](../../src/Modules/Facility/DTMS.Facility.Application/Services/IRiot3FacilityClient.cs#L5-L10)) ส่วน Zone/RouteEdge ถูกลบไปโดยตั้งใจใน `0e3b8ede`
- **ไม่แก้ backend เลย** ใช้ endpoint เดิมทั้งหมด จึงไม่ต้องแก้ `proxy.ts`
- **ไม่แก้ไข station ใน 3D** ปุ่ม Edit ใน 3D แค่เปิด drawer ตัวเดิม และเฉพาะตอนที่ `CanvasCard` ได้รับ `onEditStation` มาเท่านั้น
- **ไม่ทำ KPI tiles แบบใน prototype** (Trucks on site, Kits on time ฯลฯ) เพราะเป็นตัวเลข mock ที่ไม่มีแหล่งข้อมูลจริง
- **ไม่พอร์ตโค้ด prototype และไม่ใช้ zustand** state ส่งผ่าน props เหมือน 2D
- **ไม่ย้าย polling ไป SignalR** (งานแยกที่ระบุไว้แล้วใน [robot-layer.tsx:38-40](../../frontend/components/facility/robot-layer.tsx#L38-L40))

---

## Pre-flight (~40 min)

อ่านก่อนเริ่ม:

- [`frontend/components/facility/maps-experience.tsx`](../../frontend/components/facility/maps-experience.tsx) (2,482 บรรทัด) อ่านเฉพาะส่วนที่ระบุ
  - `TYPE_META`/`typeKey` (77-209): สีและ label ของ station แต่ละประเภท
  - `CanvasCard` (750-1640): top bar, fullscreen, bounds และ fit
  - `LiveRobots` (1878-2014): ใช้ poll ร่วมกันและวาด live chip
- [`frontend/components/facility/robot-layer.tsx`](../../frontend/components/facility/robot-layer.tsx) (506 บรรทัด)
  - `useRobotPositions` (41-78)
  - `classifyState`/`STATE_META` (85-110): สีตามสถานะ ยังไม่ได้ export
  - `RobotTooltip` (396): export แล้ว นำไปใช้ใน 3D ได้เลย
- [`frontend/components/home/hero-live-map.tsx`](../../frontend/components/home/hero-live-map.tsx) (79 บรรทัด): ผู้ใช้หลักของ `CanvasCard`
- [`frontend/lib/hooks/use-poll-schedule.ts`](../../frontend/lib/hooks/use-poll-schedule.ts): หยุด poll ตอนแท็บซ่อน (บรรทัด 64-66) ฝั่ง 3D ต้องหยุด render ตามด้วย
- [`frontend/app/globals.css`](../../frontend/app/globals.css) บรรทัด 24-74 (light) และ 117-164 (dark): token สีเป็น hex ทั้งคู่ `THREE.Color` จึงอ่านได้ตรง ๆ แต่ค่าเปลี่ยนตาม theme
- Prototype ของผู้ใช้ (ภาพ 5 รูปในบทสนทนา 2026-10-23): ใช้อ้างอิงหน้าตาเท่านั้น

Decision check before coding:

| Decision | Default | When to pick the alternative |
|---|---|---|
| ปุ่มสลับอยู่ที่ไหน | top bar ของ `CanvasCard` ข้าง chip "live" ใช้ได้ทั้ง /home และทุกที่ที่ใช้การ์ดนี้ | ถ้าต้องการ 3D เฉพาะ /home ให้ส่ง prop `allow3d` จาก `HeroLiveMap` |
| จำโหมดไว้ที่ไหน | `localStorage` key `dtms.map.view` (try/catch, ค่าเริ่มต้น `2d`) และ `?view=3d` ใน URL ชนะ localStorage เพื่อให้ทีวีแบบ kiosk เปิดเป็น 3D ได้เลย | ถ้าผู้ใช้ไม่ต้องการ URL param ให้ตัดออก (Open Q2) |
| Library | `three@0.186.1`, `@react-three/fiber@9.8.1`, `@react-three/drei@10.7.9` (เวอร์ชันเดียวกับ prototype) ตรวจกับ npm แล้ว: R3F 9.8.1 ต้องการ `react >=19 <19.4` และ repo ใช้ 19.0.0 ส่วน drei 10 ต้องการ `react ^19`, fiber `^9`, three `>=0.159` | ไม่มี ไม่ต้องลง `three-stdlib` เพราะ drei มี `RoundedBox` และ `MapControls` ให้แล้ว |
| โหลดแบบ lazy | `next/dynamic(() => import("./map-3d/scene"), { ssr: false })` เฉพาะตอนโหมด 3D | ไม่มี ห้าม import three ตรง ๆ ใน `maps-experience.tsx` |
| Camera controls | drei `MapControls` (ลากซ้าย = pan, ขวา/สองนิ้ว = หมุน, wheel/pinch = zoom) เข้ากับแผนที่มุมบน และจำกัดมุม polar ไม่ให้มองใต้พื้น | `OrbitControls` ถ้าทีวีอยากหมุนรอบจุดเดียวเท่านั้น |
| Station | `InstancedMesh` เดียวใช้ `instanceColor` วาดทุก station ใน 1 draw call | ถ้าต้องใช้รูปทรงต่างกันตามประเภท ให้แยก `InstancedMesh` ต่อประเภท (7 draw calls) |
| หุ่นยนต์ | mesh low-poly สร้างจากโค้ด (ตัวถัง rounded box + หัวลูกศร + วงแบต) ไม่มีไฟล์ GLB | GLB ถ้าผู้ใช้มีโมเดล AMR จริง ต้องเพิ่มขั้นตอนจัดการ asset (Open Q3) |
| ทำให้เคลื่อนที่นุ่ม | `THREE.MathUtils.damp` ใน `useFrame` ต่อหุ่นยนต์ และ `theta` ใช้ shortest-arc ไม่ใช้ motion spring เพราะ motion ทำงานกับ DOM/SVG | ไม่มี |
| Frameloop | `frameloop="demand"` เรียก `invalidate()` ทุก frame ตอนยังมีหุ่นยนต์เคลื่อน, ตอนผู้ใช้ลาก controls และตอนข้อมูลใหม่มา หุ่นยนต์จอดนิ่งก็ไม่ render ซึ่งสำคัญกับทีวีและแบตแท็บเล็ต | `"always"` ถ้า demand ทำให้ภาพกระตุกจนแก้ไม่ได้ (off-ramp) |
| ขนาดขั้นต่ำบนจอ | ใน `useFrame` คำนวณ world-per-pixel จากระยะกล้อง แล้ว scale หุ่นยนต์/station ไม่ให้เล็กกว่า ~10 px ใช้หลักเดียวกับ `DOT_PX`/`worldPerPx` ใน 2D | ไม่มี |
| ป้ายชื่อ | drei `<Html>` เฉพาะตัวที่ hover และตัวที่เลือก (ไม่เกิน 2 node) จึงไม่มีป้ายทับกันแบบ prototype | troika `<Text>` ถ้าภายหลังอยากให้ป้ายของ DOCK แสดงตลอด |
| สีจาก token | อ่าน `getComputedStyle(document.documentElement).getPropertyValue("--color-…")` แล้วอ่านใหม่เมื่อ `resolvedTheme` จาก `next-themes` เปลี่ยน (แบบเดียวกับ [top-nav.tsx:225](../../frontend/components/shell/top-nav.tsx#L225)) | ไม่มี |
| ปรับคุณภาพ | drei `PerformanceMonitor` + `AdaptiveDpr` ลด dpr จาก `min(devicePixelRatio, 2)` ไปถึง 1 ปิดเงาเมื่อ fps ต่ำ และเริ่มที่ dpr 1 เมื่อเป็น touch device (`matchMedia("(pointer: coarse)")`) | ไม่มี |
| Fallback | ตรวจ WebGL2 ก่อน mount ถ้าไม่มี ให้ซ่อนปุ่ม 3D และถ้าเกิด `webglcontextlost` ให้กลับเป็น 2D พร้อม toast | ไม่มี |

Open questions:

- [ ] **Q1** — `StationDto.theta` เป็น radian เหมือนหุ่นยนต์ใช่ไหม ยังไม่ยืนยัน: RIOT3 ส่ง `PosYaw` มาแต่ไม่ได้ดูว่า import แปลงหน่วยหรือไม่ บล็อก Phase 2.1 ในส่วนการหมุน station ถ้าต้องเดา: ถือเป็น radian และถ้าค่าที่ได้เกิน 2π ให้ถือเป็นองศา
- [ ] **Q2** — ทีวีเปิดหน้าไหน? `/home` แบบ fullscreen หรือ kiosk browser? และต้องการ `?view=3d` กับหมุนกล้องช้า ๆ อัตโนมัติไหม บล็อก Phase 4.3 ถ้าต้องเดา: ทำทั้งคู่ โดยหมุนอัตโนมัติเฉพาะตอน fullscreen และไม่มีใครแตะจอเกิน 60 วินาที
- [ ] **Q3** — ขนาด AMR จริงเท่าไร และมีโมเดล 3D ไหม บล็อก Phase 2.2 แค่ส่วนขนาด ถ้าต้องเดา: 1.0 × 0.7 × 0.35 ม. แบบ procedural
- [ ] **unverified** — จำนวน station บน FAN1 ดูจากภาพ 2D น่าจะประมาณ 100-200 จุด instancing รองรับได้หลายพันจุดอยู่แล้ว จึงไม่บล็อกงาน

**Estimate anchor**: `01211411` feat(frontend facility): live robot layer + station edit drawer — 7 files / +1,831 −356 lines งานนี้ขนาดใกล้เคียงกัน: ไฟล์ใหม่ประมาณ 8 ไฟล์ รวม ~1,200 บรรทัด แต่โดเมน 3D ใหม่สำหรับ repo นี้ (กล้อง, instancing, ปรับคุณภาพ) จึงตั้งไว้ ~3 วัน ไม่ใช่ 1 วัน

---

## Phase 1 — Toggle + empty 3D scene (~5h)

### 1.1 Dependencies + extract shared tables (~1h)

Files:

- Edit: `frontend/package.json` — เพิ่ม `"three": "0.186.1"`, `"@react-three/fiber": "9.8.1"`, `"@react-three/drei": "10.7.9"` และใน devDependencies เพิ่ม `"@types/three"` ที่ตรงกับ 0.186 (ตรวจเวอร์ชันด้วย `npm view @types/three versions` ตอนลง) แล้วรัน `npm install` เพื่อสร้าง lockfile ใหม่ เพราะ Dockerfile ใช้ `npm ci`
- New: `frontend/components/facility/station-types.ts` — ย้าย `TypeKey`, `TYPE_KEYS`, `TYPE_META`, `typeKey()` ออกจาก [maps-experience.tsx:77-209](../../frontend/components/facility/maps-experience.tsx#L77-L209) มาไว้ที่นี่ แล้วให้ `maps-experience.tsx` import กลับ เพื่อไม่ให้ chunk 3D import ไฟล์ขนาด 2,482 บรรทัด (ซึ่งจะเป็น import วนด้วย)
- Edit: `frontend/components/facility/robot-layer.tsx` — `export` `StateKey`, `classifyState`, `STATE_META` (บรรทัด 85-110)

ข้อควรระวัง: swatch ของ `PICKUP`/`DROPOFF`/`PARKING`/`DOCK` เป็น hex แต่ `NORMAL`/`CHARGING`/`CHECKPOINT` เป็น `var(--…)` ตัว resolver ในข้อ 2.1 ต้องรองรับทั้งสองแบบ

### 1.2 Coordinate + color helpers (~1h)

- New: `frontend/components/facility/map-3d/coords.ts`

```ts
export const MM_PER_M = 1000;
/** RIOT3 world (mm, +Y up on the floor plan) → three.js (m, Y up, floor = XZ). */
export function toScene(x: number, y: number): [number, number, number] {
  return [x / MM_PER_M, 0, -y / MM_PER_M];
}
/** Heading in radians → rotation about +Y, matching the 2D arrow (tip = +X). */
export function headingToRotY(theta: number): number { return theta; }
/** Shortest signed arc from `from` to `to`, for damping theta without a 359° spin. */
export function shortestArc(from: number, to: number): number { /* … */ }
```

- New: `frontend/components/facility/map-3d/use-theme-colors.ts` — `useThemeColors()` คืน `{ station: Record<TypeKey, THREE.Color>, robot: Record<StateKey, THREE.Color>, floor, grid }` โดย resolve `var(--x)` ผ่าน `getComputedStyle` และอ่านใหม่เมื่อ `resolvedTheme` เปลี่ยน

ข้อควรระวัง: Z ต้องติดลบ (`-y`) เพราะ 2D พลิกแกน Y ไว้แล้ว (comment ใน [maps-experience.tsx:1876](../../frontend/components/facility/maps-experience.tsx#L1876)) ถ้าลืม ภาพ 3D จะกลับซ้ายขวาเทียบกับ 2D

### 1.3 Toggle in CanvasCard + lazy scene shell (~2h)

- Edit: `frontend/components/facility/maps-experience.tsx`
  - เพิ่ม state `viewMode: "2d" | "3d"` ใน `CanvasCard` อ่านค่าตาม decision table (URL ก่อน แล้วจึง localStorage)
  - เพิ่ม segmented control **2D | 3D** ใน top bar (บรรทัด ~1278 ข้าง chip "live") ใช้ primitive เดิม ไม่ใช้ shadcn
  - ตอนเป็น `3d` ไม่ render `<svg>` กับ `LiveRobots` แต่ render `<Map3D>` ใน container เดียวกัน fullscreen, การ์ด และความสูงยังเหมือนเดิม
  - ซ่อนปุ่ม 3D ถ้า `supportsWebGL2()` เป็น false
- New: `frontend/components/facility/map-3d/index.tsx`

```tsx
"use client";
export const Map3D = dynamic(() => import("./scene").then((m) => m.Scene), {
  ssr: false,
  loading: () => <CanvasSkeleton />,   // reuse whatever skeleton CanvasCard uses
});
export function supportsWebGL2(): boolean { /* try canvas.getContext("webgl2") once, memoised */ }
```

- New: `frontend/components/facility/map-3d/scene.tsx`

```tsx
export function Scene(props: {
  map: MapSummaryDto | null;
  stations: StationDto[];
  onEditStation?: (id: string) => void;
  onContextLost: () => void;           // CanvasCard flips back to 2D + toast
}) { /* <Canvas frameloop="demand" dpr={[1, 2]} camera=…> floor + grid + MapControls */ }
```

- พื้นเป็น plane ขนาด `map.width/1000 × map.height/1000` ใช้สี `--color-canvas` มี drei `<Grid>` ระยะเส้น 5 ม. และใส่ `<MapControls>` กับ `<Bounds>` ไว้แล้วแต่ยังว่าง

### Phase 1 acceptance

- `cd frontend && npm run typecheck` ผ่าน
- `npm run build`: three.js ไม่ควรอยู่ใน chunk เริ่มต้นของ `/home` เช็กด้วย `grep -l "WebGLRenderer" .next/static/chunks/*.js` ต้องเจอใน chunk ที่โหลดตอนกด 3D เท่านั้น
- เปิด `/home` กด 3D แล้วเห็นพื้นกับกริด กด 2D แล้วแผนที่เดิมกลับมาและยังซูม/หมุนได้ปกติ reload แล้วโหมดยังอยู่

**Commit**: `feat(frontend): switch the live map to an empty 3D floor and back`

---

## Phase 2 — Stations + live robots (~6h)

### 2.1 Stations as instances (~2h)

- New: `frontend/components/facility/map-3d/stations.tsx`

```tsx
export function Stations(props: {
  stations: StationDto[];
  colors: Record<TypeKey, THREE.Color>;
  hoverId: string | null;
  selectedId: string | null;
  onHover: (id: string | null) => void;
  onSelect: (id: string | null) => void;
}) { /* one <instancedMesh args={[geo, mat, stations.length]}>; set matrix + color in useLayoutEffect */ }
```

- รูปทรง: แท่นทรงกระบอกเตี้ย ⌀0.6 ม. สูง 0.08 ม. หมุนตาม `theta` (รอ Q1)
- station ที่ `!isActive` ใช้สีจาง และ `isManualOverrideActive` ใช้ขอบ coral ตามที่ `StationLayer` 2D ทำ (อ่าน [maps-experience.tsx:1641](../../frontend/components/facility/maps-experience.tsx#L1641) ก่อนเลือกสี)
- เมื่อ `stations` เปลี่ยน ให้สร้าง instance ใหม่ทั้งชุด และ `dispose()` geometry/material ตอน unmount

### 2.2 Robots with interpolation (~3h)

- New: `frontend/components/facility/map-3d/robots.tsx`
  - เรียก `useRobotPositions(mapId)` ตัวเดียวกับ 2D ไม่เพิ่ม poll ใหม่ เพราะ 2D และ 3D ไม่ mount พร้อมกัน
  - mesh ต่อหุ่นยนต์ไม่ใช้ instancing เพราะมีน้อย (6 ตัวในภาพ) และต้องมีวงแบตแยก
  - ส่วนประกอบ: ตัวถัง `RoundedBox` 1.0×0.35×0.7 ม. (รอ Q3), ลูกศรหัวชี้ +X, วงแบตแบบ `ringGeometry` ที่ `thetaLength = 2π·battery%`
  - สีตาม `classifyState` / `STATE_META` (export ไว้แล้วใน 1.1)
  - ใน `useFrame`: `pos.x = damp(pos.x, target.x, 6, dt)`, ทำแบบเดียวกันกับ z, และ `rot.y += shortestArc(...)` แบบ damped ถ้ายังห่างจากเป้าเกิน 1 มม. ให้เรียก `invalidate()`
  - ถ้า `useReducedMotion()` เป็นจริง ให้กระโดดไปตำแหน่งใหม่เลย
  - หุ่นยนต์ที่หายไปจาก poll ให้ unmount และหุ่นยนต์ใหม่ให้ปรากฏที่ตำแหน่งจริงทันที ไม่เลื่อนมาจากจุด (0,0)

### 2.3 Minimum on-screen size (~1h)

- New: `frontend/components/facility/map-3d/use-screen-scale.ts` คืน factor ที่ทำให้วัตถุไม่เล็กกว่า `minPx` จาก `camera.position.distanceTo(target)`, `fov` และ `size.height`
- ใช้กับ group ของหุ่นยนต์และกับ scale ของ station instance
- ผลที่ได้: ถอยกล้องจนเห็นทั้งโรงงาน (~86 ม.) แล้วหุ่นยนต์ยังมองเห็นได้ ไม่เหลือเป็นจุดเล็ก ๆ แบบ prototype

### Phase 2 acceptance

- `npm run typecheck`
- เปิด 2D กับ 3D สลับกันในเวลาเดียวกัน แล้วเทียบตำแหน่งหุ่นยนต์ 1 ตัวที่จอดอยู่: อยู่ station เดียวกัน และหัวชี้ทิศเดียวกัน
- หุ่นยนต์ที่กำลังวิ่งเลื่อนนุ่ม ไม่กระโดดทุก 1 วินาที
- เปิด DevTools Performance แล้วให้หุ่นยนต์จอดนิ่งทั้งหมด จะต้องไม่มี frame ถูก render (ยืนยันว่า `frameloop="demand"` ทำงาน)

**Commit**: `feat(frontend): see stations and moving robots in the 3D map`

---

## Phase 3 — Interaction + camera (~6h)

### 3.1 Hover / select labels (~2h)

- hover station: drei `<Html>` แสดงชื่อและประเภท ใช้หน้าตาเดียวกับ `StationHoverLabel` 2D ([maps-experience.tsx:1841](../../frontend/components/facility/maps-experience.tsx#L1841))
- คลิก station: เลือกตัวนั้นและแสดงการ์ดแบบ `StationFocus` (1794) มีปุ่ม Edit เฉพาะเมื่อมี `onEditStation`
- hover หุ่นยนต์: `<Html>` ห่อ `RobotTooltip` ตัวเดิม ([robot-layer.tsx:396](../../frontend/components/facility/robot-layer.tsx#L396)) วางไว้ใน group ของหุ่นยนต์ ป้ายจึงเลื่อนตามหุ่นยนต์เอง
- แยกคลิกออกจากการลาก: ใช้ `e.delta > 6` ของ R3F เป็น threshold เดียวกับ `CLICK_THRESHOLD` ใน 2D
- ตลอดเวลามีป้ายไม่เกิน 2 อัน จึงไม่มีป้ายทับกัน

### 3.2 Camera fit + presets (~2h)

- คำนวณ fit จาก extent รวมของ station + หุ่นยนต์ บวก padding 8% ด้วย logic เดียวกับ `bounds` ใน 2D ([maps-experience.tsx:886-905](../../frontend/components/facility/maps-experience.tsx#L886-L905)) ให้ย้าย `growExtent`/`unionExtent` ไป `station-types.ts` หรือไฟล์ geometry ร่วม
- ใช้นโยบายเดียวกับ 2D: ปรับ fit ตาม bounds จนกว่าผู้ใช้จะแตะจอ จากนั้นหยุด และเปลี่ยนแผนที่หรือกด recenter แล้วกลับมา fit อีกครั้ง
- ปุ่มควบคุมมุมขวาล่าง ตำแหน่งเดียวกับ 2D: **มุมบน**, **มุมเฉียง** (polar ~55°), recenter, fullscreen ปุ่มหมุน 45° ไม่ต้องมีใน 3D เพราะ MapControls หมุนได้อยู่แล้ว

### 3.3 Live chip + chrome parity (~1h)

- ย้าย live chip ("6 robots · 0s" / "Robots offline · retrying") ออกจาก `LiveRobots` ไปเป็น component ที่ export ได้ แล้วใช้ร่วมกันทั้ง 2D และ 3D ข้อความต้องไม่ต่างกัน
- `CanvasEmptyState` ยังแสดงทับ canvas 3D เหมือนเดิมเมื่อไม่มี station

### 3.4 Help popover (~1h)

- `OperationInstructions` ([maps-experience.tsx:2018](../../frontend/components/facility/maps-experience.tsx#L2018)) แสดงวิธีควบคุมตามโหมด 3D เมื่ออยู่ใน 3D: ลากซ้าย = เลื่อน, ลากขวาหรือหมุนสองนิ้ว = หมุนมุม, ล้อเมาส์หรือ pinch = ซูม

### Phase 3 acceptance

- `npm run typecheck` และตรวจ dependency array ทุก `useEffect`/`useCallback` ใหม่ด้วยตา (repo ไม่มี eslint)
- hover station เห็นป้าย 1 อัน, คลิกเห็นการ์ด, ลากแล้วปล่อยต้องไม่นับเป็นคลิก
- สลับมุมบนกับมุมเฉียงได้ ภาพแรกเต็มกรอบพอดีกับ station ไม่มีพื้นว่างเหลือเยอะแบบ prototype

**Commit**: `feat(frontend): pick stations and robots in 3D and frame the whole site in one click`

---

## Phase 4 — Devices: tablets, TV, fallback (~6h)

### 4.1 Adaptive quality (~2h)

- `<PerformanceMonitor onDecline={…} onIncline={…}>` ลด/เพิ่ม dpr ทีละขั้น และ `<AdaptiveDpr pixelated />`
- เงา: ใช้ `ContactShadows` เฉพาะระดับคุณภาพสูง ระดับต่ำไม่มีเงาเลย
- touch device เริ่มที่ dpr 1 และไม่เปิด antialias
- ให้ผู้พัฒนาอ่านระดับคุณภาพปัจจุบันได้ผ่าน `data-quality` บน container ใช้ debug ตอนทดสอบบนแท็บเล็ต

### 4.2 Fallback + context loss (~1h)

- ฟัง `webglcontextlost` บน `gl.domElement` แล้วเรียก `onContextLost()` ซึ่ง `CanvasCard` จะสลับกลับเป็น 2D, บันทึก `2d` ลง localStorage และ toast "3D ใช้ไม่ได้บนเครื่องนี้ — กลับเป็น 2D"
- ใช้ `useToast` ตัวเดียวกับ [maps-experience.tsx:281](../../frontend/components/facility/maps-experience.tsx#L281)

### 4.3 TV mode (~1.5h) — รอ Q2

- `?view=3d` เปิดเป็น 3D ทันที
- เมื่อ fullscreen และไม่มี pointer event เกิน 60 วินาที ให้ `autoRotate` ช้า ๆ (0.3) และหยุดทันทีเมื่อมีการแตะ ถ้า `useReducedMotion()` เป็นจริงไม่ต้องหมุน
- auto-rotate ต้องเรียก `invalidate()` เองด้วย เพราะใช้ `frameloop="demand"`

### 4.4 Long-run stability check (~1.5h)

- ตรวจว่าทุก `useMemo` ที่สร้าง geometry/material มี `dispose()` ตอน unmount หรือเมื่อ deps เปลี่ยน และ `<Html>` ต้อง unmount เมื่อเลิก hover
- ทดสอบ: เปิด `/home` ใน 3D ทิ้งไว้ 30 นาทีบน PC ถ่าย heap snapshot ที่นาทีที่ 1 กับนาทีที่ 30 heap ต้องไม่โตเกิน 10% และ `renderer.info.memory.geometries`/`textures` ต้องคงที่ (log ผ่าน `data-` attribute ได้)
- สลับ 2D ↔ 3D 20 ครั้ง แล้ว `renderer.info.memory` ต้องกลับมาเท่าเดิม

### Phase 4 acceptance

- `npm run typecheck`
- Chrome DevTools → Rendering → "Emulate CPU 4× slowdown": dpr ลดลง และ fps อยู่ราว 30 ขึ้นไป
- ปิด WebGL (`chrome://flags` หรือ `--disable-webgl`): ไม่มีปุ่ม 3D ให้กด และ 2D ทำงานปกติ
- รายการทดสอบบนแท็บเล็ตและทีวีจริงไปรวมในรายการตรวจแท็บเล็ตที่ค้างอยู่ (ผู้ใช้เลือกทดสอบรวดเดียว) ดูหัวข้อ Verification

**Commit**: `feat(frontend): 3D map that slims itself down on tablets, falls back to 2D, and runs all day on a TV`

---

## Verification

| What | How | Where |
|---|---|---|
| Types | `cd frontend && npm run typecheck` | ทุก phase |
| Bundle split | `npm run build` แล้ว `grep -l "WebGLRenderer" .next/static/chunks/*.js` ต้องไม่อยู่ใน chunk ของ layout หรือ /home ตอนเริ่ม | Phase 1 |
| Position parity | เปิด 2 แท็บ (2D/3D) ดูหุ่นยนต์ตัวเดียวกัน | Phase 2 |
| Idle = no render | DevTools Performance ตอนหุ่นยนต์จอดนิ่ง ต้องไม่มี rAF frame | Phase 2 |
| Memory | heap snapshot นาทีที่ 1 เทียบนาทีที่ 30, สลับโหมด 20 ครั้ง | Phase 4 |
| Deploy | `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only frontend` แล้วเปิด `localhost:3000/home` | หลัง Phase 4 |
| Tablet (ตรวจด้วยมือ ภายหลัง) | 1) เปิด 3D ได้และลื่นพอใช้ 2) pinch/หมุนสองนิ้วทำงาน 3) แตะ station แล้วเห็นการ์ด 4) ถ้า GPU ไม่ไหวต้องกลับเป็น 2D เอง | เพิ่มในรายการตรวจแท็บเล็ตที่ค้างอยู่ |
| TV (ตรวจด้วยมือ ภายหลัง) | `/home?view=3d` + fullscreen เปิดค้าง 8 ชั่วโมง ไม่ค้างและไม่ crash, หมุนอัตโนมัติทำงาน | ส่งมอบพร้อมรายการตรวจแท็บเล็ต |

## Docs to update when this lands

- `docs/plans/facility-map-3d-view.md` (ไฟล์นี้) — เปลี่ยน Status และเขียนสรุปปิดงานท้ายไฟล์
- ADR: ไม่ต้อง เพราะเป็นมุมมองใหม่ใน frontend ไม่มีสัญญาหรือข้อมูลเปลี่ยน ส่วนการตัดสินใจ "ไม่มีโครงสร้างอาคาร" บันทึกไว้ใน Non-goal แล้ว

## Definition of done

- [ ] ปุ่ม 2D | 3D บนการ์ดแผนที่ live ใช้ได้ และระบบจำค่าไว้
- [ ] 3D แสดง station ครบทุกจุด สีตามประเภท และหุ่นยนต์ตรงตำแหน่งกับ 2D
- [ ] three.js อยู่ใน chunk แยกที่โหลดเฉพาะตอนเปิด 3D
- [ ] หุ่นยนต์จอดนิ่งแล้วไม่มี frame ถูก render
- [ ] WebGL ไม่มี/หลุด → 2D อัตโนมัติ
- [ ] heap ไม่โตเกิน 10% ใน 30 นาที
- [ ] ใช้เวลารวมไม่เกิน ~35h (เกินแผนเกิน 50% = หยุดแล้วทบทวนขอบเขตใหม่)

## Off-ramps

- **ถ้า `frameloop="demand"` ทำให้กระตุกหรือ invalidate หลุด > 2h** → ใช้ `"always"` แต่หยุด render เมื่อแท็บซ่อน (`document.hidden`) และพึ่ง PerformanceMonitor ลด dpr แทน
- **ถ้า min-screen-size scaling ทำให้วัตถุดูแปลกตอนมุมเฉียง** → จำกัด zoom out ของ MapControls (`maxDistance`) แทนการ scale
- **ถ้า Phase 4 เกินเวลา** → ส่ง Phase 1-3 ได้เลย (ใช้งานได้บน PC) แล้วทำ 4.1/4.2 ก่อน ส่วน TV mode (4.3) รอคำตอบ Q2
- **ถ้าแท็บเล็ตทำ 3D ไม่ไหวแม้ dpr 1** → ซ่อนปุ่ม 3D บน `(pointer: coarse)` แล้วใช้ 3D แค่บน PC/ทีวี

## Risks & rollback

| Risk | Likelihood | Mitigation / rollback |
|---|---|---|
| R3F 9.8.1 จำกัด `react <19.4` ทำให้อัปเกรด React ในอนาคตติด | Med | ตอนอัปเกรด React ต้องอัปเกรด R3F ด้วย ให้ใส่หมายเหตุไว้ใน commit message ของ Phase 1 |
| `npm ci` ใน Docker build ล้มเพราะ lockfile ไม่ตรง | Low | commit `package-lock.json` พร้อม Phase 1 เสมอ |
| GPU แท็บเล็ตอ่อนจนร้อนหรือกินแบต | Med | demand frameloop + dpr 1 + off-ramp ซ่อนปุ่ม |
| สีหรือทิศใน 3D กลับด้านเทียบกับ 2D | Med | acceptance ของ Phase 2 มีเช็ก parity โดยเฉพาะ |
| Rollback | — | ทุก phase เป็น commit เดียว revert ได้ ข้อมูลและ backend ไม่ถูกแตะ ถ้า revert แค่ปุ่ม (1.3) ก็ซ่อน 3D ได้ทั้งหมด |

## What would invalidate this plan

- ผู้ใช้เปลี่ยนใจอยากได้โครงสร้างอาคารจริง (ผนัง ชั้นวาง สายพาน) จะกลายเป็นงาน backend หรือ asset pipeline ต้องวางแผนใหม่ทั้งชุด
- พบว่าพิกัด RIOT3 ไม่ใช่มิลลิเมตรบนบางแผนที่ ต้องเพิ่มหน่วยลงใน `MapSummaryDto` และต้องแก้ backend

## When this plan is picked up

**First move**: เปิด `frontend/components/facility/maps-experience.tsx` ตัด `TypeKey` จนถึง `typeKey()` (บรรทัด 77-209 ยกเว้น rotation helpers ที่ 161-200) ไปไว้ใน `components/facility/station-types.ts` ใหม่ แล้วรัน `npm run typecheck`

Pre-flight ใช้ ~40 นาที ถ้าคำตอบ Q1-Q3 ยังไม่มา ให้ใช้ค่าเดาที่เขียนไว้แล้วทำ Phase 1-3 ต่อได้ จากเริ่มต้นจนเสร็จคาดว่า ~3 วันทำงาน

---

## Later, not now

- ป้ายชื่อ DOCK/CHARGING แสดงตลอดเวลาด้วย troika text
- เส้นทางวิ่งของหุ่นยนต์ ถ้าวันหนึ่ง RIOT3 เปิด API path network
- ย้าย robot poll ไป `/hubs/fleet` (งานเดิมที่ค้างอยู่)
