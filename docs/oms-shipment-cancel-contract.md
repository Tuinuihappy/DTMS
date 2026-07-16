# OMS callback — restoring `POST /api/shipments/{id}/cancelled`

**Status:** built and **switched off**. Blocked on one question for the OMS team (below).

**This is not a new contract.** DTMS used to send exactly this call. OMS removed the endpoint in June 2026, DTMS deleted the outbound chain to match ([`0f123c2`](https://github.com/Tuinuihappy/DTMS/commit/0f123c2)), and we are now asking for it back — same route, same body, byte for byte.

> **Revised 2026-07-16.** The first version of this doc warned that a cancelled shipment would routinely come back to life on retry, and called that "a real order from our data". That statistic was from `internal` orders, not OMS's. **No OMS shipment has ever been retried** — 0 retries in 61 trips. If that warning made this callback look more trouble than it is worth, please re-read §2. The stuck list also grew from 3 to 10: the first count came from a screenshot rather than a query.

## The question we need answered first

**Why was `/api/shipments/{id}/cancelled` removed, and can it come back?**

Our commit records only the fact, not the reason:

> *"Upstream OMS removed these three shipment endpoints, so DTMS must stop calling them. Today only /shipments (start) and /shipments/{id}/arrived remain."*

The three were `/failed`, `/cancelled`, `/pod-completed`. We are only asking about `/cancelled`.

- **If OMS deliberately doesn't want cancellations** — say so and we delete this work. No hard feelings; better than shipping a callback you throw away.
- **If it was collateral from a refactor** — restore the route and a DTMS admin flips one toggle. Nothing else to build on either side.
- **If it can come back but at a different route/shape** — tell us which, it is a one-line change here.

## Why we want it back

Without it, a shipment that dies stays "in progress" in OMS forever. **Ten are stuck that way right now.**

And the reason they died matters more than we first thought. We traced every one:

| cause | count | how we know |
|---|---|---|
| **a person called the job off** | **8** | operator cancelled the trip, or cancelled the order and it swept its trips |
| the robot genuinely failed | **1** | a mission reported FAILED, with a fault code |
| RIOT3 aborted, reason not recorded | **1** | `[E700001]` with no failing mission and no diagnostic |

So this is **not** an exception channel for rare robot faults. **Eight of ten were a person deciding to stop the job** — an ordinary, deliberate act, and exactly the case where OMS is most likely to be waiting on an answer that never comes.

The one real fault was **OD-0516**: the robot reached SHELF3, failed the pick action (`400403`, RIOT3 `E112045`), and RIOT3 aborted the order (`[E700001]`). Every other shipment died before finishing its first move.

**A note on `cancelReason`, because it will mislead you.** When someone cancels in DTMS, we tell RIOT3, and RIOT3 echoes the cancellation back over its webhook ~100ms later with an empty reason field — at which point DTMS overwrites the real reason with its placeholder text, `vendor cancelled`. So a payload saying `vendor cancelled` usually means *a person cancelled this*, not *the vendor did*. We fell for it ourselves while writing this doc. Treat `cancelReason` as a human note of unreliable provenance; if you need to know who stopped a job, ask us for a proper actor field and we'll add one.

### The ten

`shipmentId` is the id OMS received from `shipment.started.v1`.

| DTMS order | cancelled at (UTC) | `shipmentId` | why |
|---|---|---|---|
| OD-0464-WIP | 2026-07-06 09:18 | `cc0a94e7-5d3a-436e-b4c8-f1adc529a389` | RIOT3 aborted — `[E700001]`, no diagnostic |
| OD-NOARR-1783334634 | 2026-07-06 10:53 | `52f974ea-eb35-4d10-9e59-b83f8e173def` | **person** — operator cancelled |
| OD-0465-WIP | 2026-07-06 11:59 | `13ac77ec-ca36-4879-b539-1e50f2ed4817` | **person** — order cancelled, trip swept |
| OD-0466-WIP | 2026-07-06 11:59 | `261f0276-629b-4038-8fd8-4e7919d2a826` | **person** — order cancelled, trip swept |
| OD-0473-WIP | 2026-07-07 01:38 | `ae6053e1-5070-4337-b4cf-117248ad6df8` | **person** — order cancelled, trip swept |
| OD-0516-WIP | 2026-07-15 08:52 | `fed43139-6a79-4f48-ab75-f54b59fb8232` | **robot fault** — pick action failed |
| OD-0517-WIP | 2026-07-15 08:55 | `fceeeac2-8785-4907-aa26-695595980cbe` | **person** — operator cancelled |
| OD-0518-WIP | 2026-07-15 09:07 | `9bc3f792-92b5-47c6-83e1-98b49021e782` | **person** — operator cancelled |
| OD-0519-WIP | 2026-07-15 09:16 | `9494be6e-a9db-457c-b34e-c9ff065cacd0` | **person** — operator cancelled |
| OD-UTCTEST-1783319383 | 2026-07-16 07:18 | `5a0a750e-05a2-42e9-8485-38052d673d35` | **person** — operator cancelled (test order) |

**These need clearing on the OMS side by hand.** Nothing will retro-send them — and **turning the callback on later will not flush this backlog**, because no message was ever queued for them. Every job cancelled between now and the switch being thrown joins this list permanently.

## What DTMS will send

```
POST {CallbackBaseUrl}/api/shipments/{shipmentId}/cancelled
Authorization: Bearer <the same token used for shipment.started / shipment.arrived>
Content-Type: application/json
X-DTMS-Event-Type: shipment.cancelled.v1

{"cancelReason":"vendor cancelled","cancelledBy":"86347852","occurredAt":"2026-07-15T09:07:09Z"}
```

Identical to the old `OmsTripCancelledNotification`:

- `shipmentId` — same id `shipment.started.v1` sent. Nothing new to map.
- `cancelReason` — free text: a RIOT3 vendor reason (`[E700001]:订单执行异常`), an operator's words, or `Order cancelled: <reason>` when the whole order was cancelled and this trip was swept with it. A note, not an enum.
- `cancelledBy` — employee id, or **null** for vendor-initiated cancels.
- `occurredAt` — when the cancellation happened, not when we sent it (a retried delivery keeps the original stamp).

`2xx` and `409` are both success. `404`/other `4xx` is permanent failure: we stop retrying and alert an operator.

## Two things to re-confirm

### 1. A cancelled shipment *can* start again — but never has

**This is the paragraph the first version of this doc overstated.** The facts:

- **In theory:** if an operator retries a failed trip, the retry reuses the **same `shipmentId`**. OMS would then see `started(X) → cancelled(X) → started(X)`.
- **In practice:** **this has never happened to an OMS shipment.** Every OMS trip on record — 61 of them — ended at attempt 1. Zero retries, ever. The retries we do see (8 of them) are all on internally-created orders, a different flow.
- **Nothing structurally prevents it.** The retry action isn't blocked for OMS-sourced orders; it simply hasn't been used on one.

So: if OMS treats `cancelled` as terminal, that matches every OMS shipment to date. Worth knowing the edge exists, not worth designing around unless you want to. If you'd rather we guarantee terminality, say so — we'd move to an order-level signal that only fires once every attempt is exhausted.

### 2. Please be idempotent on repeat cancels

If a retry ever does happen and the chain ultimately dies, `cancel(X)` goes out **once per failed attempt**. DTMS de-duplicates its own delivery retries but not distinct attempts. `409` is the friendliest answer; `404` will page someone.

## Turning it on (DTMS side)

**Admin → Systems → oms → Manage subscriptions → click `Disabled` on `shipment.cancelled.v1`**

Takes effect within one Redis round-trip. No deploy. Same toggle is the kill switch.

## Notes for DTMS engineers

- Fan-out: [`ShipmentCancelledCallbackFanoutConsumer`](../src/DTMS.Api/Infrastructure/Callbacks/ShipmentCancelledCallbackFanoutConsumer.cs) — the federated re-do of the deleted `TripCancelledOmsNotifyConsumer`, same event, same root-trip-id contract
- Wire shape: [`OmsShipmentCancelledFormatter`](../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/OmsShipmentCancelledFormatter.cs) (`oms.shipment.cancelled.v1`), pinned byte-for-byte against the legacy DTO by `Cancelled_BodyMatchesLegacyContract_AndShipmentIdInPath`
- Subscription seeded disabled by `20260716120000_SeedOmsShipmentCancelledSubscription`
- **Stricter than the old consumer**, which had no trip-state guards and cancelled unconditionally. We skip pool trips (`DispatchedAt` set) and never-started trips (`StartedAt` null): no `started` was sent for either, so a cancel would name a shipment OMS has never seen. If OMS would rather have those too, drop the two guards.
- The cancel carries no lot list — neither did the old one. `TripCancelledConsumer` unbinds the trip's items while handling the same event on another queue, so any lot lookup here is a race.
- The stuck list is derived, not hand-kept — re-run it rather than trusting the table above:
  ```sql
  SELECT d."OrderRef", t."Id" AS shipment_id, t."CompletedAt"
  FROM dispatch."Trips" t
  JOIN deliveryorder."DeliveryOrders" d ON d."Id" = t."DeliveryOrderId"
  WHERE d."SourceSystemKey" = 'oms' AND d."OrderRef" <> ''
    AND t."Status" = 'Cancelled' AND t."StartedAt" IS NOT NULL AND t."DispatchedAt" IS NULL
  ORDER BY t."CompletedAt";
  ```
  (`shipmentId` = root trip id; identical to `t."Id"` only while no OMS order has been retried — join through `PreviousAttemptId` if that ever changes.)
