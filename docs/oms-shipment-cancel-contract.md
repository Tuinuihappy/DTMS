# OMS callback contract — `shipment.cancelled.v1`

**Status:** implemented on the DTMS side, **switched off**. Waiting on OMS to build the endpoint and confirm the semantics below.

## Why

DTMS never tells OMS when a shipment is cancelled. OMS's last update is `shipment.started.v1`, so a shipment that died stays "in progress" on the OMS side forever. Three real orders in one 17-minute window, all of which OMS still believes are running:

| DTMS order | cancelled at | `shipmentId` OMS holds |
|---|---|---|
| OD-0516-WIP | 2026-07-15 15:52 | `fed43139-6a79-4f48-ab75-f54b59fb8232` |
| OD-0517-WIP | 2026-07-15 15:55 | `fceeeac2-8785-4907-aa26-695595980cbe` |
| OD-0518-WIP | 2026-07-15 16:07 | `9bc3f792-92b5-47c6-83e1-98b49021e782` |

These three need clearing on the OMS side by hand — they predate this callback and nothing will retro-send them.

## What DTMS will send

```
POST {CallbackBaseUrl}/api/shipments/{shipmentId}/cancel
Authorization: Bearer <the same token used for shipment.started / shipment.arrived>
Content-Type: application/json
X-DTMS-Event-Type: shipment.cancelled.v1

{"reason":"vendor cancelled"}
```

`shipmentId` is the **same id DTMS sent in `shipment.started.v1`** — no new identifier to map.

`reason` is free text from whoever cancelled: a RIOT3 vendor cancel reason (`[E700001]:订单执行异常`), an operator's text from the DTMS UI, or `Order cancelled: <reason>` when the whole order was cancelled and the trip was swept with it. Treat it as a human-readable note, not an enum.

## What OMS needs to confirm

### 1. Path and body

Both are DTMS's proposal, not something OMS specified. Say if you want different — it is a one-line change on our side while the switch is off, and a breaking change after.

### 2. A cancelled shipment can start again ⚠️

**This is the one that needs a decision, not just an ack.**

When a trip fails, an operator can retry it from the DTMS UI. A retry reuses the **same** `shipmentId`. So this sequence is normal and will happen:

```
shipment.started.v1    shipmentId=X     attempt 1 sets off
shipment.cancelled.v1  shipmentId=X     attempt 1 dies
shipment.started.v1    shipmentId=X     attempt 2 sets off      ← X is alive again
shipment.arrived.v1    shipmentId=X     attempt 2 delivers
```

That is a real order from our data: attempts 1 and 2 cancelled, attempt 3 delivered, order completed successfully.

`cancelled` therefore means **"this attempt failed"**, not "this shipment is dead". Every retry in our history so far (8 of 8) followed a cancel, arriving 2 seconds to 7 minutes later. Retries are operator-driven, so at the moment we send the cancel we cannot know whether one is coming.

**If OMS's state machine treats `cancelled` as terminal, the follow-up `started` will be rejected and we must not enable this callback** — tell us and we will move to an order-level signal that only fires once every attempt is exhausted.

### 3. Please be idempotent, or answer 409

Two cases send a duplicate or unrecognised cancel:

- **Duplicate:** a retried shipment that ultimately dies sends `cancel(X)` once per failed attempt. DTMS's outbox de-duplicates its own retries but not distinct attempts, so OMS can receive `cancel(X)` more than once.
- **Unknown shipment:** guarded against (DTMS only cancels shipments it sent a `started` for), but not something we would bet a dead-letter queue on.

**A `2xx` or `409` is treated as success.** A `404` or other `4xx` is a permanent failure: DTMS stops retrying and raises an operator alert. So `409` on an already-cancelled or unknown shipment is the friendliest response — `404` will page someone.

## Turning it on

Once OMS confirms and the endpoint is live, a DTMS admin flips one toggle — no deploy:

**Admin → Systems → oms → Manage subscriptions → click `Disabled` on `shipment.cancelled.v1`**

Takes effect within one Redis round-trip. The same toggle is the kill switch if anything goes wrong.

## Notes for DTMS engineers

- Fan-out: [`ShipmentCancelledCallbackFanoutConsumer`](../src/DTMS.Api/Infrastructure/Callbacks/ShipmentCancelledCallbackFanoutConsumer.cs) on `TripCancelledIntegrationEvent`
- Wire shape: [`OmsShipmentCancelledFormatter`](../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/OmsShipmentCancelledFormatter.cs) (`oms.shipment.cancelled.v1`)
- Subscription seeded disabled by `20260716120000_SeedOmsShipmentCancelledSubscription`
- Skipped deliberately: pool trips (`DispatchedAt` set) and never-started trips (`StartedAt` null) — OMS was never told those shipments started, so cancelling them would name an unknown id
- The cancel carries no lot list on purpose: `TripCancelledConsumer` unbinds the trip's items while handling the same event on another queue, so any lot lookup here is a race
