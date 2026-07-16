# OMS callback — restoring `POST /api/shipments/{id}/cancelled`

**Status:** built and **switched off**. Blocked on one question for the OMS team (below).

**This is not a new contract.** DTMS used to send exactly this call. OMS removed the endpoint in June 2026, DTMS deleted the outbound chain to match ([`0f123c2`](https://github.com/Tuinuihappy/DTMS/commit/0f123c2)), and we are now asking for it back — same route, same body, byte for byte.

## The question we need answered first

**Why was `/api/shipments/{id}/cancelled` removed, and can it come back?**

Our commit records only the fact, not the reason:

> *"Upstream OMS removed these three shipment endpoints, so DTMS must stop calling them. Today only /shipments (start) and /shipments/{id}/arrived remain."*

The three were `/failed`, `/cancelled`, `/pod-completed`. We are only asking about `/cancelled`.

- **If OMS deliberately doesn't want cancellations** — say so and we delete this work. No hard feelings; better than shipping a callback you throw away.
- **If it was collateral from a refactor** — restore the route and a DTMS admin flips one toggle. Nothing else to build on either side.
- **If it can come back but at a different route/shape** — tell us which, it is a one-line change here.

## Why we want it back

Without it, a shipment that dies is still "in progress" in OMS forever. Three real orders from a single 17-minute window are stuck that way right now:

| DTMS order | cancelled at | `shipmentId` OMS holds |
|---|---|---|
| OD-0516-WIP | 2026-07-15 15:52 | `fed43139-6a79-4f48-ab75-f54b59fb8232` |
| OD-0517-WIP | 2026-07-15 15:55 | `fceeeac2-8785-4907-aa26-695595980cbe` |
| OD-0518-WIP | 2026-07-15 16:07 | `9bc3f792-92b5-47c6-83e1-98b49021e782` |

**These three need clearing on the OMS side by hand** — they predate this work and nothing will retro-send them. It accrues on every failed job.

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
- `occurredAt` — when the cancellation happened, not when we sent it (a retried callback keeps the original stamp).

`2xx` and `409` are both success. `404`/other `4xx` is permanent failure: we stop retrying and alert an operator.

## What changed since the old endpoint

Two things worth re-confirming rather than assuming.

### 1. A cancelled shipment can start again ⚠️

The old code's DTO carried this note:

> *"Receiver typically treats cancellation as final-no-retry"*

**If that is still how OMS models it, this callback will break your state machine.** When a trip fails, an operator can retry it, and **a retry reuses the same `shipmentId`**:

```
shipment.started.v1     shipmentId=X    attempt 1 sets off
shipment.cancelled.v1   shipmentId=X    attempt 1 dies
shipment.started.v1     shipmentId=X    attempt 2 sets off      ← X is alive again
shipment.arrived.v1     shipmentId=X    attempt 2 delivers
```

That is a real order from our data: attempts 1 and 2 cancelled, attempt 3 delivered, order completed. Every retry in our history (8 of 8) followed a cancel, landing 2 seconds to 7 minutes later. Retries are operator-driven, so **at the moment we send the cancel we cannot know whether one is coming.**

So `cancelled` means **"this attempt failed"**, not "this shipment is dead". If OMS needs the second meaning, we need a different design — tell us and we will move to an order-level signal that only fires once every attempt is exhausted.

### 2. Please be idempotent on repeat cancels

A retried shipment that ultimately dies sends `cancel(X)` **once per failed attempt**. DTMS de-duplicates its own delivery retries but not distinct attempts. `409` is the friendliest answer; `404` will page someone.

## Turning it on (DTMS side)

**Admin → Systems → oms → Manage subscriptions → click `Disabled` on `shipment.cancelled.v1`**

Takes effect within one Redis round-trip. No deploy. Same toggle is the kill switch.

## Notes for DTMS engineers

- Fan-out: [`ShipmentCancelledCallbackFanoutConsumer`](../src/DTMS.Api/Infrastructure/Callbacks/ShipmentCancelledCallbackFanoutConsumer.cs) — the federated re-do of the deleted `TripCancelledOmsNotifyConsumer`, same event, same root-trip-id contract
- Wire shape: [`OmsShipmentCancelledFormatter`](../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/OmsShipmentCancelledFormatter.cs) (`oms.shipment.cancelled.v1`), pinned byte-for-byte against the legacy DTO by `Cancelled_BodyMatchesLegacyContract_AndShipmentIdInPath`
- Subscription seeded disabled by `20260716120000_SeedOmsShipmentCancelledSubscription`
- **Stricter than the old consumer**, which had no trip-state guards and cancelled unconditionally. We skip pool trips (`DispatchedAt` set) and never-started trips (`StartedAt` null): no `started` was sent for either, so a cancel would name a shipment OMS has never seen. If OMS would rather have those too, drop the two guards.
- The cancel carries no lot list — neither did the old one. `TripCancelledConsumer` unbinds the trip's items while handling the same event on another queue, so any lot lookup here is a race.
