# Facility Map — Improvement Roadmap

ภาพรวมสิ่งที่ทำเพิ่มได้กับ `/facility/maps` หลัง baseline (pan/pinch/zoom/rotate/fullscreen + LOD label + scaled grid + pixel-locked markers) ทำงานครบ

อ้างอิงเซสชั่นปรับ map UX วันที่ 2026-06-14 — สาเหตุของ list นี้คือ
canvas ปัจจุบันยัง "ขาวล้วน + จุดไม่กี่จุด" → operator รู้สึกว่าไม่มี
spatial context เทียบกับ enterprise tools (Mapbox / ArcGIS / SCADA / AMR
fleet dashboards)

**สถานะปัจจุบัน (2026-06-14):** Baseline polish เสร็จ (ดู [maps-experience.tsx](../frontend/components/facility/maps-experience.tsx)) — ทุกอย่างใน roadmap นี้คือ enhancement ที่ optional

---

## ที่ทำเสร็จแล้วใน Baseline

| Item | ตำแหน่ง | ผลลัพธ์ |
|---|---|---|
| Factor-based zoom limits (MAX_ZOOM_IN=50, MAX_ZOOM_OUT=4) | `maps-experience.tsx:842-847` | ใช้กับ map ทุก unit scale (mm/cm/m/grid) |
| `hasInteractedRef` — view ตามทัน bounds จนกว่าผู้ใช้จะ touch | `maps-experience.tsx:867-877` | แก้ initial 0.25× bug ที่เกิดจาก stale bounds |
| World-unit grid (re-uses `step` rhythm) | `maps-experience.tsx` (Y-flip wrapper child #1) | ให้ feedback ตอนซูม (เส้นกว้าง/แคบลง) แทน pixel-locked dots |
| LOD station code label — fade 2×→4× | inside `stations.map(...)` block | semantic zoom; baseline อยู่ใต้ halo (haloR + 10 px) ไม่ทับ glow |
| Pixel-locked markers (dot/ring/halo) | (unchanged from original) | ตาม cartographic standard — visual hierarchy คงที่ทุก zoom |

---

## Tier 1 — High impact, low complexity (แนะนำทำก่อน)

| # | Feature | ผลลัพธ์ที่คาดหวัง | Effort | ไฟล์/จุดที่ต้องแตะ |
|---|---|---|---|---|
| **1** | **Floor plan background layer** — `<image>` SVG element ใน world units, opacity ~0.4 อยู่หลัง grid แต่หน้า canvas | แก้ root cause ของ "canvas โล่ง" — operator เห็น layout จริง (ทางเดิน/ผนัง/เครื่องจักร) | M | `maps-experience.tsx` (Y-flip wrapper) + `MapDto.imageUrl` field |
| **2** | **Scale bar** มุมล่างซ้าย "0 ─── 5 m" อิง `worldPerPx` + map unit | Cartographic standard — operator พูดได้ทันที "ห่าง dock 10 m" | S | New overlay div inside canvas container |
| **3** | **Search → fly-to station** ใน directory drawer | คลิก station จาก list → map pan/zoom + animate ไปยังจุด | S-M | Hook up `framer-motion` view-state interpolation + ref จาก drawer |

## Tier 2 — Cartographic completeness (enterprise feel)

| # | Feature | ผลลัพธ์ | Effort |
|---|---|---|---|
| **4** | **Station footprint rectangle** (world units) แสดงขนาด bay จริง | ตอบโจทย์ "ซูมแล้วเห็นรายละเอียดเพิ่ม" อย่างมีความหมาย | M |
| **5** | **Cluster bubbles** ตอน zoom out — รวมจุดในรัศมี ~30 px → ฟอง "12" | จำเป็นเมื่อ facility มี > 50 stations | M-L |
| **6** | **Counter-rotate labels** เมื่อหมุน map — label ตั้งตรงเสมอ | Match Mapbox/Apple Maps | S |
| **7** | **Mini-map overview** มุมขวาบน — แสดง viewport ปัจจุบันบน fit-all | navigation aid ตอนซูมเข้าสูง | M |

## Tier 3 — Operational power-user features

| # | Feature | ผลลัพธ์ | Effort |
|---|---|---|---|
| **8** | **Robot trail breadcrumbs** — เส้นทาง 30 วินาทีล่าสุด (fade ตามเวลา) | diagnose pattern การวิ่ง / collision investigation | M |
| **9** | **Click-drag rectangle select** + bulk force-offline | จัดการ stations ทั้ง zone ทีเดียว | M |
| **10** | **Time travel slider** — เลื่อนย้อน 5-30 นาที | root-cause incident reconstruction | L |
| **11** | **Distance measurement tool** — คลิก 2 จุด → ระยะจริง | route planning / diagnose layout | S |
| **12** | **Keyboard shortcuts** — `+/−` zoom, arrows pan, `R` recenter, `F` fullscreen, `/` focus search | power-user efficiency | S |

## Tier 4 — Admin / data plane

| # | Feature | ผลลัพธ์ | Effort |
|---|---|---|---|
| **13** | **Map upload UI** (PNG/SVG floor plan) | unblock #1 ให้พึ่งพิงได้จริงนอก demo | M |
| **14** | **Zone overlays** สีอ่อนๆ ("Receiving" / "Picking" / "Charging") | กลุ่ม stations ตาม operational area | M |
| **15** | **Heatmap layer** — utilization ของ station หรือ density ของ robot | analytics overlay, ใช้ projection data ที่มีอยู่ | L |

---

## แนะนำลำดับ (ถ้าจะลงมือ)

1. **#1 Floor plan background** — สำคัญที่สุด แก้ปัญหา "canvas โล่งดูเหมือนยังไม่เสร็จ" ที่สุดในบรรดาทั้งหมด
2. **#2 Scale bar** — งานเล็กแต่ pay-off ทันที + ใช้งานได้จริง
3. **#3 Search → fly-to** — กลายเป็น feature ที่ขาดไม่ได้เมื่อ facility โตขึ้น
4. **#12 Keyboard shortcuts** — quick win ระหว่างรอ feature ใหญ่
5. **#4 Footprint** + **#6 Counter-rotate labels** — เก็บงาน cartographic
6. **#5 Clustering** — เริ่มจำเป็นเมื่อ facility > 50 stations
7. **#8 Robot trail** + **#10 Time travel** — investigation tools เมื่อเริ่ม operate จริงและมี incident

---

## หลักการที่ใช้ตัดสินใจ

- **Pixel-lock markers** (ดอท/halo/ring/label) ตาม cartographic standard — symbol size ต้องคงที่ ไม่งั้น operator แยกไม่ออกว่า "จุดใหญ่เพราะอะไร — ซูม? ข้อมูล? severity?"
- **World-unit content** (grid, footprint, floor plan, trail) — ให้ feedback ตอนซูม + ขยายเป็น semantic detail
- **LOD (Level of Detail)** — รายละเอียดเพิ่มตามซูม (label ตอนนี้, footprint/zone label ในอนาคต)
- **Clustering** ตอน zoom out — overcrowding fix สำหรับ density สูง

อ้างอิง enterprise patterns: Mapbox GL `interpolate` expressions, ArcGIS LOD, Google Maps semantic zoom, Samsara/MiR fleet markers
