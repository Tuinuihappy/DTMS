# ADR-011: Frontend Architecture for Multi-Mode

- **Status**: Accepted
- **Date**: 2026-06-23
- **Deciders**: Architecture team
- **Related**: [ADR-001](adr-001-multi-mode-transport-split.md), [ADR-006](adr-006-transport-mode-feature-flag.md), Phase 2-5 docs

## Context

Multi-mode refactor adds frontend changes ใน Phase 2-5 รวมหลายสิบ files ใหม่ + แก้ existing components หลายสิบ files (ดู phase docs สำหรับ list)

ปัจจุบัน frontend organization:
```
frontend/
├── app/                    Next.js App Router (pages)
├── components/
│   ├── dispatch/           (AMR-coupled — 15+ files referencing vendor fields directly)
│   ├── facility/           (Map + station UI — currently AMR-only)
│   ├── dashboard/          (KPIs hardcoding battery/charging)
│   └── primitives/         (StationCombobox, DateTime, ...)
├── lib/
│   ├── api/                (one .ts per backend module)
│   ├── hooks/              (useSWR wrappers)
│   └── vendor/             (riot3-error-codes.ts hardcoded)
└── package.json            (Next 16 + base-ui + Tailwind)
```

Recent commits show conventions being established:
- `feat(ui): cobalt console dark theme + tokenize 60+ hex escape hatches` (46f5632)
- `refactor(ui): migrate all components to the standard datetime utility` (ed8f401)
- `feat(ui): add centralized date/time utility + <DateTime /> primitive` (fe44caa)

Per [memory `project_frontend_layout`](../../memory/project_frontend_layout.md):
- Monorepo at `d:\DTMS\frontend`
- Next 16 + shadcn (base-ui variant — use `render` prop NOT `asChild`)

No test runner (Jest/Vitest/Playwright) — per [ADR-004](adr-004-testing-strategy.md) deferred

ปัญหาที่ต้องตัดสิน:
1. **Folder structure** — แยก per-mode (`transport/amr/`, `transport/manual/`) หรือ flat?
2. **Component naming** — semantic suffix (`-card`, `-panel`, `-dialog`) บังคับไหม?
3. **Mode-aware rendering** — pattern ไหน (conditional, polymorphic, route-split)?
4. **Capability flags** — frontend รู้ได้ยังไงว่า mode ไหน enabled?
5. **Design tokens vs Tailwind utilities** — ที่ทำไปแล้ว 60+ hex tokens (commit 46f5632) — formalize เป็น rule ไหม?
6. **Data fetching** — SWR pattern + cache key convention
7. **Live updates** — SignalR subscription hook pattern
8. **Loading + error states** — standardize เพื่อ consistency
9. **Frontend testing** — defer ถึงเมื่อไหร่ และ smoke checklist เป็น artifact ไหม?

## Decision

ใช้ **per-mode folder structure** + **mode-aware composition** + **capability-driven rendering** + **strict design system**

### 1. Folder Structure (target — fully achieved after Phase 5)

