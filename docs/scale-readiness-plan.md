# Scale-Readiness Implementation Plan

> **Status:** Drafted 2026-06-16 — not started
> **Predecessor:** [perf-tests/results-2026-06-16/REPORT.md](../perf-tests/results-2026-06-16/REPORT.md) (E2E + Load + Stress run, Volume aborted)
> **Trigger:** Before promoting DTMS beyond pilot scale (current safe ceiling ≈ 150 concurrent users), close the architectural ceilings exposed by the perf run.

---

## Why this plan exists

Perf test 2026-06-16 ran scenarios A/B/C/D/E. Result: **0 errors at 500 VU**, but five structural ceilings appeared:

| # | Ceiling | Symptom (measured) | Architectural root cause |
|---|---|---|---|
| 1 | Outbox throughput parity 1.75% | 600k pending vs 10.5k processed | Single `BackgroundService` polls 6 modules sequentially every 5s, batch=50 |
| 2 | Postgres connection ceiling | `FATAL: too many clients already` from external psql at 500 VU | 8 Scoped `DbContext`s, no PgBouncer, `max_connections=100` (alpine default) |
| 3 | Frontend SSR 4× regression | p95 407ms → 1.22s @ 60 VU | Every new live-feature commit added uncached fetches (`cache: "no-store"`) |
| 4 | Latency knee at ~150 VU | p95 grows 5.6× from 100→500 VU | Symptom of #1+#2 — request queueing on starved pool |
| 5 | SignalR amplification | UI updated >10 min after k6 stopped | 3-5 projectors broadcast per same domain event, no coalescing on Order/Trip/Job hubs |

**Each finding has a battle-tested solution in the .NET/Postgres/Next.js ecosystem.** This plan sequences them by ROI and unblock-order. Do NOT skip ahead — Phase A unblocks B, B unblocks C, etc.

---

## Decisions

| Question | Answer | Reasoning |
|----------|--------|-----------|
| Outbox dispatcher model | Per-module parallel + `FOR UPDATE SKIP LOCKED` | Unblocks multi-instance; matches pattern in MassTransit Outbox & Wolverine |
| Worker process | Separate container post-Phase B | Lets API scale on request CPU, worker scale on backlog — orthogonal |
| DB pooling | PgBouncer (transaction mode) | Industry standard; Npgsql `DataSource` for app-side reuse |
| Read replica | Defer to Phase E | Premature until #1/#2 fixed |
| Frontend cache layer | React `cache()` + Next.js `revalidate` + ISR | Native to Next.js 16; no infra change |
| SignalR backplane | Redis (already deployed) | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` — single line of code |
| SignalR coalescing | Reuse `DashboardCounterBatcher` pattern | Already in tree at `Realtime/DashboardCounterBatcher.cs` |
| Rate limiter | Per-API-key partition, exclude /health & /hubs | Per-IP collides under corporate NAT |
| Observability stack | OpenTelemetry → Jaeger (existing) + Prometheus + Grafana | Jaeger container already in compose |
| Rollout | Feature-flagged where possible; otherwise behind config defaults that fall back to current behaviour | Each phase deployable independently |

---

## Phase A — Outbox throughput (P0, 2-3 days) ⬜

**Goal:** Drain rate ≥ create rate. Backlog reaches steady-state ≤ 1k pending under sustained 1k orders/s.

### Why P0

Without this, every other improvement is masked by event-publish backpressure. SignalR storms continue. SLA-risk projector lags. Dispatch consumers receive trip events minutes late.

### Files to change

```
src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/
├── OutboxProcessorService.cs           # rewrite: parallel modules, SKIP LOCKED, batched
├── OutboxOptions.cs                    # NEW: bind to appsettings
└── OutboxModule.cs                     # NEW: per-module dispatcher abstraction

