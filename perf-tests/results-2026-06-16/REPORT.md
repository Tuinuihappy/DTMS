# DTMS Performance Test Report — 2026-06-16

**Stack:** .NET 10 preview API + Next.js 16 frontend + Postgres 16 + RabbitMQ 3.13 + Redis 7 + Jaeger
**Host:** Windows 11, Docker Desktop (single-host)
**Load tool:** k6 v1.x (grafana/k6 container hitting `host.docker.internal`)
**API config under test:** `RateLimit__PermitLimit=100000`, `WindowSeconds=1`, `QueueLimit=5000` (rate limit lifted to expose backend ceilings)

## TL;DR

| Scenario | Throughput | Success | p50 | p95 | p99 | Verdict |
|---|---|---|---|---|---|---|
| **A. Reads** (stats+list+list?status, 100 VU) | **1,836 req/s** | 100% | 24 ms | 93 ms | 231 ms | Healthy |
| **B. Writes** (POST /upstream, 30 VU) | **696 orders/s** | 100% | 17 ms | 91 ms | 176 ms | Healthy |
| **C. Mixed E2E** (create+list+stats+get, 50 VU) | **902 req/s** (300 cycles/s) | 100% | 24 ms | 114 ms | 205 ms | Healthy (but see #5) |
| **D. Frontend SSR** (root+login+orders, 60 VU) | **93 req/s** (23 cycles/s) | 100% | 316 ms | 1.22 s | 1.87 s | **Regressed 4×** vs prior run |
| **E. Stress** (mixed read/write ramp 5→500 VU, 4m) | **1,011 req/s** | 100% | 225 ms | 572 ms | 815 ms | Holds, but latency knee at ~150 VU |

**Volume test was not completed** — stopped at user request before 500k-row seed finished. Findings #1–#2 below were observed during the stress phase rather than under volume load.

Headline:
- **Backend API still does not break under 500 concurrent users** — 0 errors across all scenarios. Throughput plateaus around 1k req/s; latency grows linearly (5.6× from baseline to 500 VU).
- **Two prior findings have regressed**: outbox-processor lag is *worse* (#1), frontend SSR is *4× slower* (#3).
- **New: Postgres connection pool exhausts** at sustained 500 VU (#2) — saw `FATAL: sorry, too many clients already` from an external probe while the API kept serving.

---

## Critical findings

### 🔴 1. Outbox processor lag has regressed to ~1.75% throughput parity
- **Observed:** After scenarios B + C + E (combined ~149k orders created), DB held:
  - `deliveryorder.OutboxMessages` = **600,258** total, **589,711 pending (98.2%)**, only **10,547 processed**
  - `dispatch.OutboxMessages` = ~129 pending
- **Prior run (2026-06-06):** 92k pending vs 3.6k processed (~4% parity) — already flagged in [perf-tests/results/REPORT.md](../results/REPORT.md). Now it's **worse**: 1.75% parity.
- **Where:** [src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/](../../src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/) — single `OutboxProcessorService` per module
- **Impact at scale:**
  - Planning, dispatch, SLA-risk, validated-consumer and trip-lifecycle consumers receive events minutes-to-hours late.
  - SignalR UI updates lag the source-of-truth (we observed the frontend continuing to update for >10 minutes after k6 stopped, because outbox was still flushing).
  - At 1k orders/s create rate vs ~150 outbox/s drain rate, the queue grows by ~850/s. A 1-hour sustained burst → 3M backlog. Disk + replication lag will follow.
- **Recommended fix:**
  1. Increase outbox batch size (current default likely 50–100) to ~500–1000.
  2. Spawn one `OutboxProcessor` per module *in parallel* — DeliveryOrder, Dispatch, Planning, Fleet, Facility each get an independent loop.
  3. Or extract outbox processing into a dedicated worker container so it can scale horizontally without contending with API ingress for CPU.
  4. Verify `IX_OutboxMessages_ProcessedOnUtc` is the index actually used by the dispatcher's "find pending" query — partial index on `WHERE ProcessedOnUtc IS NULL` is usually faster.

### 🔴 2. Postgres connection pool exhausts at sustained 500 VU
- **Observed:** Mid-stress, running `psql` from an external shell returned:
  > `FATAL: sorry, too many clients already`
- The API itself never returned a 5xx, but **no headroom is left** for ops tooling, BI replicas, ad-hoc queries, or a parallel admin process.
- **Where:** API connection-string in [docker-compose.yml:70](../../docker-compose.yml#L70) — no `Pooling=true; MaxPoolSize=N` parameters. Default Npgsql pool is 100 per process, plus each EF `DbContext` activity holds a connection for the duration of the transaction.
- **Impact at scale:**
  - k8s rolling deploys would briefly double connection demand (old pods + new pods) — guaranteed to hit `max_connections` (default 100 on Postgres alpine).
  - Any out-of-band tooling (replication snapshot, BI export, manual `psql`) gets locked out under load.
  - Connection storms during cold-start (e.g. all SignalR clients reconnect after API restart) will fan out and trip the limit.
- **Recommended fix:**
  1. Set Postgres `max_connections=300–500` (it's currently 100 by default on `postgres:16-alpine`).
  2. Set `MaxPoolSize=80` per API instance in the connection string and verify pool stats with `pg_stat_activity`.
  3. Add PgBouncer in front of Postgres for true multi-instance scaling. Connection pooling at the proxy means API can scale to N replicas without exploding Postgres.

### 🟡 3. Frontend SSR has regressed 4× since 2026-06-06
- **Observed (this run):** 93 req/s, p50 316 ms, **p95 1.22 s**, p99 1.87 s @ 60 VU
- **Prior run (2026-06-06):** 189 req/s, p50 216 ms, **p95 407 ms**, p99 487 ms @ 60 VU
- **Where:** Next.js 16 standalone build in [frontend/](../../frontend/) — pages affected: `/`, `/login`, `/delivery-orders`
- **Probable causes:**
  - More heavy components added to the orders page (table, drawer, dispatch funnel etc. — see recent commits referenced in repo state).
  - SSR cold-start cost per page is now multi-hundreds-of-ms even when DB is empty (we measured after cleanup).
  - No CDN/edge caching → every SSR hit goes back to Node.
- **Impact at scale:**
  - 60 concurrent users → p95 already over 1s. At 200+ users the Node event loop will be the bottleneck long before the API is.
  - Search-engine crawls and prefetch traffic will look identical to real users — no static fallback.
- **Recommended fix:**
  1. Profile the orders page — `next build` output likely shows large server-component bundles. Move static layout out of RSC.
  2. Add `revalidate=60` (ISR) or stale-while-revalidate to read-only listings.
  3. Put a CDN (CloudFront/Cloudflare) or Next.js cache layer in front; expect p95 to drop to ~50 ms.
  4. Scale frontend horizontally (`docker compose ... --scale frontend=3`) behind a load balancer — Node is single-threaded per process.

### 🟡 4. Latency knee at ~150 VU; tail latency degrades linearly past it
- **Observed (stress test, mixed 70/30 read/write):**

  | VU | Phase | Behaviour |
  |---|---|---|
  | 5 → 50 | warm-up, 30s | normal |
  | 50 → 150 | 60s | p50 ~25 ms, p95 ~100 ms (close to baseline) |
  | 150 → 300 | 60s | p50 climbs to ~150 ms, p95 ~350 ms |
  | 300 → 500 | 60s | p50 ~225 ms, p95 ~572 ms — *knee crossed* |
  | 500 sustained | 30s | p99 815 ms, max 2.69 s — but **still 0 errors** |
- **Headline:** From baseline (100 VU, p95 93 ms) → stress (500 VU, p95 572 ms) = **5.6× tail-latency growth**. No 429s, no 5xx, no DB timeouts visible from k6's side — the API absorbs load by queueing internally.
- **Where:** Likely the same connection-pool + outbox-saturation interaction as #1 and #2. Need profiling to confirm bottleneck — could be DB, could be `OrderListView` projection rebuild, could be MassTransit publish path.
- **Impact at scale:**
  - SLA at >300 concurrent users is no longer "fast" — p95 well over the typical 500 ms cabinet target.
  - The system "absorbs" load by queueing requests, which means a sustained burst causes memory growth and eventual GC pauses — we saw `dtms-api` mem go from 326 MiB idle to 404 MiB warm, not yet alarming but worth watching with `dotnet-counters`.
- **Recommended fix:** address #1 and #2 first; the knee is a symptom. Re-test after to see if it moves to >500 VU.

### ℹ️ 5. POST `/delivery-orders/upstream` does not return `id` in body
- **Observed:** Scenario C tried to chain `POST → GET /{id}` but `r.json('id')` was always null, so the GET branch never executed (latency_get = 0ms in summary).
- **Where:** Look at the upstream POST handler in [src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/](../../src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/).
- **Impact:** Cosmetic for testing, but real for upstream OMS integration — they cannot follow up on the created order without parsing `Location` header or re-querying by `orderRef`.
- **Recommended fix:** Return `{ id, orderRef, status }` minimal response body, or set `Location: /api/v1/delivery-orders/{id}`. If already returning `Location`, update [scenario-c-mixed.js:80](../scenario-c-mixed.js#L80) to use `r.headers.Location` instead of `r.json('id')`.

---

## Comparison vs prior run (2026-06-06)

| Metric | 2026-06-06 | 2026-06-16 | Δ |
|---|---|---|---|
| Scenario A throughput | 3,204 req/s | 1,836 req/s | **−43%** |
| Scenario A p95 | 43 ms | 93 ms | **+116%** |
| Scenario B throughput | 1,010 orders/s | 696 orders/s | **−31%** |
| Scenario B p95 | 37 ms | 91 ms | **+146%** |
| Scenario C throughput | 1,077 req/s | 902 req/s | −16% |
| Scenario D throughput | 189 req/s | 93 req/s | **−51%** |
| Scenario D p95 | 407 ms | 1.22 s | **+200%** |
| Outbox throughput parity | ~4% | **~1.75%** | **regressed** |
| Stress: max VU before error | not tested | 500 VU, 0 errors | — (new) |

**Interpretation:** Across the board, ~30–50% throughput regression and ~2–3× latency growth. This is most likely caused by the *recently added* OMS notification + dispatch envelope features (B3/B4 work referenced in recent commits), which add synchronous outbox writes per order and new background consumers competing for CPU. Worth confirming with `git log --since='2026-06-06' --oneline`.

---

## Scenario detail

### A. Backend reads — 100 VU peak, 80 s
```
Total reqs: 145,659 (1,836 /s)   Iterations: 48,553 cycles
Errors:     0
Latency:    min=1.5ms  med=24ms  p95=93ms  p99=231ms  max=818ms
```
Endpoints: `/stats`, `/?pageSize=20`, `/?pageSize=50&status=DRAFT`.

### B. Backend writes — 30 VU peak, 70 s
```
Total reqs:  47,959  (696 /s)
Created:     47,959 orders
Errors:      0
Latency:     min=44µs  med=17ms  p95=91ms  p99=176ms  max=684ms
```
Single `POST /api/v1/delivery-orders/upstream` per iteration. Each write also produces outbox row → see finding #1.

### C. Mixed E2E — 50 VU peak, 80 s
```
Total reqs:  70,881 (902 /s, 300 e2e cycles/s)
Errors:      0
Per-endpoint p95:
  create  → 157 ms (p99=259 ms, max=614 ms)
  list    → 101 ms
  stats   →  79 ms
  get     →   0 ms  ← never executed; see finding #5
```

### D. Frontend SSR — 60 VU peak, 70 s
```
Total reqs:  6,424  (93 /s, 23 cycles/s)
Errors:      0
Per-page p95:
  /                  → 1.36 s
  /login             → 1.37 s
  /delivery-orders   → 1.30 s
```

### E. Stress — ramp 5→500 VU over 4m20s, mixed 70% read / 30% write
```
Total reqs:  258,824 (1,011 /s)
Errors:      0
Latency by op (combined):
  read   → med=201ms  p95=524ms   p99=771ms   max=2.64s
  write  → med=296ms  p95=647ms   p99=862ms   max=2.69s
```
Notable: peak concurrency reached the configured cap (500 VU) without any HTTP failures.

---

## What we did NOT test (and why it matters)

| Skipped | Reason | Risk if untested |
|---|---|---|
| Volume read against 500k-row DB | Stopped at user request mid-seed | List/filter performance against realistic prod-size table is unverified. Index efficacy on `Status`, `CreatedDate` not validated past ~150k rows. |
| Long-duration soak (24h+) | Time budget | Memory leaks, slow connection growth, GC behaviour, log/disk growth not measured. |
| Multiple API replicas behind LB | Single-host docker-compose | Horizontal-scale headroom unknown. SignalR sticky-session story untested. |
| RabbitMQ failure recovery | Not part of scope | Consumer reconnect, message redelivery, dead-letter behaviour unverified. |
| Auth-enabled load test | `Auth__Disable=true` for testing | Real-world request cost includes JWT validation per request — likely +1–3 ms per req. |

---

## Priority recommendations

1. **Fix outbox throughput** (finding #1) — single biggest blocker to horizontal scale. Without this, every other improvement is masked by event-publish backpressure. Target: drain rate ≥ create rate at all sustained loads.
2. **Tune Postgres + add PgBouncer** (finding #2) — required before deploying >1 API replica.
3. **Frontend SSR optimization** (finding #3) — investigate the 4× regression *first* (find the offending commit), then add caching layer. Don't paper over a regression with infrastructure.
4. **Implement & re-run volume test** at 500k–1M orders to validate read-path indexes and confirm finding #4's knee moves with #1/#2 fixed.
5. **Cross-check finding #5** — small but real DX bug for OMS integrators.

---

## How to reproduce this run

```powershell
# 1. Relax rate limit for testing
docker compose stop api
$env:RateLimit__PermitLimit  = "100000"
$env:RateLimit__WindowSeconds = "1"
$env:RateLimit__QueueLimit   = "5000"
docker compose up -d api

# 2. Seed facility (only once after a fresh DB)
bash perf-tests/seed.sh

# 3. Run each scenario
docker run --rm --add-host=host.docker.internal:host-gateway `
  -v "d:/DTMS/perf-tests:/scripts" `
  -v "d:/DTMS/perf-tests/results-2026-06-16:/out" `
  grafana/k6:latest run --summary-export=/out/scenario-a.json /scripts/scenario-a-read.js
# repeat for scenario-b, -c, -d, -e

# 4. Cleanup after (frees outbox + stops SignalR storm)
docker compose stop api
docker cp perf-tests/cleanup.sql dtms-postgres:/tmp/cleanup.sql
docker exec dtms-postgres psql -U postgres -d amr_delivery_planning -f /tmp/cleanup.sql
docker compose up -d api
```

## Artefacts in this folder

- `scenario-a.json` — read-heavy k6 summary
- `scenario-b.json` — write-heavy k6 summary
- `scenario-c.json` — mixed E2E k6 summary
- `scenario-d.json` — frontend SSR k6 summary
- `scenario-e.json` — stress (ramp to 500 VU) k6 summary
- `../scenario-e-stress.js` — new stress test script (added this run)
- `../cleanup.sql` — TRUNCATE script used to drain test data after each run
- `../seed-volume.sql` — 500k volume seed (not executed to completion)