```
frontend/
├── app/                          Next.js App Router
│   ├── (console)/                ← dispatcher console route group
│   │   ├── trips/[id]/page.tsx
│   │   ├── operator-board/page.tsx    (Phase 4)
│   │   └── fleet-board/page.tsx       (Phase 5)
│   ├── (admin)/                  ← admin route group
│   │   ├── operators/page.tsx         (Phase 4)
│   │   ├── fleet-providers/page.tsx   (Phase 5)
│   │   └── facility/[id]/page.tsx
│   └── api/                      Next.js API routes (proxy if any)
│
├── components/
│   ├── dispatch/                 ← shared dispatch UI (mode-agnostic)
│   │   ├── trip-action-bar.tsx       (composes per-mode action panels)
│   │   ├── trip-detail-drawer.tsx    (composes per-mode extension panels)
│   │   ├── trip-list.tsx
│   │   ├── badges.tsx
│   │   └── mission-timeline.tsx
│   │
│   ├── transport/                ← per-mode folders (Phase 4+)
│   │   ├── amr/                  (moved from dispatch/ in Phase 4)
│   │   │   ├── amr-trip-extension-panel.tsx
│   │   │   ├── pass-robot-dialog.tsx
│   │   │   ├── snapshot-inspector.tsx
│   │   │   └── mission-failure-alert.tsx
│   │   ├── manual/               (Phase 4)
│   │   │   ├── manual-trip-extension-panel.tsx
│   │   │   ├── operator-board.tsx
│   │   │   ├── operator-card.tsx
│   │   │   ├── operator-picker.tsx
│   │   │   ├── reassign-operator-dialog.tsx
│   │   │   ├── geofence-editor.tsx
│   │   │   ├── sla-breach-alert.tsx
│   │   │   └── operator-presence-badge.tsx
│   │   ├── fleet/                (Phase 5)
│   │   │   ├── fleet-trip-extension-panel.tsx
│   │   │   ├── waybill-tracker.tsx
│   │   │   ├── provider-status-board.tsx
│   │   │   └── provider-picker.tsx
│   │   └── shared/               (cross-mode utilities)
│   │       ├── transport-mode-badge.tsx
│   │       └── transport-mode-icon.tsx
│   │
│   ├── facility/                 ← warehouse + map (Phase 2)
│   │   ├── warehouse-detail.tsx
│   │   ├── warehouse-list.tsx
│   │   └── facility-map.tsx          (layers: robot + operator + geofence)
│   │
│   ├── dashboard/                ← mode-aware KPI cards (Phase 4-5)
│   │   ├── amr-fleet-kpis.tsx        (rename from robots-analysis-experience.tsx)
│   │   ├── manual-operations-kpis.tsx (Phase 4)
│   │   └── fleet-logistics-kpis.tsx   (Phase 5)
│   │
│   ├── primitives/               ← reusable UI primitives
│   │   ├── date-time.tsx             ✓ (already exists per commit fe44caa)
│   │   ├── warehouse-combobox.tsx    (Phase 2)
│   │   ├── amr-station-combobox.tsx  (Phase 2)
│   │   ├── lat-lng-display.tsx
│   │   └── ...
│   │
│   └── ui/                       ← shadcn primitives (existing)
│       ├── button.tsx
│       ├── card.tsx
│       └── ...
│
├── lib/
│   ├── api/                      ← one file per backend module + per Transport mode
│   │   ├── trips.ts
│   │   ├── facility.ts               (post-Phase 2 — Warehouse only)
│   │   ├── transport-amr.ts          (Phase 3 — AmrTripExtension, position, etc.)
│   │   ├── transport-manual.ts       (Phase 4 — Operator, assignment, POD)
│   │   ├── transport-fleet.ts        (Phase 5 — Waybill, providers)
│   │   ├── system.ts                 (Phase 4 — useCapabilities)
│   │   └── ...
│   ├── hooks/                    ← SWR + SignalR wrappers
│   │   ├── use-capabilities.ts       (Phase 4 — IS source of truth for "is mode enabled?")
│   │   ├── use-trip.ts
│   │   ├── use-operator-presence.ts  (Phase 4 — SignalR subscription)
│   │   ├── use-waybill-updates.ts    (Phase 5)
│   │   └── ...
│   ├── transport/                ← per-mode utilities (cross-cutting non-component)
│   │   ├── amr/
│   │   │   └── riot3-error-codes.ts  (moved from lib/vendor/)
│   │   ├── manual/
│   │   │   └── sla-formatters.ts
│   │   └── fleet/
│   │       └── waybill-status-map.ts
│   └── utils.ts                  (cn() helper, existing)
│
└── package.json
```

### 2. Component Naming Convention

```
{noun}-{type}.tsx
```

Where `{type}` is one of:
- **`-card`** — Block UI showing entity summary (OperatorCard)
- **`-panel`** — Section within larger view (AmrTripExtensionPanel)
- **`-dialog`** — Modal overlay (ReassignOperatorDialog)
- **`-drawer`** — Slide-in panel (TripDetailDrawer)
- **`-board`** — Full page view of multiple entities (OperatorBoard)
- **`-tracker`** — Real-time status display (WaybillTracker)
- **`-picker`** / **`-combobox`** — Input for selection (WarehousePicker)
- **`-badge`** — Inline status indicator (OperatorPresenceBadge)
- **`-alert`** — Notification or warning (SlaBreachAlert)
- **`-editor`** — Interactive creation/modification (GeofenceEditor)
- **`-section`** — Composable sub-block (TripItemsSection)

PascalCase for component name, kebab-case for filename.

### 3. Mode-Aware Rendering Pattern

**Pattern A: Composition (preferred)**

Container component composes per-mode panels. Selection logic centralized:

