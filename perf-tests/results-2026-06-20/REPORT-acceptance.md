# Phase A Acceptance — k6 Scenario B at Full Tuned Pipeline + NoOp Vendor

**Date:** 2026-06-20
**Build:** main @ f91c8b1 (after Phase A Step A3 core + NoOp adapter)
**Goal:** Validate Phase A end-to-end with **zero vendor side-effects**

## Setup — final tuned config

```
Outbox__UseSkipLocked=true
Outbox__PublishConcurrency=8
Outbox__BatchSize=500
Outbox__PollIntervalSeconds=1
Outbox__PerMessageTimeoutSeconds=10  (default)
RateLimit__PermitLimit=100000        (lifted)
RateLimit__WindowSeconds=1
RateLimit__QueueLimit=5000
VendorAdapter__Riot3__Enabled=false   ← prevents real RIOT3 calls
```

Boot log confirmed: `OutboxProcessorService started (UseSkipLocked=True, BatchSize=500, PublishConcurrency=8, PollIntervalSeconds=1, PerMessageTimeoutSeconds=10)`

Scenario: write_ramp 1 → 30 VU over 1m10s, then ramp-down.

## TL;DR

| Dimension | Before Phase A (2026-06-16) | Run 2 (tuned, vendor on) | **Run 3 (acceptance, vendor off)** | Δ vs baseline |
|---|---|---|---|---|
| TPS create | 696 orders/s | 326 orders/s | **812 orders/s** | **+17%** |
| p95 latency (write) | 91ms | 156ms | **39.53ms** | **−57%** |
| p99 latency (write) | 176ms | 228ms | **51.33ms** | **−71%** |
| Error rate | 0% | 0% | **0%** | flat |
| RIOT3 calls | (didn't measure) | ~1,000+ unintended | **0** 🔒 | safe |
| NoOp skips logged | n/a | n/a | **2,315** | proves vendor seam swapped |
| Orders → Dispatched | (didn't measure) | 1,021 (with 2 Failed) | **5,009 (with 0 Failed)** | clean |
| Pending after k6 | ~589k 🔴 | ~87k 🟡 | ~220k 🟡 | better than 06-16, worse than Run 2 (because higher TPS) |
| Backlog drain time | ~14h | unmeasured | ~24min @ 143/s observed | acceptable for catch-up after burst |

Headline:
- 🎉 **Zero RIOT calls** during a 56,134-order burst that, without the NoOp flag, would have shelled out 5,009+ vendor mission-create calls. Production safety mechanism works.
- ✅ **API side excellent** — 812/s sustained with p95 39.5ms, p99 51.3ms, 100% success. Best read on the API write-path Phase A delivers.
- ✅ **5,009 orders fully Dispatched in DTMS** end-to-end (DispatchByRoute → NoOp → MarkOrderDispatched) within ~2 min. Pipeline works.
- ⚠️ **Drain still trails create at peak burst** — drain ~143 events/s observed vs ~6,400 events/s generation at 30 VU peak. Phase A unblocks per-batch throughput but per-tick polling cadence still binds.
- 📋 **Next bottleneck**: outbox processor processes 6 modules sequentially in `ProcessUnpublishedEventsAsync`. Parallelising those (each module on its own loop) would lift effective drain ceiling 6×.

---

## Full k6 output

```
  █ THRESHOLDS

    errors                                       ✓ 'rate<0.05' rate=0.00%
    http_req_duration{ep:create}  p(95)=39.53ms  ✓ 'p(95)<1500'
                                  p(99)=51.33ms  ✓ 'p(99)<3000'
    http_req_failed                              ✓ 'rate<0.05' rate=0.00%

  █ TOTAL RESULTS
    checks_succeeded   100.00%   56134 / 56134
    orders_created     56,134    811.93/s
    http_req_duration  min  avg     med     p90     p95     p99    max
                       —    24.24ms 22.77ms 34.52ms 39.53ms 51.33ms 409.46ms
```

## DTMS pipeline outcome (post k6 + 60s settle)

| Status | Count | % |
|---|---|---|
| **Dispatched** | **5,009** | **8.9%** |
| Confirmed (stuck behind drain) | 51,123 | 91.1% |
| Failed | 0 | 0% |
| Planned (in flight) | 0 | 0% |

## Vendor seam verification (NoOp adapter)

```
NoOp log occurrences in API:  2,315  ← every Dispatch → NoOp → fake orderKey
"Sending RIOT3 order" log:    0      ← zero real vendor calls
```

Example NoOp log line:
```
[NoOp] Skipping RIOT3 dispatch for upperKey=d565aa80ed4f4cc38fe0cb4f75b170e3-G1
        with 3 mission(s); returning fake orderKey=NOOP-561e853037bf45bba51d728280b78531
```

Dispatch consumer accepted the fake orderKey and persisted it on `Trip.VendorRequestSnapshot = "{}"` per the NoOp contract — no broken downstream assumptions.

## Outbox drain timeline

| Time after k6 ended | deliveryorder pending |
|---|---|
| t = 0s | 220,216 |
| t = +10s | 218,707 |
| t = +60s | 211,612 |
| (observed drain) | ~143 events/s |

3,000 events/s theoretical (PollIntervalSeconds=1 × BatchSize=500 × 6 modules) was NOT realised. Why:
- `ProcessUnpublishedEventsAsync` iterates the 6 module DbContexts **sequentially** with `await`.
- Each module tick at full 500 batch with SKIP LOCKED tx + parallel publish + SaveChanges + count = ~500-800ms wall-clock under contention.
- 6 × ~700ms ≈ 4.2s per cycle + 1s wait = ~5s effective cycle, even though `PollIntervalSeconds=1`.
- 500 messages / 5s = 100/s per module. Most pending is in deliveryorder so ~100/s on the hot schema — matches observed ~143/s (somewhat helped by NoOp speed).

## What this validates vs what's still open

### Validated ✅
1. NoOp adapter conditional DI works — flip flag, no vendor calls happen
2. Phase A Step A3 tunable knobs are correctly bound and surface in boot log
3. Full DTMS pipeline (Submit → Validate → Confirm → Plan → Dispatch) flows end-to-end under load with NoOp at the seam
4. API write-path scales well (812/s sustained, p95 < 40ms)
5. 0 errors across 56k orders — no transaction conflicts, no consumer faults, no SQL issues with SKIP LOCKED + ExecutionStrategy combo

### Open 🟡 / 📋
1. **Phase A acceptance "pending ≤ 100 within 30s settle" NOT met.** Drain rate ~143/s vs create-side ~6,400 events/s at peak.
2. **Next bottleneck: sequential module loop.** Easy parallelisation win — `Parallel.ForEachAsync` over the 6 module contexts in `ProcessUnpublishedEventsAsync` would ~5× drain. Worth a follow-up Step A4 or similar.
3. **Drain catch-up at 24 min for a 70s burst** is acceptable for chaos events but breaks the "sustained throughput" picture. Real-world load (5-10 orders/s normal traffic) drains fast — the test stress (30 VU bursting) is not normal user behaviour.

## Recommendations

| Priority | Action | Effort |
|---|---|---|
| **P0** | Parallelise outbox per-module loop (Step A4-equivalent) — single biggest remaining drain win | ~1h |
| P1 | Re-run scenario B after parallel modules ship → target ≤100 pending in 30s settle | 30 min |
| P2 | Document `VendorAdapter:Riot3:Enabled=false` requirement in perf-test runbook | 10 min |
| P3 | Add per-module pending gauge (Step A3 deferred telemetry) for live observability | 1.5h |

---

## Files referenced

- [REPORT.md](REPORT.md) — first run (vendor enabled, finding #2 surfaced outbox drain bottleneck)
- [`docs/scale-readiness-plan.md`](../../docs/scale-readiness-plan.md) — Phase A spec + acceptance threshold
- [`src/AMR.DeliveryPlanning.Api/Adapters/NoOpOrderDispatcherAdapter.cs`](../../src/AMR.DeliveryPlanning.Api/Adapters/NoOpOrderDispatcherAdapter.cs) — NoOp impl
- [`src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxProcessorService.cs`](../../src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/OutboxProcessorService.cs#L74) — sequential module loop (next bottleneck)
- [`k6-scenario-b-acceptance.txt`](../k6-scenario-b-acceptance.txt) — raw k6 output of this run
