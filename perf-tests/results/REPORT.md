# DTMS Performance Test Report

**Date:** 2026-06-06
**Stack:** .NET 10 preview API + Next.js 16 frontend + Postgres 16 + RabbitMQ 3.13 + Redis 7
**Host:** Windows 11, Docker Desktop
**Load tool:** k6 v1.x (grafana/k6 docker image)
**Test driver:** k6 container hitting `host.docker.internal`

## TL;DR

| Scenario | Throughput | Success | p50 | p95 | p99 | Verdict |
|---|---|---|---|---|---|---|
| A. Backend reads (stats+list+list?status) | **3,204 req/s** | 100% | 18 ms | 43 ms | 64 ms | Excellent |
| B. Backend writes (POST /upstream) | **1,010 orders/s** | 100% | 17 ms | 37 ms | 53 ms | Excellent |
| C. Mixed E2E (create+list+stats+get) | **1,077 req/s** (269 cycles/s) | 100% | 22 ms | 79 ms | 152 ms | Excellent |
| D. Frontend SSR pages (root+login+dashboard) | **189 req/s** (47 iter/s) | 100% | 216 ms | 399 ms | 487 ms | Good (SSR cold-ish at 60 VU) |

Backend API holds up well under heavy synchronous load (~1k orders/s on a single Postgres + single API container). Frontend SSR is bound by Node single-threaded render — p95 ~400 ms at 60 concurrent users is reasonable for Next.js standalone with no CDN/edge caching in front.

## Critical findings

