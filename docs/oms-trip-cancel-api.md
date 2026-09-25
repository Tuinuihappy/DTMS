# Cancelling a shipment from OMS

`POST /api/v1/source/trips/{tripId}/cancel` — new on 2026-09-25. Lets OMS call
off a shipment it created, instead of asking a DTMS operator to press Cancel.

It joins the five source-system trip endpoints you already use
(`acknowledge`, `pickup`, `drop`, `complete`, `acknowledge-robot-pass`): same
base path, same bearer token, same `actionBy` convention.

---

## Read this before you build anything

**Cancelling the trip usually cancels the order too.**

Most of your orders have exactly one trip. When the last active trip on an order
is cancelled, DTMS terminates the parent delivery order and its items as well.
There is no separate "cancel the order" call for you to make, and no way to
cancel the trip while keeping the order alive.

So treat this endpoint as *cancel the shipment*, not *cancel this attempt*. It
is not reversible: a cancelled order does not go back to in-progress, and
re-dispatching means submitting a new order.

**On an AMR shipment this stops a physical robot.** If DTMS is executing the
transport and the robot is mid-route, the cancel reaches the vehicle controller
and the run is aborted. Same effect as a DTMS operator pressing Cancel.

---

## The call

```
POST /api/v1/source/trips/{tripId}/cancel
Authorization: Bearer <your existing system token>
Content-Type: application/json

{
  "actionBy": "somchai.k",
  "reason": "Customer cancelled the order",
  "actedAt": "2026-09-25T10:14:00Z"
}
```

| field | required | notes |
|---|---|---|
| `actionBy` | **yes** | Whoever pressed cancel on your side — user id, name, email, your choice. A blank one is rejected. |
| `reason` | no, but please | Free text. See below. |
| `actedAt` | no | When it happened in your system, ISO-8601. Defaults to when we receive the call. |

**`tripId`** is the `shipmentId` we sent you on `shipment.started.v1`. One
caveat: if a shipment was re-dispatched after a failure, that id points at the
superseded attempt and cancelling it will be refused. Two of your orders have
hit that so far. Ask us for the current trip id if you see the rejection.

Your token already carries the permission (`dtms:source:oms:trip:cancel`,
granted 2026-09-25). No new credential, no re-issue.

You can only cancel shipments on your own orders. A trip belonging to another
system answers 403, with a message naming the real owner.

---

## What you get back

| code | meaning |
|---|---|
| **204** | Cancelled. No body. |
| **204** | Also what you get if it was already cancelled — see "Retrying is safe". |
| **400** | Refused: the shipment is in a state that cannot be cancelled, or the request was incomplete. |
| **401** | Token missing, expired, or not ours. |
| **403** | Either the token lacks the cancel permission (tell us — one click on our side), or that shipment belongs to a different source system. The message says which. |
| **404** | No shipment with that id. |
| **500** | Our fault. The body carries a `traceId` — send it to us. |

Errors are RFC 7807 problem documents (served as `application/json`):

```json
{
  "status": 400,
  "title": "Business Rule Violation",
  "detail": "Cannot cancel a trip in Completed status.",
  "instance": "/api/v1/source/trips/4f2a.../cancel",
  "traceId": "0a91...",
  "error": "Cannot cancel a trip in Completed status."
}
```

`error` repeats `detail` so code written against our older shape keeps working.
It is a compatibility shim and will go away — **read `detail`**.

Messages you might see:

```
400  Cannot cancel a trip in Completed status.
400  actionBy is required.
400  Vendor cancel failed: <reason from the robot controller>
403  Trip 4f2a... belongs to source system 'sap', not 'oms' — caller may only act on its own trips.
404  Trip 0000...0001 not found.
```

The vendor one means the robot controller refused. Nothing was changed on our
side — the shipment is still running. Worth retrying or escalating rather than
treating as cancelled.

> **Changed 2026-09-25.** These used to be 400 for everything. A missing
> shipment is now 404 and somebody else's shipment is 403, so you can tell "fix
> the id" from "fix the token" from "the shipment cannot be cancelled". The body
> gained `status` / `title` / `instance` / `traceId`, kept `error`, and is still
> served as `application/json` — so nothing breaks if you are not ready to
> change.

---

## Retrying is safe

If you lose the connection and never see our response, **send the same request
again**. A cancel on an already-cancelled shipment answers 204 and does nothing:
no second robot command, no duplicate event, no second entry in the audit trail.

You do not need an idempotency key on this endpoint.

The only requests that are *not* safe to blind-retry are the 400s — those mean
we refused, and repeating changes nothing.

---

## Please send a reason

`reason` is optional so a missing one never fails your call, but it is the most
useful field you can send us. It becomes:

- the line a DTMS operator reads on the order timeline, and
- the `cancelReason` we will send back to you on `shipment.cancelled.v1` once
  that callback is switched on.

Leave it out and we record `"Cancelled by source system"`, which tells nobody
anything. We log a warning every time that happens, so we will notice.

---

## What this cannot do

**Cancel before the shipment starts.** The endpoint takes a trip id, and a trip
only exists once we have dispatched. An order that is accepted but not yet
dispatched has nothing to cancel this way — use
[`POST /api/v1/source/delivery-orders/{orderRef}/cancel`](oms-order-cancel-api.md)
instead, which is addressed by your own order reference and reaches that window.

**Cancel a finished shipment.** Completed and rejected shipments are refused.

---

## One question for you

Once `shipment.cancelled.v1` is switched on, a cancel *you* requested would come
straight back to you as a callback. We can suppress that, or leave it as a
confirmation receipt — we do not know which you would rather have. Tell us
before we enable it.

---

## Notes for DTMS engineers

- Endpoint: [`SourceSystemTripEndpoints`](../src/Modules/Dispatch/DTMS.Dispatch.Presentation/SourceSystemTripEndpoints.cs); handler: [`SourceCancelTripCommandHandler`](../src/Modules/Dispatch/DTMS.Dispatch.Application/Commands/SourceSystemTrip/SourceCancelTripCommandHandler.cs)
- Origin check runs in `SourceTripCommandHandlerBase` before any state is read, so the 204/403 difference cannot be used to probe for another system's trip ids
- The already-cancelled shortcut is deliberately `Cancelled`-only; `Trip.Cancel` must keep throwing so `TripCancellationService`'s xmin retry can still detect the vendor echo winning the race
- Cancel itself is [`TripCancellationService`](../src/Modules/Dispatch/DTMS.Dispatch.Application/Services/TripCancellationService.cs) — the same path the operator console uses, so there is one place that talks to the vendor about a cancel
- Origin lands on `ExecutionEvent.Details` as `via=oms` and the actor on `ExecutionEvent.Actor`; the order activity row carries `ActorId = oms`. Vendor-echo cancels tag themselves `via=riot3` / `via=riot3-reconciler`, operator cancels carry the employee id and no origin
- Echo suppression before enabling `shipment.cancelled.v1`: see [oms-shipment-cancel-contract.md](oms-shipment-cancel-contract.md)
