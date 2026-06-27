# Scale-Readiness Implementation Plan

> **Status (2026-06-20 end of day):** Phase A + B + D all 🟢 **SHIPPED** today — 27 commits across outbox tuning (A1-A4), vendor isolation (NoOp × 2 seams + composition log + `.env.test`), connection pool (singleton NpgsqlDataSource + PgBouncer transaction-mode), and dedicated outbox-worker container. API +34% TPS, p95 −61%, 0 errors at 30 VU, 0 RIOT outbound, p99 −32% with Phase D split. Burst-acceptance "≤100 pending in 30s" still ❌ at 30 VU peak — root cause confirmed: consumer DB row-lock serialization, NOT outbox/pool/container architecture. Scale-ready foundation complete for realistic sustained load + multi-replica deploy.
> **Predecessor:** [perf-tests/results-2026-06-16/REPORT.md](../perf-tests/results-2026-06-16/REPORT.md) (E2E + Load + Stress run, Volume aborted)
> **Acceptance runs (chronological):** [REPORT.md (A1+A2 only)](../perf-tests/results-2026-06-20/REPORT.md) · [REPORT-acceptance.md (A1+A2+A3 + NoOp, sequential modules)](../perf-tests/results-2026-06-20/REPORT-acceptance.md) · [REPORT-acceptance-final.md (full A1-A4 + dual NoOp + `.env.test`)](../perf-tests/results-2026-06-20/REPORT-acceptance-final.md)
> **Load-test runbook:** copy [`.env.test.example`](../.env.test.example) → `.env.test`, then `docker compose --env-file .env.test up -d --force-recreate api`. Boot log MUST show `[Composition] = NoOp/NoOp` before launching k6.
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

## Phase A — Outbox throughput (P0, 2-3 days) 🟢 CODE COMPLETE 2026-06-20

**Goal:** Drain rate ≥ create rate. Backlog reaches steady-state ≤ 1k pending under sustained 1k orders/s.

**Progress (2026-06-20):**
- ✅ A1 partial index — shipped, EXPLAIN clean
- ✅ A2 part 1 SKIP LOCKED + part 2 Parallel.ForEachAsync per-message — shipped, both verified under load
- ✅ A3 core: PollIntervalSeconds + PerMessageTimeoutSeconds tunable — shipped, hot-reload
- 🟡 A3 telemetry (per-module counters / gauge / dispatch_duration histogram) — deferred
- ✅ A4 Parallel per-module loop — shipped, verified safe + 0 RIOT via full NoOp + `.env.test`
- ⬜ A5 Consumer parallelism (NEW, surfaced post-B+D) — `ConcurrentMessageLimit` tuning on heavy consumers. Only A-stream item left to push burst drain >300/s
- 🟡 Acceptance — 5 runs done across the day; "≤100 pending in 30s at 30 VU peak" threshold NOT met because consumer logic + Postgres row locks per order are the binding constraint, NOT outbox/pool/container architecture. **Realistic-load acceptance (5-10 orders/s sustained) IS met comfortably.** Burst-acceptance recalibration recommended after A5 ships.

**Operational safety shipped alongside (separate concern, enables safe testing):**
- ✅ NoOp adapters for both `IRobotOrderDispatcher` (POST orders) and `IRiot3OrderQueryService` (reconciler GETs)
- ✅ Composition logger — boot log shows which adapter active
- ✅ `.env.test` template — single command loads all load-test flags safely

### Why P0

Without this, every other improvement is masked by event-publish backpressure. SignalR storms continue. SLA-risk projector lags. Dispatch consumers receive trip events minutes late.

### Files to change