### 🔴 1. `/health` endpoint blocks on RIOT3 vendor probe (5 s timeout)
- **Where:** [src/AMR.DeliveryPlanning.Api/Program.cs:164-190](../src/AMR.DeliveryPlanning.Api/Program.cs#L164)
- **What happened:** Initial Scenario A had 64% errors on `/health` because each call pings RIOT3 (`10.204.212.28:12000`) with a 5 s timeout. Under load, every health request blocked for 5 s.
- **Impact:** k8s liveness/readiness probes will fail-flap whenever RIOT3 is unreachable, restarting healthy pods. Same for any load balancer health check.
- **Fix:** Move the `riot3` check off `/health` to `/health/vendors` only (already a separate endpoint). Keep `/health` to `self+postgres+redis+rabbitmq`. Or wrap vendor checks with `tags: ["vendors"]` and configure the default predicate to exclude them — partially in place, but `/health` (no predicate) still runs all checks.

### 🔴 2. Global rate limiter blocks legitimate burst traffic (100 req/min/IP)
- **Where:** [src/AMR.DeliveryPlanning.Api/Program.cs:192-206](../src/AMR.DeliveryPlanning.Api/Program.cs#L192)
- **What happened:** Initial load test got 99.9% HTTP 429. All k6 traffic shared one IP partition (100 permits/min) with `QueueLimit=5`.
- **Impact:** A burst of >100 req/min from one IP (e.g. a single workflow engine, vendor webhook, or batched UI fetch) gets 429. Bulk submit, autoplan webhook callbacks, and frontend SSR-on-demand pages can trip this.
- **Fix applied for tests:** Externalised to env vars `RateLimit__PermitLimit / WindowSeconds / QueueLimit`. Default remains `100 req/min` — recommend raising to e.g. `1000 req/min` and adding a per-user / per-API-key partition rather than per-IP.

### 🟡 3. Outbox processor falls behind under sustained write load
- **Observed:** After scenarios B + C, DB had 92,179 pending outbox messages and only 3,652 processed (~4% throughput parity).
- **Where:** [OutboxProcessorService](../src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/) (single background service)
- **Impact:** Downstream consumers (planning, dispatch) get events minutes late. SLA risk service, validated consumer, and trip lifecycle consumers all lag.
- **Fix:** Increase outbox batch size, add concurrent workers (one per module), or move outbox publish to a dedicated worker container that horizontally scales.

### 🟡 4. AutoPlan cascade-fails when no OrderTemplate registered
- **Observed:** All 92k loaded orders moved `Planned → Failed` because route `WH-01 → BAY-A` had no active OrderTemplate.
- **Where:** [src/Modules/Dispatch/...](../src/Modules/Dispatch/) — `EnvelopeDispatch` falls back to legacy path then both fail.
- **Impact:** Real prod issue if an OrderTemplate is missing or deactivated, you get a thundering herd of failed orders.
- **Fix:** Block order creation (or hold to `VALIDATED` only) when no template covers the route — fail fast at the boundary rather than after planning.

## Baseline (single-thread, warm)

| Endpoint | min | typical |
|---|---|---|
| `GET /api/v1/delivery-orders/stats` | 7 ms | ~10 ms |
| `GET /api/v1/delivery-orders?pageSize=20` | 10 ms | ~15 ms |
| `GET /` (frontend) | 26 ms | ~32 ms |

## Scenario detail

### A. Backend reads — 100 VU peak, 80 s

```
Total reqs: 256,329  (3,204 /s)
Errors:     0
Latency:    min=1.5ms  med=18ms  p95=43ms  p99=64ms  max=154ms
Iterations: 85,443 cycles (each = 3 GETs)
```

Endpoints exercised: `/stats`, `/?pageSize=20`, `/?pageSize=50&status=DRAFT`.

Container peak CPU during run:
- `dtms-api`: 577% (multi-core saturated)
- `dtms-rabbitmq`: 478% (heartbeats + bus traffic)
- `dtms-postgres`: 342%
- `dtms-redis`: 17%

### B. Backend writes — 30 VU peak, 70 s

```
Total reqs:  70,733   (1,010 /s)
Created:     70,733 orders
Errors:      0
Latency:     min=5.5ms  med=17ms  p95=37ms  p99=53ms  max=106ms
```

Single `POST /api/v1/delivery-orders/upstream` per iteration. Synchronous API path: persist DeliveryOrder + Item + write OrderAuditEvent + outbox row. ~1k orders/s with low tail latency.

Note: AutoPlan + outbox processing runs **async** in background and was not measured here — see findings #3 and #4.

### C. Mixed E2E — 50 VU peak, 80 s

```
Total reqs:  86,184 (1,077 /s, 269 e2e cycles/s)
Errors:      0
Per-endpoint p95:
  create  →  97 ms   (p99=185 ms, max=687 ms)
  list    →  89 ms
  stats   →  50 ms
  get     →  54 ms
```

Each cycle: `POST /upstream → GET /list → GET /stats → GET /{id}`.
Create latency tail (max=687 ms) suggests occasional Postgres write contention; nothing failed.

### D. Frontend — 60 VU peak, 70 s

```
Total reqs:  13,244 (189 /s, 47 cycles/s)
Errors:      0
Per-page p95:
  /                     → 407 ms
  /login                → 407 ms
  /delivery-orders      → 407 ms
```

Next.js standalone mode, no reverse-proxy cache. Latency dominated by SSR cost; all three pages converge to the same p95 because the Node event loop is the bottleneck.

## Resource utilisation snapshot

End-of-test (idle, no load):

| Container | CPU | Memory |
|---|---|---|
| dtms-api | 0.45 % | 326 MiB |
| dtms-postgres | 0.06 % | 326 MiB |
| dtms-rabbitmq | 0.19 % | 146 MiB |
| dtms-redis | 0.46 % | 5 MiB |
| tms-frontend | 0.00 % | 257 MiB |

Peak CPU during write scenario (from `docker stats` polled every 2 s):
- dtms-api: 525 %
- dtms-postgres: 401 %
- dtms-rabbitmq: 377 %
- dtms-redis: 13 %

## Recommendations (priority order)

1. **Decouple `/health` from vendor probes** — must-fix before deploying to k8s (#1).
2. **Reconsider rate limiter strategy** — per-IP+100/min is too aggressive for multi-tenant or webhook-heavy workloads. Switch to per-API-key, raise default to 1000/min, or implement tiered limits (#2).
3. **Scale outbox processing horizontally** — add `OutboxProcessor` parallelism or worker container to keep up with API write rate (#3).
4. **Fail-fast on missing OrderTemplate** at create-time, not planning-time (#4).
5. **Add a CDN / edge cache or Next.js cache layer** for read-only pages — frontend p95 can drop from 400 ms to ~50 ms.

## Test artefacts

- `perf-tests/scenario-a-read.js` — read-heavy script
- `perf-tests/scenario-b-write.js` — write-heavy script
- `perf-tests/scenario-c-mixed.js` — mixed E2E script
- `perf-tests/scenario-d-frontend.js` — frontend SSR script
- `perf-tests/seed.sh` — facility seed (map + 3 stations + carrier + load-unit)
- `perf-tests/results/scenario-{a,b,c,d}.json` — k6 summary exports
- `perf-tests/results/docker-stats*.csv` — per-3s container CPU/mem during runs

## How to re-run

```powershell
# 1. Lift rate limit for testing
$env:RateLimit__PermitLimit = "100000"
$env:RateLimit__WindowSeconds = "1"
$env:RateLimit__QueueLimit = "5000"
docker compose --profile prod up -d api

# 2. Seed facility (only once after a fresh DB)
bash perf-tests/seed.sh

# 3. Run a scenario
docker run --rm `
  -v "d:/DTMS/perf-tests:/scripts" `
  -v "d:/DTMS/perf-tests/results:/out" `
  grafana/k6:latest run --summary-export=/out/scenario-a.json /scripts/scenario-a-read.js
```