```tsx
// components/dispatch/trip-detail-drawer.tsx
export function TripDetailDrawer({ trip }: { trip: TripDto }) {
  return (
    <Drawer>
      <SharedTripHeader trip={trip} />
      <MissionTimeline events={trip.events} />

      {/* Mode-specific extension panel — lazy loaded */}
      {trip.mode === 'Amr' && <AmrTripExtensionPanel tripId={trip.id} />}
      {trip.mode === 'Manual' && <ManualTripExtensionPanel tripId={trip.id} />}
      {trip.mode === 'Fleet' && <FleetTripExtensionPanel tripId={trip.id} />}

      <SharedTripActions trip={trip} />
    </Drawer>
  );
}
```

**Pattern B: Capability gate**

Gate top-level navigation + features by enabled modes (from `useCapabilities`):

```tsx
// components/layout/main-nav.tsx
const { data: caps } = useCapabilities();

return (
  <nav>
    <NavLink href="/trips">Trips</NavLink>
    {caps?.enabledModes.includes('Manual') && (
      <NavLink href="/operator-board">Operators</NavLink>
    )}
    {caps?.enabledModes.includes('Fleet') && (
      <NavLink href="/fleet-board">Fleet</NavLink>
    )}
  </nav>
);
```

**Anti-pattern: polymorphic component with switch inside**

```tsx
// ✗ DON'T DO THIS
function TripExtensionPanel({ trip }) {
  switch (trip.mode) {
    case 'Amr':    return <div>{/* 200 lines */}</div>;
    case 'Manual': return <div>{/* 200 lines */}</div>;
    case 'Fleet':  return <div>{/* 200 lines */}</div>;
  }
}
```

→ Each mode panel deserves its own file in `transport/{mode}/`. Use Pattern A (composition).

### 4. Capability-Driven Rendering

`useCapabilities()` is the SINGLE source of truth for "is this mode enabled":

```tsx
// lib/hooks/use-capabilities.ts
export function useCapabilities() {
  return useSWR<SystemCapabilities>(
    '/api/system/capabilities',
    fetcher,
    { revalidateOnFocus: false, dedupingInterval: 60_000 },   // cache 1 min
  );
}

// Usage
const { data: caps, isLoading } = useCapabilities();

if (isLoading) return <Skeleton />;
if (!caps?.enabledModes.includes('Manual')) return null;
```

**DON'T** hard-code mode availability:
```tsx
// ✗ DON'T
if (process.env.NEXT_PUBLIC_MANUAL_ENABLED) { ... }   // ENV var can drift from backend
const enabled = ['Amr', 'Manual'];                    // hardcoded list
```

Backend `/api/system/capabilities` (per [ADR-006](adr-006-transport-mode-feature-flag.md)) is the truth.

### 5. Design Token Discipline (per recent commit 46f5632)

**Rule:** Every color in TSX MUST come from Tailwind theme tokens. **No raw hex, no rgb(), no inline hex in className**.

**Approved color tokens:**

```
Surface:      bg-background    bg-card    bg-popover    bg-muted
Text:         text-foreground  text-muted-foreground   text-card-foreground
Primary:      bg-primary       text-primary-foreground
Secondary:    bg-secondary     text-secondary-foreground
Accent:       bg-accent        text-accent-foreground
Destructive:  bg-destructive   text-destructive-foreground
Warning:      bg-warning       text-warning-foreground
Success:      bg-success       text-success-foreground
Border:       border-border    border-input
```

For semantic UI states (Trip status, Operator presence, Waybill stage), map to semantic tokens centrally:

```tsx
// components/transport/shared/status-colors.ts
export function tripStatusVariant(status: TripStatus): BadgeVariant {
  switch (status) {
    case 'Completed': return 'success';
    case 'Failed':
    case 'Cancelled': return 'destructive';
    case 'Paused': return 'warning';
    case 'InProgress': return 'default';
    default: return 'secondary';
  }
}
```

Tailwind classes via `cn()` helper for conditional composition:

```tsx
import { cn } from '@/lib/utils';

<div className={cn(
  "rounded-lg border bg-card p-4",
  hasError && "border-destructive bg-destructive/5",
  isStale && "opacity-60"
)}>
```

### 6. Date/Time Display (per recent commit fe44caa)

**Rule:** ALL date/time display goes through `<DateTime />` primitive.

