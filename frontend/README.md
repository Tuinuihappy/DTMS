# DTMS Templates UI

Single-page Next.js operator console for the DTMS backend's
**ActionTemplate** + **OrderTemplate** APIs. Left pane is the
reusable-action catalog; right pane composes order templates and
exercises the RIOT3 `/instantiate` endpoint.

```
ActionTemplate catalog  │  OrderTemplate composer
─────────────────────── │  ─────────────────────────
• LIFT_PALLET           │  Saved templates ▾
  id=4 p0=1 p1=0        │
• DROP_PALLET           │  Editor:
  id=4 p0=2 p1=0        │   Missions  [MOVE … ] [ACT ⤳ LIFT_PALLET]
                        │   [Save]  [Instantiate ▸]
```

## Tech

- Next.js 16 (App Router, Turbopack)
- TypeScript (strict)
- Tailwind CSS v4
- shadcn/ui (base-ui variant)
- TanStack Query v5 — caching + optimistic mutations
- react-hook-form + zod — forms + validation
- sonner — toasts
- lucide-react — icons

## Backend coupling

API routes (the frontend hits these through the Next.js rewrite):

| Method | Path | Purpose |
|---|---|---|
| GET / POST / PATCH / DELETE | `/api/v1/planning/action-templates` | catalog CRUD |
| POST | `/api/v1/planning/action-templates/{id}/activate` | enable |
| POST | `/api/v1/planning/action-templates/{id}/deactivate` | disable |
| GET / POST / PATCH / DELETE | `/api/v1/planning/order-templates` | composer CRUD |
| POST | `/api/v1/planning/order-templates/{id}/activate` | enable |
| POST | `/api/v1/planning/order-templates/{id}/deactivate` | disable |
| POST | `/api/v1/planning/order-templates/{id}/instantiate` | resolve + send to RIOT3 (or dry-run preview) |

The contracts live in `types/*.ts` (zod schemas + plain TS interfaces)
and are mirrored from the C# DTOs in the DTMS backend
(`src/Modules/Planning/AMR.DeliveryPlanning.Planning.Presentation/PlanningEndpoints.cs`).
When the backend contract changes, update those files and the wire-format
helpers (`missionFormToRequest`, `missionToForm`, etc.).

## Running locally

```bash
# 1. Start the backend (separate terminal, from the DTMS repo root)
cd ..
dotnet run --project src/AMR.DeliveryPlanning.Api
# Make sure Auth:Disable=true is exported so requests don't 401.

# 2. Start this frontend
cd frontend
npm install        # first time only
npm run dev
# → http://localhost:3000
```

The Next.js dev server proxies `/api/*` to `http://localhost:5219` via
the rewrite in `next.config.ts`. If your backend lives elsewhere, set
`DTMS_BACKEND_URL` before `npm run dev`:

```bash
DTMS_BACKEND_URL=http://10.0.0.42:5219 npm run dev
```

If you prefer to skip the proxy and hit the backend directly from the
browser, set `NEXT_PUBLIC_API_BASE` and **also** delete the rewrite block
in `next.config.ts` so both layers don't fight. CORS must be enabled on
the backend in that case (it isn't today).

## Manual smoke test

1. **ActionTemplate create** — left pane → `+ New`, fill
   `name=TEST_LIFT, id=4, param0=1, param1=0`, submit. Row appears
   immediately (no full-page refetch).
2. **ActionTemplate edit** — `[edit]` on TEST_LIFT, change `param0`
   to `2`, save. Row updates in place.
3. **Activate/deactivate** — `⌖` icon dims the row. Untick "Show
   inactive" to hide; tick to bring it back.
4. **Delete** — `[×]` shows a destructive confirm dialog.
5. **OrderTemplate create** — right pane, `+ New OrderTemplate`,
   name=`DEMO`, add a MOVE mission (mapId/stationId), add an ACT
   mission pointing at `TEST_LIFT`, save.
6. **Instantiate (dry run)** — `▸` icon, enable `dryRun`, submit.
   Resolved envelope is rendered in the JSON viewer; `TEST_LIFT` is
   expanded into its parameters.
7. **Instantiate (live)** — disable `dryRun`, submit. Toast shows the
   RIOT3 `orderKey`, or a clean error if RIOT3 is offline.
8. **Validation** — submit an ActionTemplate without `param0` →
   inline error, no network call.

## Project layout

```
app/
  layout.tsx       # root layout + providers + Toaster
  providers.tsx    # QueryClient + TooltipProvider
  page.tsx         # split-panel root
components/
  ui/              # shadcn primitives (base-ui)
  action-template/ # list, form, row
  order-template/  # list, form, mission-row, instantiate-dialog
  shared/          # empty-state, confirm-destructive, json-viewer
lib/
  api.ts           # fetchJson wrapper + ApiError
  action-templates.ts, order-templates.ts  # typed clients
  query-keys.ts    # central cache-key factory
  utils.ts         # cn() helper
types/
  action-template.ts, order-template.ts    # zod schemas + DTOs
```

## Not in scope (yet)

- Authentication UI (relies on backend `Auth:Disable=true` for now)
- Mobile breakpoints (desktop, minimum ~1280px)
- MilkRunTemplate, Job, DeliveryOrder, Trip dashboards
- i18n
- Persisted UI state (filters reset on reload)
