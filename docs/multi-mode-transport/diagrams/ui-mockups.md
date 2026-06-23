# UI Mockups — Multi-Mode Transport

ASCII mockups สำหรับ key components ที่จะ implement ใน Phase 2-5 ใช้เป็น design reference ก่อนเขียน TSX จริง

**Stack:** Next.js 16 + React + shadcn/ui (base-ui variant) + Tailwind + lucide-react
**Per [ADR-011](../adr/adr-011-frontend-architecture.md):** All components ต้องใช้ design tokens, `<DateTime />` primitive, mode-aware via `useCapabilities()`

---

## 1. Warehouse → AmrStation 2-Step Picker (Phase 2)

**Context:** Replace single `StationCombobox` with hierarchical Warehouse → AmrStation
**Used in:** Order create/edit, Trip filter, dispatch console search

### State A: Empty (mode=Amr, no selection)

```
┌─ Pickup Location ────────────────────────────────────────────┐
│                                                               │
│  Warehouse *                                                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 🔍 Select warehouse...                              ▾   │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
│  AMR Station *                                                │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ ⊘ Select warehouse first                                │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
└──────────────────────────────────────────────────────────────┘
```

### State B: Warehouse selected (AMR station picker now enabled)

```
┌─ Pickup Location ────────────────────────────────────────────┐
│                                                               │
│  Warehouse *                                                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 🏢 WH-BKK-01  Bangkok DC          [Amr] [Manual]    ▾   │ │
│  └─────────────────────────────────────────────────────────┘ │
│     ↑ shows code + name + serviceMode badges                  │
│                                                               │
│  AMR Station *                                                │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 🔍 Select station in Bangkok DC...                  ▾   │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
└──────────────────────────────────────────────────────────────┘
```

### State C: Warehouse picker opened (dropdown)

```
┌─ Warehouse Picker (popover) ─────────────────────────────────┐
│  🔍 [search by code or name______________________]            │
│  ─────────────────────────────────────────────────────────── │
│  Sort: ⊙ Code    ○ Name    ○ Distance                        │
│  ─────────────────────────────────────────────────────────── │
│  🏢 WH-BKK-01    Bangkok DC                                   │
│      📍 13.7°N, 100.5°E · 📦 [Amr] [Manual]                  │
│  ─────────────────────────────────────────────────────────── │
│  🏢 WH-CNX-01    Chiang Mai DC                                │
│      📍 18.8°N, 98.9°E · 📦 [Manual] [Fleet]                 │
│  ─────────────────────────────────────────────────────────── │
│  🏢 WH-KKC-01    Khon Kaen DC                                 │
│      📍 16.4°N, 102.8°E · 📦 [Fleet]                         │
│  ─────────────────────────────────────────────────────────── │
│      ⚠ 2 warehouses hidden (don't serve Amr mode)            │
└──────────────────────────────────────────────────────────────┘
```

### State D: AMR Station picker opened (Warehouse=WH-BKK-01)

```
┌─ AMR Station Picker (popover) ───────────────────────────────┐
│  🔍 [search in Bangkok DC_______________________]             │
│  ─────────────────────────────────────────────────────────── │
│  Filter: ⊙ Pickup  ○ Drop  ○ Charge  ○ All                  │
│  ─────────────────────────────────────────────────────────── │
│  📦 DOCK-01      Loading Bay 1            [Pickup] [Dropoff]  │
│      x=15.2 y=8.4 θ=90°                                       │
│  ─────────────────────────────────────────────────────────── │
│  📦 DOCK-02      Loading Bay 2            [Pickup] [Dropoff]  │
│      x=25.6 y=8.4 θ=90°                                       │
│  ─────────────────────────────────────────────────────────── │
│  📦 SHELF-3A     Shelf 3A                 [Pickup]            │
│      x=42.0 y=15.0                                            │
│  ─────────────────────────────────────────────────────────── │
│      ⊘ DOCK-03 (offline — operator override till 17:00)      │
└──────────────────────────────────────────────────────────────┘
```

### State E: Mode=Manual (Station picker hidden)

```
┌─ Pickup Location ────────────────────────────────────────────┐
│                                                               │
│  Warehouse *                                                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 🏢 WH-BKK-01  Bangkok DC          [Amr] [Manual]    ▾   │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
│  ℹ Manual mode — operator delivers to warehouse address       │
│    📍 123 Sukhumvit Rd, Bangkok 10110                        │
│    📞 Warehouse Manager — +66 89 123 4567                    │
│    🎯 Geofence: 100m radius                                  │
│                                                               │
└──────────────────────────────────────────────────────────────┘
```