```tsx
// ✓ Correct
<DateTime value={trip.createdAt} />              // → "11:42" (smart format based on age)
<DateTime value={trip.createdAt} relative />     // → "2h ago"
<DateTime value={trip.expectedDropBy} countdown /> // → "in 15m"
<DateTime value={trip.completedAt} format="full" /> // → "Tue Jun 23, 11:42 GMT+7"

// ✗ Forbidden
{new Date(trip.createdAt).toLocaleString()}      // hardcoded format
{format(trip.createdAt, 'HH:mm')}                // bypass primitive
{trip.createdAt}                                 // raw ISO string
```

### 7. Data Fetching with SWR

**Conventions:**

```tsx
// Cache key = API path string
const { data, isLoading, error, mutate } = useSWR<TripDto>(
  trip ? `/api/trips/${trip.id}` : null,    // conditional fetch
  fetcher,
  {
    refreshInterval: 5_000,                  // optional polling
    revalidateOnFocus: true,                 // refresh on tab focus
  },
);

// Mutation pattern
async function handleAcknowledge() {
  try {
    await acknowledgeTrip(trip.id);          // API call
    await mutate();                          // re-fetch
  } catch (e) {
    setError(formatError(e));
  }
}
```

**Standard fetcher** (in `lib/utils.ts` or `lib/api/fetcher.ts`):

```tsx
export async function fetcher<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: 'include' });
  if (!res.ok) {
    const error = await res.json().catch(() => ({}));
    throw new ApiError(res.status, error.message ?? res.statusText);
  }
  const envelope = await res.json();
  return envelope.data ?? envelope;          // unwrap RIOT3-style envelope
}
```

### 8. Live Updates (SignalR)

**Hook pattern:**

```tsx
// lib/hooks/use-operator-presence.ts
export function useOperatorPresence(operatorId: string) {
  const { mutate } = useSWR(`/api/operators/${operatorId}/presence`);

  useEffect(() => {
    const sub = signalR.subscribe(`operator-presence:${operatorId}`, () => {
      mutate();   // invalidate SWR cache → triggers re-fetch
    });
    return sub.unsubscribe;
  }, [operatorId, mutate]);

  return useSWR<OperatorPresence>(`/api/operators/${operatorId}/presence`, fetcher);
}
```

→ SignalR ใช้แค่เป็น **invalidation signal** — actual data ผ่าน SWR (1 source of truth, simpler cache)

### 9. Loading + Error States

**Standard skeleton** for cards/lists:

```tsx
if (isLoading) {
  return (
    <Card>
      <CardContent className="p-6 space-y-3">
        <Skeleton className="h-4 w-1/3" />
        <Skeleton className="h-4 w-2/3" />
        <Skeleton className="h-20 w-full" />
      </CardContent>
    </Card>
  );
}
```

**Standard error display:**

```tsx
if (error) {
  return (
    <Alert variant="destructive">
      <AlertCircle className="h-4 w-4" />
      <AlertTitle>Failed to load</AlertTitle>
      <AlertDescription>{formatError(error)}</AlertDescription>
      <Button variant="outline" size="sm" onClick={() => mutate()}>
        Retry
      </Button>
    </Alert>
  );
}
```

**Empty states** — always provide actionable next step:

```tsx
if (data?.length === 0) {
  return (
    <EmptyState
      icon={<Users />}
      title="No operators on shift"
      description="Operators will appear here when they clock in"
      action={<Button onClick={() => router.push('/admin/operators')}>Manage operators</Button>}
    />
  );
}
```

### 10. Testing (deferred — manual smoke for now)

Per [ADR-004](adr-004-testing-strategy.md), no FE test runner ใน Phase 1-5. Verification:

```bash
cd frontend
npm run typecheck      # ✓ catches DTO shape changes from backend
npm run lint           # ✓ catches style + a11y violations
npm run build          # ✓ catches build-time errors
# Manual smoke: phase-specific checklist (see each phase doc)
```

**Manual smoke checklist artifact** — stored in each phase doc under `### Manual Smoke Test`

**Future:** Add Vitest + React Testing Library after Phase 5 if bug rate suggests need

## Alternatives Considered

### Alternative A: Flat `components/` structure (no `transport/{mode}/` split)

**Pros:** Less nesting, faster to find files
**Cons:**
- AMR + Manual + Fleet components mixed — file count explodes
- Hard to enforce module boundary at FE level
- File picker noise when working on single mode

**Rejected:** Per-mode folders mirror backend module split (consistency)

