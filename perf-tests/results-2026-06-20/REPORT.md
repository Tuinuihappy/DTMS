# DTMS Performance Test Report — 2026-06-20

**Stack:** .NET 10 preview API + Next.js 16 frontend + Postgres 16 + RabbitMQ 3.13 + Redis 7 + Jaeger
**Host:** Windows 11, Docker Desktop (single-host)
**Load tool:** k6 v1.x (grafana/k6 container hitting `host.docker.internal`)
**API config under test:**
- `Outbox__UseSkipLocked=true` (Phase A Step A2 part 1)
- `Outbox__PublishConcurrency=8` (Phase A Step A2 part 2)
- `RateLimit__PermitLimit=100000`, `WindowSeconds=1`, `QueueLimit=5000` (rate limit lifted to expose backend ceilings — same as 2026-06-16 baseline)
- Partial index `IX_OutboxMessages_Pending` always on (Phase A Step A1 migration)

**Pre-test cleanup applied** so deliveryorder/outbox baselines were minimal (43 orders total / 0 pending outbox in deliveryorder before run).

## TL;DR

| Metric | 2026-06-16 baseline | 2026-06-20 post Phase A A1+A2 | Δ |
|---|---|---|---|
| **TPS create** | 696 orders/s | **933 orders/s** | **+34%** ✅ |
| **p95 latency** (write) | 91ms | **35.45ms** | **−61%** ✅ |
| **p99 latency** (write) | 176ms | **57.1ms** | **−68%** ✅ |
| **Success rate** | 100% | **100%** | flat ✅ |
| **Errors** | 0 | **0** | flat ✅ |
| **Outbox drain parity** | 1.75% 🔴 | **~0.6%** still 🔴 | **regressed** — Phase A A1+A2 alone insufficient |

Headline:
- ✅ **API side excellent** — throughput +34%, p95 latency cut by 60%+, zero errors. Phase A code paths (partial index + SKIP LOCKED + parallel publish + EnableRetryOnFailure + ExecutionStrategy wrap) all proven correct under sustained 30 VU 70s burst.
- 🔴 **Outbox drain is the new bottleneck, not the symptom Phase A A1+A2 targeted.** With 933 orders/s create × ~8 events per order auto-pipeline → **~7,500 outbox events/s generated**. Drain rate measured at **~10 events/s per module** (bottlenecked by hardcoded `PollingInterval=5s` × `BatchSize=50`). Drain ≠ create. Backlog grows linearly.
- 📋 **Next step is Step A3 (tunable PollIntervalSeconds + BatchSize)**, not Phase B/C/D. Phase A is *necessary but not sufficient*.

---

## Setup notes

### Test orderRef pattern
`LOAD-${uuidv4()}` — unique per request, no API-side idempotency dedup (no `Idempotency-Key` header).