```
src/DTMS.Api/Infrastructure/Outbox/
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

Rewrite the dispatcher loop. Original code at [OutboxProcessorService.cs:79](../src/DTMS.Api/Infrastructure/Outbox/OutboxProcessorService.cs#L79) iterated modules sequentially with `foreach`. New shape:

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

### Step A3 — Tunable options + telemetry (0.5 day) 🟡 core shipped 2026-06-20

**Part 1 — Tunable options (shipped):** all 5 outbox knobs are now `OutboxOptions` properties bound from configuration via `IOptionsMonitor`, hot-reload aware (read per-iteration). Pre-flag hardcoded constants `PollingInterval=5s` and `PublishTimeout=10s` are gone. Boot log surfaces all 5 in one line so the running pod's config is self-documenting.

```csharp
// OutboxOptions (current)
public class OutboxOptions
{
    public bool UseSkipLocked { get; set; } = false;       // A2 part 1
    public int BatchSize { get; set; } = 50;               // A2 part 1
    public int PublishConcurrency { get; set; } = 1;       // A2 part 2
    public int PollIntervalSeconds { get; set; } = 5;      // A3 ✓
    public int PerMessageTimeoutSeconds { get; set; } = 10; // A3 ✓
}
```

**Part 2 — OpenTelemetry counters (pending):** the existing `WorkflowMetrics.SetOutboxPending` is a single aggregated gauge. The per-module breakdown below would change ops' diagnostic from "outbox is X behind, somewhere" to "vendoradapter module is X behind, others are healthy":
- `dtms.outbox.processed` (tag: module, success)
- `dtms.outbox.pending_gauge` (per module)
- `dtms.outbox.dispatch_duration` histogram

Defer this until after A4 — there's no point instrumenting a loop we're about to restructure.

### Step A4 — Parallel per-module loop (NEW, ~1h) 🟢 shipped 2026-06-20

**Surfaced by the 2026-06-20 acceptance run.** With A1+A2+A3 active at `BatchSize=500, PollIntervalSeconds=1, PublishConcurrency=8`, the *theoretical* drain ceiling is `500 × 6 modules / 1s = 3000 events/s`. Observed: ~143 events/s.

Root cause: [`OutboxProcessorService.ProcessUnpublishedEventsAsync`](../src/DTMS.Api/Infrastructure/Outbox/OutboxProcessorService.cs#L59) iterates the 6 module DbContexts **sequentially with `await`**. At full batch + SKIP LOCKED tx + parallel publish + SaveChanges + count, each module's tick is ~500-800ms. 6 × 700ms = ~4.2s per cycle even though the configured poll is 1s. Effective per-module rate ≈ 100-150/s.

**Fix shape:**
```csharp
// Today (sequential):
totalPending += await ProcessModuleAsync(outboxCtx, ..., opts, ct);
totalPending += await ProcessModuleAsync(deliveryorderCtx, ..., opts, ct);
totalPending += await ProcessModuleAsync(planningCtx, ..., opts, ct);
// ... 6 modules × ~700ms = 4.2s wall-clock cycle

// Target (parallel):
var modules = new (DbContext db, string source)[] { (outboxCtx, "outbox"), ... };
var pendingPerModule = await Task.WhenAll(
    modules.Select(m => ProcessModuleAsync(m.db, publisher, m.source, opts, ct)));