### Alternative B: Route-based mode split (`app/amr/trips/` vs `app/manual/trips/`)

Separate Next.js routes per mode

**Pros:** URL semantics clear
**Cons:**
- Dispatcher console sees ALL modes — duplicate page implementations
- User has to switch routes to switch context
- Trip list per mode is bad UX (operators want unified view)

**Rejected:** Mode-aware composition in shared routes preferred

### Alternative C: Storybook for component catalog

**Pros:** Visual reference, isolated component testing
**Cons:**
- Setup overhead (config, deploy)
- Drift between Storybook stories and real usage
- No test framework yet — Storybook without testing = documentation only

**Rejected for now:** Mockups in `docs/multi-mode-transport/diagrams/ui-mockups.md` serve similar purpose with lower maintenance

### Alternative D: Separate `apps/operator-mobile/` for mobile app

If mobile app shares code with web dispatcher

**Pros:** Shared types + utilities
**Cons:**
- Mobile app is React Native/Flutter — separate stack
- Backend API contract (Manual Operator API spec) is the shared boundary
- Monorepo complexity not justified

**Rejected:** Mobile app lives in separate repo. Per [Manual Operator API spec](../api/manual-operator-api.md), they sync via REST contract

### Alternative E: Server Components by default

Use Next.js Server Components heavily (per Next 16 default)

**Pros:** Smaller client bundle
**Cons:**
- Most UI is interactive (action buttons, live updates, forms)
- SWR is client-side
- SignalR requires client component
- RSC sweet spot is server-rendered lists — most of our UI doesn't fit

**Decision:** Server Components for purely static pages (admin lists with no interaction). Client Components for everything else. Use `"use client"` directive consistently.

## Consequences

### Positive

- ✓ FE folder structure mirrors backend modules — consistent mental model
- ✓ Per-mode files small + focused (no 500-line polymorphic components)
- ✓ Capability flag pattern enables runtime mode toggle (per ADR-006)
- ✓ Design tokens + DateTime primitive enforce visual consistency
- ✓ SWR + SignalR hybrid simplifies cache management
- ✓ Empty/loading/error patterns reduce decision fatigue per component

### Negative

- ✗ More folder nesting (transport/manual/components/...)
- ✗ Manual smoke testing burden grows with each phase
- ✗ No type safety between Mockup and actual component (mockup drift risk)
- ✗ SignalR connection management complexity (per-page subscription lifecycle)

### Neutral

- Capability hook adds 1 HTTP call on page load (cached 60s — negligible)
- Per-mode `lib/transport/{mode}/` cross-cutting utilities — may need refactor if shared logic emerges

## Acceptance Criteria

- [ ] Phase 2 introduces `components/primitives/warehouse-combobox.tsx` + `amr-station-combobox.tsx`
- [ ] Phase 2 splits `lib/api/facility.ts` per pattern in this ADR
- [ ] Phase 3 introduces `lib/api/transport-amr.ts` + `components/transport/amr/`
- [ ] Phase 4 introduces `lib/hooks/use-capabilities.ts` + uses for all mode-gated UI
- [ ] Phase 4 introduces `components/transport/manual/` per mockups
- [ ] Phase 5 introduces `components/transport/fleet/` per mockups
- [ ] ALL new components use design tokens (no raw hex) + `<DateTime />` primitive
- [ ] Per-phase manual smoke checklist updated in phase docs
- [ ] No raw `fetch()` in components — all through `lib/api/*.ts` modules

## Related ADRs

- [ADR-001](adr-001-multi-mode-transport-split.md) — Per-mode module structure (backend)
- [ADR-006](adr-006-transport-mode-feature-flag.md) — `/api/system/capabilities` powers `useCapabilities()`
- [ADR-004](adr-004-testing-strategy.md) — FE test deferral rationale
- [UI Mockups](../diagrams/ui-mockups.md) — visual reference for components

## References

- shadcn/ui base-ui: https://ui.shadcn.com/docs/components/ (per memory, use `render` not `asChild`)
- Next.js App Router: https://nextjs.org/docs/app
- SWR docs: https://swr.vercel.app
- Tailwind theming: https://tailwindcss.com/docs/theme
- Memory: [project_frontend_layout](../../memory/project_frontend_layout.md)
- Recent commits establishing patterns:
  - 46f5632 — design tokens
  - ed8f401 — DateTime migration
  - fe44caa — DateTime primitive