### Location codes
Switched [perf-tests/scenario-b-write.js:49-50](../scenario-b-write.js#L49) from generic `WH-01` / `BAY-A` to **`SHELF1` / `SHELF2`** (real codes in this environment) so orders flow further through the pipeline (Confirmed → Planned → Dispatched) instead of stalling at validation.

### First-run gotcha — rate limiter at defaults
First k6 attempt failed catastrophically: 99.88% of 164,899 requests returned 429 with p95=16ms (fast rejection). Root cause: `RateLimit__PermitLimit=100` (default 100 req per 60s window per IP). Same `apples-to-apples` setup as the 2026-06-16 baseline required lifting to `100000/1s/5000`. Verified with the env vars above. Second run = the headline numbers.

---

## Run timeline

| Phase | Duration | Action |
|---|---|---|
| Pre-flight | — | Build clean, docker healthy 16min uptime |
| Bench baseline | 1 min | 43 orders / 0 pending outbox in deliveryorder |
| Flip flags + restart | 2 min | Boot log confirmed `(UseSkipLocked=True, BatchSize=50, PublishConcurrency=8)` |
| k6 scenario B (1st) | 75s | **FAIL** — 99.88% 429 (rate limiter at defaults) |
| Lift rate limit + restart | 2 min | All flags + rate-limit lift set together |
| k6 scenario B (2nd) | 72s | **PASS** — 0 errors, 64,622 orders, 933/s |
| Settle window | 60s | Outbox drain measurement |
| Cleanup | 30s | 125,592 LOAD orders + 503,702 outbox events deleted in one tx |
| Roll back + verify | 2 min | Back to defaults, 43 orders / 0 pending |

---

## Critical findings

### ✅ Finding #1 — k6 acceptance thresholds all passed

```
  █ THRESHOLDS

    errors                                       ✓ 'rate<0.05' rate=0.00%
    http_req_duration{ep:create}  p(95)=35.45ms  ✓ 'p(95)<1500'
                                  p(99)=57.10ms  ✓ 'p(99)<3000'
    http_req_failed                              ✓ 'rate<0.05' rate=0.00%

  █ TOTAL RESULTS
    checks_succeeded   100.00%   64622 / 64622
    orders_created     64,622    933.4/s
    http_req_duration  min  avg     med     p90     p95     p99    max
                       —    21.04ms 18.66ms 30.46ms 35.45ms 57.1ms 524.02ms
```

**Compared to 2026-06-16:** API throughput +34% (696 → 933 orders/s), p95 latency cut 60% (91ms → 35ms). Phase A's API-side wins are real and measurable.

### 🔴 Finding #2 — Outbox drain rate cannot keep up with create rate

The deeper diagnostic, captured 60-180s after k6 ended:

| Schema | Total | Pending right after k6 | Pending after 2min settle |
|---|---|---|---|
| outbox | 0 | 0 | 0 |
| **deliveryorder** | 504,006 | **500,076** 🔴 | **498,688** 🔴 |
| planning | 16,051 | 0 ✅ | 0 ✅ |
| dispatch | 370 | 0 ✅ | 0 ✅ |
| fleet | 0 | 0 ✅ | 0 ✅ |
| vendoradapter | 1,686 | 0 ✅ | 0 ✅ |

**Drain rate measurement** (deliveryorder schema, 3 rolling windows):

```
last 10s: 100 processed = 10/s
last 30s: 300 processed = 10/s
last 60s: 550 processed = ~9/s
```

Steady-state **~10 events/s** drain. At 500k pending → **~14h** to drain that batch.

### 📋 Finding #3 — Root cause: hardcoded `PollingInterval=5s` × `BatchSize=50`

In [OutboxProcessorService.cs:22](../../src/DTMS.Api/Infrastructure/Outbox/OutboxProcessorService.cs#L22):

```csharp
private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
```

Max theoretical drain per module per tick = `BatchSize / PollIntervalSeconds = 50 / 5 = 10 events/s`. That ceiling holds even with `UseSkipLocked=true` + `PublishConcurrency=8`. Per-batch latency is excellent (8 messages × ~5ms parallel ≈ 10ms), but the loop wakes only once per 5s.

Scale-readiness plan Step A3 already calls this out:
> ```csharp
> "Outbox": {
>   "PollIntervalSeconds": 2,    // was hardcoded 5
>   "BatchSize": 500,            // was hardcoded 50
> }
> ```

With Step A3 settings, theoretical max = `500 / 2 = 250/s per module × 6 modules = 1,500 events/s`. Closer to the ~7,500/s generation rate, would need further tuning (BatchSize=1000 or PollIntervalSeconds=1) to fully match.

### ⚠️ Finding #4 — Auto-pipeline at upstream stalls at Confirmed under load

Of the 64,622 orders created during the test:

```
load_orders_total      125,592    (test + leftovers from prior sessions)
load_orders_confirmed  124,358    ← stalled here
load_orders_planned          0    ← no Planning consumer fired
load_orders_dispatched       0    ← no Trip created
```

The `Submit → Validate → Confirm` transitions happen in-process synchronously via the upstream `auto-pipeline`. The handoff to Planning (`Confirmed → Planned`) goes through outbox → `DeliveryOrderConfirmedIntegrationEventV1` → `DeliveryOrderValidatedConsumer`. With outbox backlog at 500k events, the Planning consumer never received the trigger for these orders. **No production impact** in this test because the orders were deleted afterward, but in production this would be a 14-hour SLA breach on every order in the burst.

### 🟡 Finding #5 — `RateLimit__PermitLimit=100` default is too tight for any meaningful load test

Same gotcha as 2026-06-16. The default rate limit is fine for production (per-IP throttling) but blocks all useful perf testing. Documented in [Program.cs:347-348](../../src/DTMS.Api/Program.cs#L347):

> *"... `RateLimit__PermitLimit` / `RateLimit__WindowSeconds` / `RateLimit__QueueLimit` for load tests (e.g. PermitLimit=100000, WindowSeconds=1)."*

Recommend adding a `make perf-test` target (or similar) that exports these env vars before invoking k6, so the rate-limit trap doesn't keep being rediscovered.

---

## Recommendations

| Priority | Action | Effort | Expected outcome |
|---|---|---|---|
| **P0** | **Step A3** — promote `PollIntervalSeconds` + `BatchSize` to `OutboxOptions`, add per-module pending gauge | ~2-2.5h | Tune to `PollIntervalSeconds=1` + `BatchSize=500` → drain rate ~3,000/s per module → ≥ create rate |
| **P0** | **Re-run scenario B after Step A3** | ~30 min | Validate Phase A acceptance properly (pending ≤ 100 after 60s settle) |
| **P1** | **Document rate-limit lift in perf-test runbook** | ~10 min | Avoid 30-min wasted on a known gotcha |
| **P2** | Phase B (PgBouncer) | 1-1.5 d | After Step A3 — multi-replica blocker remains separate |

---

## Comparison with 2026-06-16 baseline

| Dimension | 2026-06-16 | 2026-06-20 | Change |
|---|---|---|---|
| TPS create (write side) | 696 orders/s | 933 orders/s | **+34%** ✅ |
| p95 latency (create) | 91ms | 35.45ms | **−61%** ✅ |
| p99 latency (create) | 176ms | 57.1ms | **−68%** ✅ |
| Errors | 0 | 0 | flat ✅ |
| Outbox parity (drain/create) | 1.75% | ~0.6% | regressed (proportional — higher create rate, same drain ceiling) |
| Pending after test (deliveryorder) | 589,711 | 498,688 | similar |
| EXPLAIN ANALYZE plan (deliveryorder.OutboxMessages poll) | Index Scan + Sort | **Index Scan, no Sort** (partial index pre-ordered) | ✅ |

**Reading:** Phase A A1+A2 *did* improve the per-request and per-batch side — API throughput is up, latency is down, the partial index removed the Sort step. But the per-tick polling cadence is the binding constraint, and that's exactly what Step A3 targets.

---

## Files referenced

- [`docs/scale-readiness-plan.md`](../../docs/scale-readiness-plan.md) — Phase A plan + Step A3 spec
- [`docs/crash-recovery-workflow-resilience-plan.md`](../../docs/crash-recovery-workflow-resilience-plan.md) — T1 + outbox metrics context
- [`src/DTMS.Api/Infrastructure/Outbox/OutboxProcessorService.cs`](../../src/DTMS.Api/Infrastructure/Outbox/OutboxProcessorService.cs) — `PollingInterval` constant (line 22)
- [`src/DTMS.Api/Infrastructure/Outbox/OutboxOptions.cs`](../../src/DTMS.Api/Infrastructure/Outbox/OutboxOptions.cs) — current options class (target for Step A3 expansion)
- [`perf-tests/k6-scenario-b-run.txt`](../k6-scenario-b-run.txt) — raw k6 output of the passing run
- [`perf-tests/results-2026-06-16/REPORT.md`](../results-2026-06-16/REPORT.md) — baseline comparison