src/Modules/*/Infrastructure/Persistence/Migrations/
└── 2026xxxx_AddOutboxPartialIndex.cs   # 6 migrations (one per module schema)
```

### Step A1 — Partial index migration (2 hr) ✅ shipped 2026-06-20

Replace plain btree on `ProcessedOnUtc` with a partial index on pending rows. Skips the scanned-but-discarded 98% of historical rows.

```sql
-- For each schema: deliveryorder, planning, dispatch, fleet, vendoradapter, outbox
DROP INDEX IF EXISTS deliveryorder."IX_OutboxMessages_ProcessedOnUtc";
CREATE INDEX CONCURRENTLY "IX_OutboxMessages_Pending"
  ON deliveryorder."OutboxMessages" ("OccurredOnUtc")
  WHERE "ProcessedOnUtc" IS NULL;
```

**Manual migration** — `dotnet-ef` is incompatible with .NET 10 preview (see [feedback_migration_manual.md](../memory/feedback_migration_manual.md)). Hand-write `MigrationBuilder.Sql(...)` calls with `suppressTransaction:true` (CONCURRENTLY can't run inside the EF migration tx). Put `[DbContext(typeof(...))]` + `[Migration("...")]` attributes directly on the class (no separate Designer file needed — codebase convention for manual migrations).

**Important** — the 6 migrations share `public.__EFMigrationsHistory`. Use **unique MigrationId timestamps per module** (e.g. `20260620000020/30/40/50/60`) or the second DbContext sees the first's record and skips its own.

**Result:**
- ✅ All 6 schemas swapped to `IX_OutboxMessages_Pending`, `indisvalid=t` on each
- ✅ `EXPLAIN ANALYZE` on `deliveryorder.OutboxMessages` poll query: `Index Scan using IX_OutboxMessages_Pending`, **Execution Time 0.461 ms → 0.065 ms** (7× faster on empty table; Sort node eliminated because the partial index is ordered by `OccurredOnUtc` matching the ORDER BY)
- ✅ Smoke order reached `Dispatched` in 3s (T1 main path unchanged)
- ⚠️ Idle DB CPU delta deferred — measure under Phase A acceptance scenario B (load test), not at idle

### Step A2 — `SKIP LOCKED` + parallel modules (1 day) ✅ shipped 2026-06-20

**Part 1 (shipped):** SKIP LOCKED raw-SQL fetch path behind `Outbox:UseSkipLocked` flag (default off). The per-module dispatcher was first extracted into `ProcessModuleAsync` + `FetchBatchAsync` + `PublishBatchAsync` + `CountPendingAsync` helpers (pure refactor), then a `ProcessModuleSkipLockedAsync` sibling was added that wraps fetch + publish + save in an explicit transaction so the `FOR UPDATE` locks hold across the publish loop. Per-tick options snapshot via `IOptionsMonitor<OutboxOptions>` so hot-reload works module-uniformly. Flag-off verified (smoke Dispatched in 3s), flag-on verified end-to-end (deliveryorder + planning outbox processed within 1 tick, zero SQL errors).

**Part 2 (shipped):** `Parallel.ForEachAsync` per-message inside `PublishBatchAsync`, bounded by new `OutboxOptions.PublishConcurrency` (default 1). Refactored to **two-phase pattern** — parallel publish into a pre-allocated `results[]` array (thread-safe because `IPublishEndpoint` + OpenTelemetry histograms are thread-safe; no shared mutable collection), then sequential mutation loop that calls `MarkAsProcessed` / `MarkAsFailed` on the calling thread (DbContext change-tracker single-threaded). Verified at concurrency=1 (regression-free) and concurrency=8 with a 5-burst: all 5 orders Dispatched in ~4.7s clustered within 0.2s of each other (vs sequential where #5 would queue behind #1-4 ~23.5s).

Together, parts 1+2 unblock Phase D (multi-replica outbox workers — SKIP LOCKED prevents row contention) AND raise single-replica throughput (parallel publish saturates IBus connection pool).

Rewrite the dispatcher loop. Original code at [OutboxProcessorService.cs:79](../src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxProcessorService.cs#L79) iterated modules sequentially with `foreach`. New shape:

```csharp
// Pseudo — one tick:
await Parallel.ForEachAsync(_modules, ct, async (module, t) =>
{
  await using var scope = _scopeFactory.CreateAsyncScope();
  await using var tx = await db.Database.BeginTransactionAsync(t);

  // Raw SQL for SKIP LOCKED — EF doesn't translate FOR UPDATE SKIP LOCKED
  var batch = await db.Database
    .SqlQueryRaw<OutboxMessage>(@"
      SELECT * FROM ""OutboxMessages""
      WHERE ""ProcessedOnUtc"" IS NULL
        AND (""NextRetryAtUtc"" IS NULL OR ""NextRetryAtUtc"" <= now())
      ORDER BY ""OccurredOnUtc""
      LIMIT @batchSize
      FOR UPDATE SKIP LOCKED",
      new NpgsqlParameter("batchSize", _options.BatchSize))
    .ToListAsync(t);

  // Publish in parallel within batch (bounded by ParallelOptions.MaxDegreeOfParallelism)
  await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = _options.PublishConcurrency, CancellationToken = t },
    (msg, ct2) => PublishOneAsync(msg, db, ct2));

  await tx.CommitAsync(t);
});
```

**Why `SKIP LOCKED`:** unblocks Phase D (multiple worker replicas) — without it, two workers fight the same row.

**Why `Parallel.ForEachAsync` per-message:** publishing to MassTransit is async I/O. Sequential blocks the whole batch on one slow recipient.

### Step A3 — Tunable options + telemetry (0.5 day)

```csharp
// appsettings.json
"Outbox": {
  "PollIntervalSeconds": 2,    // was hardcoded 5
  "BatchSize": 500,            // was hardcoded 50
  "PublishConcurrency": 8,     // was 1 (sequential)
  "PerMessageTimeoutSeconds": 10
}
```

Add OpenTelemetry counters:
- `dtms.outbox.processed` (tag: module, success)
- `dtms.outbox.pending_gauge` (per module)
- `dtms.outbox.dispatch_duration` histogram

### Acceptance — Phase A

Run perf scenario B (write, 30 VU, 70s) → check after settle:
```powershell
docker exec dtms-postgres psql -U postgres -d amr_delivery_planning --% -c "SELECT COUNT(*) FROM deliveryorder.\"OutboxMessages\" WHERE \"ProcessedOnUtc\" IS NULL;"
```
- **Before fix:** ~50k+ pending 5 min after test ends
- **After fix:** ≤ 100 pending within 30s of test ending

**Run 2026-06-20 (Steps A1+A2 only, before A3):** see [`perf-tests/results-2026-06-20/REPORT.md`](../perf-tests/results-2026-06-20/REPORT.md).
- ✅ API throughput: 696 → 933 orders/s (+34%), p95 35ms (−61%), 0 errors
- 🔴 Outbox parity: ~0.6% — drain ~10 events/s vs ~7,500 events/s generated. Bottleneck is the hardcoded `PollingInterval=5s` × `BatchSize=50` (= 10/s/module ceiling). Phase A A1+A2 are necessary but not sufficient. **Step A3 must land before acceptance can be claimed.**

**Run 2026-06-20 (full pipeline + NoOp vendor):** see [`perf-tests/results-2026-06-20/REPORT-acceptance.md`](../perf-tests/results-2026-06-20/REPORT-acceptance.md).
- ✅ API: 812 orders/s sustained, p95 39.5ms, p99 51.3ms, 0 errors, 56,134 orders accepted
- ✅ Vendor safety: 2,315 NoOp skips, **0 RIOT3 calls** (proves the safety flag works under load)
- ✅ 5,009 orders fully Dispatched within ~2 min, 0 Failed
- 🟡 Drain ~143 events/s observed (still trails ~6,400 events/s generation at 30 VU peak). Acceptance threshold "≤ 100 pending within 30s" NOT met; backlog drained over ~24 min instead.
- 📋 **Next bottleneck identified**: `ProcessUnpublishedEventsAsync` iterates 6 module DbContexts sequentially. Parallel iteration would lift drain ~5×. Worth a Step A4 follow-up before claiming Phase A acceptance.

---

## Phase B — DB connection pool (P0, 1-2 days) ⬜

**Goal:** API scales to 5+ replicas without exhausting Postgres connections. Headroom for BI/ops tooling.

### Why P0

Without PgBouncer, every API replica × DbContext-per-request × scoped-lifetime = N × M connections to Postgres. The current pilot can't even survive a rolling deploy (old pods + new pods × current load = 2× pool demand).

### Step B1 — Postgres + Npgsql config (2 hr)

```yaml
# docker-compose.yml
postgres:
  image: postgres:16-alpine
  command: >
    postgres
    -c max_connections=500
    -c shared_buffers=512MB
    -c effective_cache_size=2GB
    -c work_mem=8MB
    -c maintenance_work_mem=128MB
    -c random_page_cost=1.1
    -c effective_io_concurrency=200
```

Update connection string to use Npgsql `DataSource` (singleton, modern pooling):
```csharp
// Program.cs — replace ad-hoc UseNpgsql(connStr) calls
var npgsqlDataSource = new NpgsqlDataSourceBuilder(connStr)
  .EnableDynamicJson()
  .Build();
builder.Services.AddSingleton(npgsqlDataSource);

// Each DbContext:
services.AddDbContextPool<DeliveryOrderDbContext>((sp, o) =>
  o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
   .AddInterceptors(...));
```

Switch from `AddDbContext` → `AddDbContextPool` for all 8 DbContexts. Caveat: must remove constructor-injected scoped services from DbContext — use lazy resolution via `IServiceProvider`.

### Step B2 — Fix raw NpgsqlConnection in health check (15 min)

[Program.cs:243](../src/AMR.DeliveryPlanning.Api/Program.cs#L243) opens a raw connection per `/health` call. Replace with EF context probe or use the shared `NpgsqlDataSource`:
```csharp
var conn = await dataSource.OpenConnectionAsync(ct);
await using (conn) {
  await using var cmd = conn.CreateCommand();
  cmd.CommandText = "SELECT 1";
  await cmd.ExecuteScalarAsync(ct);
}
```

### Step B3 — PgBouncer in compose (0.5 day)

```yaml
# docker-compose.yml
pgbouncer:
  image: bitnami/pgbouncer:1.22.0
  container_name: dtms-pgbouncer
  ports:
    - "6432:6432"
  environment:
    POSTGRESQL_HOST: postgres
    POSTGRESQL_PORT: 5432
    POSTGRESQL_USERNAME: postgres
    POSTGRESQL_PASSWORD: postgres
    PGBOUNCER_DATABASE: amr_delivery_planning
    PGBOUNCER_POOL_MODE: transaction
    PGBOUNCER_MAX_CLIENT_CONN: 5000
    PGBOUNCER_DEFAULT_POOL_SIZE: 50
    PGBOUNCER_RESERVE_POOL_SIZE: 10
  depends_on:
    postgres: { condition: service_healthy }
```

Change API connection string:
```
ConnectionStrings__DefaultConnection=Host=pgbouncer;Port=6432;Database=amr_delivery_planning;Username=postgres;Password=postgres;Pooling=true;MaxPoolSize=50
```

**Transaction-mode caveat:** prepared statements + `SET LOCAL` work, but `LISTEN/NOTIFY` + temp tables don't. Verify no code uses these.

### Acceptance — Phase B

```powershell
# Run scenario E (stress to 500 VU) again
# Then immediately external psql:
docker exec dtms-postgres psql -U postgres -d amr_delivery_planning -c "SELECT count(*) FROM pg_stat_activity;"
```
- **Before fix:** `too many clients` error from external psql
- **After fix:** external psql works during peak; `pg_stat_activity` shows ≤ 200 connections (PgBouncer multiplexed many app-side connections)
- Scale `docker compose up -d --scale api=3` and re-run stress — still 0 errors

---

## Phase C — Frontend SSR cache (P1, 2-3 days) ⬜

**Goal:** SSR p95 back below 500ms at 60 VU. Headroom for 200+ concurrent browsers.

### Why P1 (not P0)

Backend can absorb more browser traffic than the frontend can render. But Phase A+B don't help if every browser tab still spawns N uncached fetches.

### Files to change

Identified by perf root-cause investigation (Agent report):

```
frontend/lib/api/
├── delivery-orders.ts          # add cache() wrapper + revalidate hints
├── facility.ts                 # remove cache: "no-store" where safe
└── dashboard.ts                # dedup getOrderFunnel callers

frontend/app/
├── layout.tsx                  # getServerSession() → cached per request
├── delivery-orders/list/page.tsx  # export const revalidate = 30
├── dashboard/page.tsx          # export const revalidate = 15
└── home/page.tsx               # Suspense boundary around HeroLiveMap

frontend/components/dashboard/
├── kpi-rail.tsx                # dedup vs orders-analysis-experience
└── orders-analysis-experience.tsx
```

### Step C1 — React `cache()` dedup (1 day)

Wrap every server-side fetch in `cache()` so multiple components in one request hit the network once:

```typescript
// frontend/lib/api/dashboard.ts
import { cache } from 'react';

export const getOrderFunnel = cache(async (range: string) => {
  const res = await fetch(`${BACKEND}/api/dashboard/funnel?range=${range}`, {
    next: { revalidate: 15 }
  });
  if (!res.ok) throw new Error(`funnel ${res.status}`);
  return res.json();
});
```

Repeat for: `listOrders`, `getOrderStats`, `getStations`, `listMaps`, `getOrderFunnel`, `getFleetUtilization`.

### Step C2 — Page-level revalidate + ISR (0.5 day)

```typescript
// frontend/app/delivery-orders/list/page.tsx
export const revalidate = 30;  // ISR: page cached for 30s
export const dynamic = 'force-static';  // when safe
```

Apply to: `/`, `/login`, `/delivery-orders/list`, `/dashboard`, `/dashboard/orders`, `/home`.

For pages that genuinely need fresh data per request (e.g. `/delivery-orders/{id}`), keep `dynamic = 'force-dynamic'` but wrap heavy panels in `<Suspense>` so the shell streams immediately.

### Step C3 — Suspense boundaries (0.5 day)

```tsx
// frontend/app/home/page.tsx
<Suspense fallback={<HeroSkeleton />}>
  <HeroLiveMap />  {/* slow fetch is inside */}