totalPending = pendingPerModule.Sum();
// ~700ms wall-clock cycle = ~5× drain throughput
```

**Subtleties to handle:**
- Each `ProcessModuleAsync` already gets its own `DbContext` instance via `scope.ServiceProvider.GetRequiredService<T>()`. Resolutions are thread-safe; the resolved instances are not shared. ✓
- The shared `IPublishEndpoint` is thread-safe per MassTransit guarantees. ✓
- Per-module `Task.WhenAll` means one slow module won't extend the cycle past the slowest module (rather than past the *sum*).
- One module faulting must not cancel the others — wrap each in try/catch and aggregate errors.

**Acceptance:** re-run scenario B at the same tuned config + NoOp; pending ≤ 100 within 30s of test ending. *Result: A4 ship shifted the bottleneck downstream (consumer logic), threshold still not met. See A5 below.*

### Step A5 — Consumer parallelism (NEW, ~2h actual) 🟢 shipped 2026-06-21

**Result**: `DeliveryOrderValidatedConsumer` `ConcurrentMessageLimit` tuned 1 → 4 via `ConsumerDefinition<T>`. Drain rate +33% (67/s → 89/s), Dispatched count +58% at settle window (3,557 → 5,609), p99 latency −12% (96 → 85ms). Modest gain — Postgres row-lock contention on per-order UPDATEs is the real ceiling, not consumer thread availability. Burst threshold "≤100 in 30s at 30 VU peak" remains mathematically unreachable on single host (4,800 events/s create vs 90/s drain) — closes only via multi-replica deployment (Phase D enables this) or workflow refactor. See [`perf-tests/results-2026-06-21/REPORT-a5.md`](../perf-tests/results-2026-06-21/REPORT-a5.md).

**Surfaced by the 5th acceptance run (post Phase A+B+D).** After A1+A2+A3+A4 + B + D all shipped, observed drain rate stayed at ~67-78/s across all configurations. Postgres connection pool fine, container CPU fine, outbox publish fine — the binding constraint is the **per-order consumer logic**.

Each Confirmed event drives `DeliveryOrderValidatedConsumer` through:
`MarkOrderPlanning → CreateJobAnchor → MarkOrderPlanned → DispatchByRoute → MarkOrderDispatched`
= ~5 DB ops + row-lock waits + transaction overhead per order ≈ 30-50ms wall-clock per order per consumer instance.

With MassTransit `ConcurrentMessageLimit = 1` (default), one consumer instance processes ONE order at a time. Throughput ≈ 20 orders/s per consumer. Multiply by the ~3-4 heavy consumers in the system → 60-80 orders/s system-wide — exactly what we measured.

**Fix shape:**
```csharp
// In MassTransit endpoint config (ModuleServiceRegistration.cs or similar)
cfg.ReceiveEndpoint("delivery-order-validated", ep =>
{
    ep.PrefetchCount = 64;            // already 16; raise to 64 for parallel buffer
    ep.ConcurrentMessageLimit = 8;    // ← KEY change: 1 → 8 parallel handlers
    ep.ConfigureConsumer<DeliveryOrderValidatedConsumer>(context);
});
```

**Where to apply:**
- `DeliveryOrderValidatedConsumer` (Planning — heaviest, primary target)
- `Trip*Consumer` (Dispatch — also heavy on DB)
- Projection consumers (lighter, lower priority but still benefit)

**Expected throughput projections:**

| `ConcurrentMessageLimit` | Per-consumer drain | System drain (~3 active consumers) |
|---|---|---|
| 1 (today) | ~20/s | ~60-80/s (measured) |
| 4 | ~60/s | ~180-240/s |
| 8 | ~100/s | ~300-400/s |
| 8 × 3 worker replicas | ~100/s × 3 | ~900-1200/s |

**Subtleties:**
- Row-lock contention amplifies if events for same order cluster (FIFO outbox order). Mitigation: per-handler short-lived transactions (already in place).
- Ordering: ConcurrentMessageLimit > 1 = messages may process out of order. Audit each consumer for ordering assumptions. DTMS has idempotency + state guards (T1 work) so most are safe — projection consumers need careful audit.
- DB connection pressure: 8 parallel × N consumers × N replicas. Phase B PgBouncer absorbs this (transaction-mode multiplexing). ✓

**Acceptance:** re-run scenario B at full tuned + NoOp + Phase D split; pending drain rate ≥ 300/s.

**Note:** Even after A5, the original "≤100 pending in 30s at 30 VU peak burst" threshold likely **still not met** — 30 VU generates ~4,800 events/s, far beyond any single-host single-replica drain rate. Realistic target post-A5 = drain matches **sustained** create rate (100+ orders/s normal traffic). Burst recovery time goes from ~36 min today to ~5-10 min post-A5.

### Acceptance — Phase A 🟡 PARTIAL → CLOSED with caveats (2026-06-20)

**Status:** Phase A code (A1+A2+A3+A4) all shipped + verified safe under load. The original "≤100 pending in 30s" threshold is NOT met at 30 VU burst, but the third acceptance run revealed the bottleneck has moved downstream to consumer DB throughput (Planning/Dispatch consumers processing drained events). A4 (parallel modules) only helps when publish is the bottleneck — under NoOp publish, the bottleneck is the DOWNSTREAM work, not the outbox loop. Phase B (PgBouncer) + Phase D (separate outbox worker container) are the next levers for the burst-acceptance threshold.

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

## Phase B — DB connection pool (P0, 1-2 days) 🟢 SHIPPED 2026-06-20

> **Shipped today**: B1 (Postgres tuning + singleton NpgsqlDataSource shared by 9 DbContexts), B2 (health check on same pool), B3 (PgBouncer transaction-mode pool). Multi-replica connection ceiling REMOVED — `pg_stat_activity` stays at ~46 connections even under 30 VU sustained load + health-probe burst.
>
> **Phase A burst-acceptance threshold ("≤100 pending in 30s") still NOT met after B** — see [`REPORT-phase-b.md`](../perf-tests/results-2026-06-20/REPORT-phase-b.md). Drain rate stayed at ~75/s, identical to pre-B3. Root cause confirmed: **consumer logic throughput** (Planning + Dispatch consumers doing ~5 DB ops per order under row-lock contention) is the binding constraint, not connection acquisition. Phase D (separate outbox worker container with independent resources) is the next lever for that threshold. Phase B + A together ARE sufficient for realistic sustained load (5-10 orders/s).

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

[Program.cs:243](../src/DTMS.Api/Program.cs#L243) opens a raw connection per `/health` call. Replace with EF context probe or use the shared `NpgsqlDataSource`:
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

## Phase D — Outbox worker container (P2, 3-5 days → ~4h actual) 🟢 SHIPPED 2026-06-20

**Goal:** API container scales on request CPU; worker container scales on backlog. Orthogonal scaling axes.

### Why P2 → bumped to immediate after Phase B finding

Phase A made outbox fast; Phase D made it independently scalable. After Phase B closed the connection-pool ceiling, the only remaining "bottleneck moved" candidate was per-container CPU sharing — solved by Phase D. Shipped same day as Phase B once the architecture made it straightforward.

### Shipped approach — same image + env flag (simpler than original spec)

Original spec called for a separate `DTMS.Worker` project. Actual implementation uses the **migrator pattern** that already exists in compose — same Docker image, single flag `Outbox:RunInThisProcess` (default true) gates the `IHostedService` registration. Mirrors the established pattern, avoids code duplication, ships in hours instead of days.

### Files changed

- `src/DTMS.Api/Modules/ModuleServiceRegistration.cs` — conditional `services.AddHostedService<OutboxProcessorService>()` based on flag (IOutboxProcessor singleton stays so `/admin/replay` still works from API)
- `src/DTMS.Api/Adapters/CompositionLogger.cs` — boot log emits `Outbox:RunInThisProcess = True/False` so container role is visible from a single log grep
- `docker-compose.yml` — new `outbox-worker` service using the same `dtms-api` image with `Outbox__RunInThisProcess=true` hardcoded; api default kept true (backwards-compat for plain `docker compose up -d`), overridden to false via `.env.test` for split-load-test config
- `.env.test` / `.env.test.example` — add `Outbox__RunInThisProcess=false`

### Results — Phase D acceptance (5th k6 run today)

| Metric | Phase B (single container) | **Phase D (split)** | Δ |
|---|---|---|---|
| Containers | 1 (api) | **2 (api + outbox-worker)** | architectural |
| API TPS | 608/s | **620/s** | +2% |
| p95 latency | 67.4ms | **60.3ms** | **−10%** |
| **p99 latency (tail)** | 141ms | **95.6ms** | **−32%** ✅ |
| Drain rate (single-host) | ~75/s | ~67/s | same (Postgres-bound) |
| pg_stat_activity peak | 47 | **46** | flat (PgBouncer multiplexes both) |
| RIOT outbound | 0 | **0** 🔒 | flat |

**Tail latency improvement** is the measurable single-host win (api stops competing with OutboxProcessor for CPU/threads). **Drain rate stays the same** on single-host Docker — Postgres remains the bottleneck. The TRUE Phase D benefit is in production with separate VMs/nodes: independent scaling (`docker compose up --scale outbox-worker=3`), failure isolation, independent deploy.

See [`perf-tests/results-2026-06-20/REPORT-phase-d.md`](../perf-tests/results-2026-06-20/REPORT-phase-d.md) for full analysis.

### MVP scope + known follow-up

**🟢 Track C closed 2026-06-22** ([2b09928](https://github.com/Tuinuihappy/DTMS/commit/2b09928)) — 7 hosted services (5 vendor pollers + 2 downstream broadcasters) now gated under `Workers:VendorPollers:RunInThisProcess`. Default true keeps api behaviour; outbox-worker container sets false. Measured impact: worker RIOT API calls 60/min → 0/min (50% vendor load reduction). Idempotent services (PlanningReconciliation, SlaRisk, FleetUtilization, TopologyOverlayExpiry, InfraHealthPoller) intentionally NOT gated — DB-only, safe to run 2×.

Remaining Phase D follow-ups: leader election for multi-replica api (only matters once K8s scales api > 1 replica — not a single-host docker-compose concern); pre-existing worker masstransit-bus Degraded endpoints (TripDropCompletedOmsNotify + TripStartedOmsNotify) — tracked as separate infra debt.

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

## Phase F — SignalR backplane + projector consolidation (P1, 2 days) — F1 🟢 shipped 2026-06-22

**Goal:** Multiple API replicas can deliver SignalR to any client; redundant projector pushes eliminated.

### Step F1 — Redis backplane — 🟢 shipped 2026-06-22 (~1.5h actual)

Flipped the flag (`SignalR__UseRedisBackplane=true` in `.env.test`), added env passthrough in docker-compose for both `api` and `outbox-worker` services, scaled api to 2 replicas via new `docker-compose.scale.yml` overlay (uses `!reset` YAML tag to clear `container_name` + host port mapping). Verified fan-out end-to-end:

- 2 headless SignalR clients (`dtms-bp-client-a` + `dtms-bp-client-b`) connected via Docker DNS round-robin to api:8080
- Drain triggered on `dtms-api-1` ONLY
- Both clients received `__drain` event at the **identical timestamp** (06:56:37.683Z) — ~9ms end-to-end including Redis pubsub roundtrip
- Redis showed 30+ `dtms:sr*` pubsub channels (one per hub × 2 machine IDs = api + worker both participating)

Single-replica regression check passed before scaling — G1 headless client connected + subscribed + observed VendorHealthChanged events flowing through the backplane.

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

This is the same pattern [DashboardCounterBatcher](../src/DTMS.Api/Realtime/DashboardCounterBatcher.cs) uses with its 250ms coalescing window — extend that pattern to OrderHub / JobHub / TripHub.

### Step F3 — Rate limiter exclusions — 🟢 shipped 2026-06-22 ([4828232](https://github.com/Tuinuihappy/DTMS/commit/4828232))

Bypass added at [Program.cs](../src/DTMS.Api/Program.cs) — `/health`, `/hubs`, `/metrics` skip the per-IP partitioned rate limiter via `GetNoLimiter("bypass-infra")`. Business paths (`/api/*`) still rate-limit per IP unchanged.

Verified with curl spam test (PermitLimit=5/60s):
- `/health/ready` × 30 → 30 ok / **0 × 429** (bypass working)
- `/api/v1/delivery-orders` × 30 → 5 ok / **25 × 429** (limit still active)

Critical prerequisite for K8s rollout — without F3 the G1 drain protocol's reconnect burst would trip 429 storms on NAT-egress clients, defeating G1's whole purpose.

### Step F3 — Rate limiter exclusions (original plan, 15 min)

[Program.cs:317-336](../src/DTMS.Api/Program.cs#L317) — exclude `/health`, `/health/ready`, `/hubs`, `/metrics`:

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

> **G2 partial credit (2026-06-22, [f920151](https://github.com/Tuinuihappy/DTMS/commit/f920151))** — `dtms.workflow.shutdown_duration_seconds` histogram (phase=bus|total) emitted via the existing OTLP pipeline + structured logs. Phase G adds the Prometheus scrape exporter + Grafana panel that turns the metric into a dashboard + alert, but the data itself is already flowing into the OTel collector today. See [crash-recovery-workflow-resilience-plan.md §11 G2](crash-recovery-workflow-resilience-plan.md).

### Files

```
docker-compose.yml                                # add prometheus + grafana services
infra/prometheus.yml                              # NEW: scrape config
infra/grafana/dashboards/dtms-overview.json       # NEW: starter dashboard
src/DTMS.Api/Program.cs           # add OpenTelemetry .NET metrics export
```

### Steps

1. Add Prometheus + Grafana to compose.
2. Add `OpenTelemetry.Exporter.Prometheus.AspNetCore` to API.
3. Build a starter dashboard with: HTTP p50/p95/p99, outbox lag, EF query duration, GC pause times, ThreadPool starvation, SignalR connection count, **shutdown phase distribution** (G2 data already flowing).
4. Add alert rules: p95 > 500ms for 5 min, outbox pending > 10k for 2 min, error rate > 1% for 5 min, **P95(shutdown_duration_seconds{phase="total"}) > 60s over 7d**.

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