### Behavior notes
- Switching mode resets station selection (Manual won't keep amr-station-id from prior Amr selection)
- Warehouse picker filtered by `serviceModes.includes(currentMode)`
- AMR station picker disabled until warehouse picked
- Show distance from default location when sort=Distance (requires user geo permission)
- Loading state: shimmer skeleton on combobox
- Error state: red border + inline error message

---

## 2. Operator Board (Phase 4)

**Context:** Dispatcher view of all operators currently on shift
**Used in:** `/operator-board` page
**Live data:** SignalR subscription for presence + GPS

### Full page layout

```
┌─ Operator Board ──────────────────────────────────── 🔄 Live ─┐
│                                                                 │
│  Filters:  Warehouse: [All Bangkok ▾]   Status: [On-shift ▾]   │
│            Search: [name or employee code____]                  │
│                                                                 │
│  ┌─ Stats ──────────────────────────────────────────────────┐ │
│  │  👥 12 on shift   🚛 8 in trip   ⏸ 4 idle   ⚠ 1 stalled │ │
│  └──────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ ▽ John Doe          EMP-001    📍 13.7°, 100.5°  🟢    │ │
│  │   ─────────────────────────────────────────────────────  │ │
│  │   Current trip: TRP-7f3a · Bangkok DC → DC Korat        │ │
│  │   Status: 🚛 In transit · ETA <DateTime />               │ │
│  │   Last seen: 12s ago · 4G · 🔋 72%                       │ │
│  │   Certifications: [STANDARD] [HAZMAT]                    │ │
│  │   [💬 Contact]  [🔄 Reassign]  [📋 View trip]            │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ ▷ Jane Smith        EMP-007    📍 13.8°, 100.6°  🟡    │ │
│  │   ─────────────────────────────────────────────────────  │ │
│  │   Current trip: TRP-9c2e · ⚠ SLA breach (8 min over)    │ │
│  │   Last seen: 4 min ago · 4G · 🔋 18% ⚠                  │ │
│  │   [💬 Contact]  [🔄 Reassign]  [⚠ Resolve alert]         │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ ▷ Bob Lee           EMP-014    📍 (no GPS)        ⚪   │ │
│  │   ─────────────────────────────────────────────────────  │ │
│  │   No active trip · idle 12 min                          │ │
│  │   Last seen: 1 min ago                                  │ │
│  │   [💬 Contact]  [➕ Assign trip]                          │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                                 │
│                                          [Showing 3 of 12]      │
└─────────────────────────────────────────────────────────────────┘
```

### Status legend (presence indicators)

```
🟢 Active     — heartbeat < 30s
🟡 Warning    — heartbeat 30s-2min OR battery < 20% OR SLA risk
🔴 Critical   — SLA breached OR no heartbeat > 2min
⚪ Unknown    — no GPS / app backgrounded
```

### Expanded card details (click to expand)

```
┌─────────────────────────────────────────────────────────────────┐
│ ▽ John Doe          EMP-001                                 🟢  │
│   ─────────────────────────────────────────────────────────── │
│   ┌─ Shift ─────────────────────────────────────────────────┐ │
│   │ Started: <DateTime /> · 4h 23m elapsed                   │ │
│   │ Scope: WH-BKK-01, WH-BKK-02                              │ │
│   │ Trips today: 5 completed · 1 in progress                 │ │
│   └──────────────────────────────────────────────────────────┘ │
│                                                                 │
│   ┌─ Current Trip TRP-7f3a ─────────────────────────────────┐ │
│   │ Bangkok DC → DC Korat                                    │ │
│   │ Items: 5 pallets, 480kg                                  │ │
│   │ Timeline:                                                │ │
│   │   ✓ Assigned       <DateTime />                          │ │
│   │   ✓ Acknowledged   <DateTime /> (+2m)                    │ │
│   │   ✓ Pickup         <DateTime /> (+18m)                   │ │
│   │   ⋯ Drop ETA       <DateTime />                          │ │
│   │                                                          │ │
│   │ [📋 Open trip detail]   [🗺 Show on map]                 │ │
│   └──────────────────────────────────────────────────────────┘ │
│                                                                 │
│   ┌─ Device ────────────────────────────────────────────────┐ │
│   │ iPhone 14 Pro · v1.0.3 · iOS 17.4                       │ │
│   │ Last sync: 12s ago · Network: 4G · Battery: 🔋 72%      │ │
│   │ Push token: active · GPS accuracy: 8m                    │ │
│   └──────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Empty states

```
┌─ No operators on shift ─────────────────────────────────────┐
│                                                              │
│                          👥                                  │
│                                                              │
│              No operators currently on shift                 │
│         Operators will appear here when they clock in        │
│                                                              │
│                  [Manage operators →]                        │
└──────────────────────────────────────────────────────────────┘
```

### Alert banner (stalled trips)

```
┌─ ⚠ Action Required ─────────────────────────────────────  X ─┐
│  2 trips have breached SLA in the last hour                  │
│  TRP-9c2e — Jane Smith (8 min over pickup)                   │
│  TRP-3a51 — Mark Davis (15 min over acknowledgement)         │
│                                          [Review all →]      │
└──────────────────────────────────────────────────────────────┘
```

### Behavior notes
- Auto-refresh: SignalR push every state change (no polling)
- Sort: critical-first by default (🔴 → 🟡 → 🟢 → ⚪)
- Filter persists in URL query for shareable links
- Clicking row expands; cmd-click opens trip in new tab
- Mobile breakpoint: collapse to single-column card stack
- Loading skeleton: 3 placeholder cards

---

## 3. Waybill Tracker (Phase 5)

**Context:** Fleet trip detail showing 3PL provider status + tracking
**Used in:** Trip detail drawer (Fleet mode only), `/fleet-board` page

### Inline panel (in Trip Detail Drawer)

```
┌─ Waybill ────────────────────────────────────────────────────┐
│                                                                │
│  📦 KE-2026-7F3A92                              [In Transit]   │
│  Kerry Express · Standard delivery                             │
│                                                                │
│  ┌─ Status Timeline ──────────────────────────────────────┐  │
│  │                                                          │  │
│  │  ✓ Created            <DateTime /> · 09:15              │  │
│  │  │                                                       │  │
│  │  ✓ Accepted           <DateTime /> · 09:18 (+3m)        │  │
│  │  │                                                       │  │
│  │  ✓ Picked up          <DateTime /> · 11:42 (+2h 24m)    │  │
│  │  │  📍 Bangkok DC                                       │  │
│  │  │                                                       │  │
│  │  ⋯ In transit         (currently)                       │  │
│  │  │  📍 Last scan: Lopburi sortation                     │  │
│  │  │  ETA: <DateTime /> · ~3h remaining                   │  │
│  │  │                                                       │  │
│  │  ○ Out for delivery   (pending)                         │  │
│  │  │                                                       │  │
│  │  ○ Delivered          (pending)                         │  │
│  │                                                          │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                │
│  [🔗 Track on Kerry →]    [📞 Contact provider]                │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### Compact card (in list view)

```
┌──────────────────────────────────────────────────────────────┐
│ TRP-9c2e   📦 KE-2026-7F3A92                                  │
│            Bangkok DC → 123 Sukhumvit Rd                      │
│            Kerry · 🚛 In Transit · ETA <DateTime />            │
│            ━━━━━━━━━━━━━━━━━━━━○○○ 60%                       │
│                                            [🔗] [⋯]           │
└──────────────────────────────────────────────────────────────┘
```

### Fleet board (page view — all in-flight waybills)

```
┌─ Fleet Board ──────────────────────────── 🔄 Live ─ [Filter ▾] ─┐
│                                                                   │
│  ┌─ Stats by Provider ────────────────────────────────────────┐ │
│  │  Kerry     ●●●●●●●●○○  8 in-flight   ⚠ 1 delayed           │ │
│  │  Flash     ●●○○○○○○○○  2 in-flight                          │ │
│  │  J&T       ○○○○○○○○○○  0 in-flight                          │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                   │
│  ┌─ All Waybills (10) ────────────────────────────────────────┐ │
│  │                                                              │ │
│  │  📦 KE-2026-7F3A92  Kerry   In Transit   ETA 16:00          │ │
│  │     BKK → Korat · TRP-9c2e · 480kg                          │ │
│  │                                                              │ │
│  │  📦 KE-2026-8B4C15  Kerry   Out For Delivery  ⏱ 15m         │ │
│  │     BKK → 88 Silom · TRP-7a31 · 12kg                        │ │
│  │                                                              │ │
│  │  📦 KE-2026-9D2E08  Kerry   ⚠ Delayed       ETA was 12:00   │ │
│  │     BKK → Nonthaburi · TRP-3f4d · 5kg                       │ │
│  │     Last update: 14:00 · No movement for 2h                 │ │
│  │     [🔄 Force reconcile]  [📞 Contact Kerry]                 │ │
│  │                                                              │ │
│  │  📦 FL-2026-0A1B22  Flash   Picked Up                       │ │
│  │     CNX → BKK · TRP-2e90 · 25kg                             │ │
│  │                                                              │ │
│  └──────────────────────────────────────────────────────────────┘│
│                                                                   │
│                                            [Showing 4 of 10]      │
└───────────────────────────────────────────────────────────────────┘
```

### Status color coding

```
[Created]              gray
[Accepted]             blue
[Picked Up]            cyan
[In Transit]           default (primary)
[Out For Delivery]     orange
[Delivered]            green ✓
[⚠ Delayed]            amber/yellow
[Failed]               red
[Cancelled]            slate
```

### Action panel (for delayed waybills)

```
┌─ ⚠ Delayed Waybill ────────────────────────────────────────────┐
│                                                                 │
│  📦 KE-2026-9D2E08 has not updated for 2 hours                  │
│  Expected delivery: <DateTime /> (now 4h overdue)               │
│                                                                 │
│  Last known position: Nonthaburi sortation (14:00)              │
│                                                                 │
│  ─────────────────────────────────────────────────────────────  │
│                                                                 │
│  Actions:                                                       │
│  [🔄 Force reconcile]  → polls Kerry API for latest status      │
│  [📞 Contact Kerry]    → opens support contact                  │
│  [🔁 Re-dispatch]      → cancel + create new Fleet trip         │
│  [✋ Mark failed]       → escalate to ops manager                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Behavior notes
- Status auto-refreshes every webhook from Kerry/Flash
- Reconciliation fallback polls every 10min (per ADR-005 / Phase 5)
- Click waybill → opens Trip detail drawer with Waybill panel expanded
- "Track on Kerry" opens provider tracking URL in new tab
- Delayed flag triggers when `now > estimatedDeliveryAt + 60min` without status change

---

## Color Tokens (per recent commit 46f5632)

ทุก mockup ใช้ token เหล่านี้ — **ห้าม raw hex**:

```
Status badges:
  Created          bg-muted text-muted-foreground
  InProgress       bg-primary text-primary-foreground
  Paused           bg-warning text-warning-foreground
  Completed        bg-success text-success-foreground
  Failed           bg-destructive text-destructive-foreground
  Cancelled        bg-secondary text-secondary-foreground

Presence:
  Active           text-success
  Warning          text-warning
  Critical         text-destructive
  Unknown          text-muted-foreground

Surface:
  Card             bg-card text-card-foreground border-border
  Popover          bg-popover text-popover-foreground
  Alert            bg-warning/10 border-warning/40 text-warning-foreground
```

---

## Date/Time Display (per recent commit fe44caa)

ทุก mockup ที่แสดง date/time ต้องใช้ `<DateTime />` primitive:

```tsx
// ✓ Correct
<DateTime value={trip.startedAt} />              // → "11:42"
<DateTime value={trip.startedAt} relative />     // → "2h ago"
<DateTime value={trip.startedAt} format="full" /> // → "Tue Jun 23, 11:42 GMT+7"

// ✗ Wrong — never inline format
{new Date(trip.startedAt).toLocaleString()}      // forbidden
{format(trip.startedAt, 'HH:mm')}                // forbidden
```

---

## Responsive Breakpoints

```
Mobile (< 640px):   Single column, simplified cards, no live map
Tablet (640-1024):  2-column layout, condensed timeline
Desktop (> 1024):   Full layout as shown above
```

Operator app uses **mobile breakpoint** as primary — dispatcher console uses **desktop**

---

## Accessibility

- All status badges have ARIA labels ("In transit", not just color)
- Live regions for SignalR updates (`aria-live="polite"`)
- Keyboard navigation: Tab through cards, Enter to expand, Esc to close
- Color contrast WCAG AA minimum (validated by Tailwind preset)
- Icons paired with text labels (never icon-only for actions)

---

## Implementation References

| Mockup | Phase | Implementation file (to create) |
|---|---|---|
| Warehouse picker | 2 | `frontend/components/primitives/warehouse-combobox.tsx` |
| AmrStation picker | 2 | `frontend/components/primitives/amr-station-combobox.tsx` |
| Operator board | 4 | `frontend/components/transport/manual/operator-board.tsx` |
| Operator card | 4 | `frontend/components/transport/manual/operator-card.tsx` |
| Waybill tracker | 5 | `frontend/components/transport/fleet/waybill-tracker.tsx` |
| Fleet board | 5 | `frontend/app/fleet-board/page.tsx` |

ดู [ADR-011](../adr/adr-011-frontend-architecture.md) สำหรับ folder convention + design patterns