</Suspense>
```

Shell renders < 50ms; map streams in when data lands.

### Step C4 — Verify cache miss/hit ratio (instrumentation)

Add a tiny middleware/header that exposes `x-next-cache: HIT | MISS | STALE` so we can verify in browser DevTools and in perf re-runs.

### Acceptance — Phase C

Re-run scenario D:
```
docker run --rm --add-host=host.docker.internal:host-gateway ... grafana/k6 run /scripts/scenario-d-frontend.js
```
- **Before fix:** 93 req/s, p95 1.22s
- **After fix:** ≥ 200 req/s, p95 ≤ 500ms
- Each unique page seen ≥ 70% cache HIT rate after warm-up

---

## Phase D — Outbox worker container (P2, 3-5 days) ⬜

**Goal:** API container scales on request CPU; worker container scales on backlog. Orthogonal scaling axes.

### Why P2

Phase A makes outbox fast; Phase D makes it independently scalable. Worth doing before going multi-region or multi-tenant. Less urgent than fixing the throughput itself.

### Files to change

```
src/AMR.DeliveryPlanning.Worker/                  # NEW project
├── Program.cs                                    # minimal host: outbox processor + MassTransit + DbContexts
├── AMR.DeliveryPlanning.Worker.csproj
└── Dockerfile

src/AMR.DeliveryPlanning.Api/Program.cs           # conditionally skip OutboxProcessorService when WORKER_MODE=external
docker-compose.yml                                # add outbox-worker service
```

### Steps

1. Extract `OutboxProcessorService` + module registrations into shared library.
2. Create new minimal worker host project, depend on that library.
3. Add `OutboxRunsIn` config: `Api` (default, current) or `Worker` (external).
4. Add `outbox-worker` service to compose, scalable via `--scale outbox-worker=3`.
5. Verify SKIP LOCKED prevents double-publish across replicas.

### Acceptance — Phase D

- API logs show no outbox dispatch entries when `Outbox__RunsIn=Worker`
- Scale to 3 worker replicas: drain rate ≥ 3× single replica
- Kill one worker mid-run: remaining two keep draining, no message loss, no duplicates (idempotency check via message ID tracking)

---

## Phase E — Read replica + CQRS routing (P2, 1 week) ⬜

**Goal:** List/stats/reports queries offloaded from primary. Primary CPU reserved for writes + transactional reads.

### Why P2

Helpful but not urgent. Phase A+B already give significant headroom. Revisit when DB CPU sustained > 60%.

### Steps

1. Add Postgres read-replica container (streaming replication from primary).
2. Add second `NpgsqlDataSource` keyed `"read"`.
3. Annotate read-only repositories with attribute or split read-side handlers to use `"read"` source.
4. Verify replica lag stays < 1s under load (monitor `pg_stat_replication`).

### Acceptance — Phase E

- Primary CPU at 500 VU drops from 401% (current peak) to < 200%
- Replica handles list/stats traffic
- No staleness complaints (we already accept eventual consistency on projections)

---

## Phase F — SignalR backplane + projector consolidation (P1, 2 days) ⬜

**Goal:** Multiple API replicas can deliver SignalR to any client; redundant projector pushes eliminated.

### Step F1 — Redis backplane (1 hr)

```csharp
// Program.cs
builder.Services.AddSignalR()
  .AddStackExchangeRedis(redisConn, opts => opts.Configuration.ChannelPrefix = RedisChannel.Literal("DTMS:signalr"));
```

Redis already deployed. One-line change.

### Step F2 — Consolidate projector broadcasts (1-2 days)

Currently `DeliveryOrderConfirmedIntegrationEventV1` triggers 3 separate SignalR pushes via 3 projectors (see [REPORT.md finding #5](../perf-tests/results-2026-06-16/REPORT.md)). Refactor:

```csharp
// Single SignalR-orchestrating consumer per event family
// Projectors write to DB only; emit a domain-internal "ProjectionCommitted" event
// One subscriber on ProjectionCommitted does the SignalR push
```

This is the same pattern [DashboardCounterBatcher](../src/AMR.DeliveryPlanning.Api/Realtime/DashboardCounterBatcher.cs) uses with its 250ms coalescing window — extend that pattern to OrderHub / JobHub / TripHub.

### Step F3 — Rate limiter exclusions (15 min)

[Program.cs:317-336](../src/AMR.DeliveryPlanning.Api/Program.cs#L317) — exclude `/health`, `/health/ready`, `/hubs`, `/metrics`:

```csharp
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
{
  if (ctx.Request.Path.StartsWithSegments("/health") ||
      ctx.Request.Path.StartsWithSegments("/hubs") ||
      ctx.Request.Path.StartsWithSegments("/metrics"))
    return RateLimitPartition.GetNoLimiter("bypass");

  // Per API-key (JWT sub claim) if present, else per IP
  var key = ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
  return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { ... });
});
```

### Acceptance — Phase F

- 3-replica deploy: connect WebSocket to replica A, trigger event on replica B, message arrives on A
- After A1+F2, single domain event → exactly 1 SignalR push per hub (verify with hub trace + counter)

---

## Phase G — Observability backbone (cross-cutting, 2-3 days) ⬜

**Goal:** All decisions backed by data, not guesswork. SLOs measurable.

### Files

```
docker-compose.yml                                # add prometheus + grafana services
infra/prometheus.yml                              # NEW: scrape config
infra/grafana/dashboards/dtms-overview.json       # NEW: starter dashboard
src/AMR.DeliveryPlanning.Api/Program.cs           # add OpenTelemetry .NET metrics export
```

### Steps

1. Add Prometheus + Grafana to compose.
2. Add `OpenTelemetry.Exporter.Prometheus.AspNetCore` to API.
3. Build a starter dashboard with: HTTP p50/p95/p99, outbox lag, EF query duration, GC pause times, ThreadPool starvation, SignalR connection count.
4. Add alert rules: p95 > 500ms for 5 min, outbox pending > 10k for 2 min, error rate > 1% for 5 min.

### Acceptance — Phase G

Grafana shows live SLO dashboard at http://localhost:3001. Alerts fire on intentional regression (test with: throttle outbox processor manually, watch alert).

---

## Phase H — Volume & soak validation (P1, 1 week) ⬜

**Goal:** Confirm Phase A-G fixes hold under realistic data volume + duration.

### Tests to run

1. **Volume** — finish what was skipped: seed 500k orders + 1M items, run scenario A with status filter. Verify list query stays < 200ms p95.
2. **Soak** — scenario C at 50 VU for 24h. Watch memory, connections, disk, GC. Expect flat.
3. **Multi-instance** — `docker compose up -d --scale api=3 --scale outbox-worker=3`. Re-run all scenarios. SignalR delivery still < 2s.
4. **Chaos** — kill RabbitMQ for 2 min mid-test, restart. All messages eventually delivered, no orders stuck.

### Acceptance — Phase H

All above pass without regression. Update `perf-tests/results-2026-XX-XX/REPORT.md` with new headline numbers, retire the 2026-06-16 findings.

---

## Cross-cutting risks

| Risk | Mitigation |
|---|---|
| Phase A2 `SKIP LOCKED` raw SQL diverges from EF model | Pin the column list explicitly; add migration unit test that selects all outbox columns |
| `AddDbContextPool` requires removing scoped deps from DbContext ctor | Audit interceptor injection — currently `AuditSaveChangesInterceptor` + `DomainEventOutboxSaveChangesInterceptor` are scoped; refactor to resolve via `IServiceProvider` per-call |
| PgBouncer transaction mode breaks LISTEN/NOTIFY | Grep codebase for `LISTEN`, `NOTIFY`, `pg_notify` — currently none expected; verify |
| Frontend cache TTL too aggressive shows stale data | Keep TTL ≤ SignalR delivery time (currently <2s aspiration); fallback to SignalR-driven invalidation |
| Multi-instance SignalR breaks sticky-session-dependent flows | Audit hub methods for in-memory state — should be zero; all state on Redis after Phase F |
| Worker container split duplicates publish | SKIP LOCKED (A2) guarantees one worker claims each row |

---

## Sequencing (Gantt-style)

```
Week 1:  [Phase A: outbox]──────────────────────
Week 2:           [Phase B: PgBouncer]──────────
Week 2:                    [Phase C: SSR cache]──
Week 3:                                     [Phase F: SignalR backplane]
Week 3:                                              [Phase G: observability]
Week 4:                                                          [Phase D: worker]
Week 5:                                                                    [Phase H: volume/soak]
Week 6+: [Phase E: read replica — only if Phase H reveals primary CPU bound]
```

**Critical path:** A → B → C delivers 80% of value in 2 weeks.

---

## SLOs (post-plan target)

| Metric | Pre-plan (2026-06-16) | Post-plan target |
|---|---|---|
| API read p95 @ 100 VU | 93 ms | < 50 ms |
| API write p95 @ 30 VU | 91 ms | < 50 ms |
| API read p95 @ 500 VU | 524 ms | < 200 ms |
| API write p95 @ 500 VU | 647 ms | < 250 ms |
| Outbox drain parity | 1.75% | ≥ 100% |
| Outbox lag p99 | hours | < 30s |
| Frontend SSR p95 @ 60 VU | 1.22 s | < 500 ms |
| SignalR delivery p95 | unmeasured (>10 min during storm) | < 2 s |
| Max concurrent VU before errors | 500 (single-instance) | 2000+ (3-replica) |
| Postgres connection headroom | 0 (`too many clients`) | > 50% always available |

---

## Out of scope (deliberate)

- **Service mesh (Istio/Linkerd)** — premature; nginx/HAProxy enough for current scale
- **Kubernetes migration** — docker-compose adequate through Phase H; revisit when ≥ 3 production env (dev/staging/prod) require shared infra
- **Multi-region** — needs business driver first
- **Event sourcing rewrite** — current outbox + projector pattern is fine after Phase F consolidation
- **GraphQL** — REST + SignalR meets all current UI needs

---

## How to use this plan

1. Read perf REPORT.md for the data behind each finding.
2. Start at Phase A. Do NOT skip ahead — `SKIP LOCKED` (A2) is a prerequisite for Phase D's multi-worker design.
3. After each phase, re-run the relevant perf scenario and update the SLO table.
4. Daily plans should reference back to this doc (e.g. `docs/plans/2026-06-17.md` working on "Phase A1 partial index").
5. When phase complete, mark `⬜ → ✅` here and move acceptance numbers to the SLO table.
