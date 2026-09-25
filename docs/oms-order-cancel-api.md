# Cancelling an order from OMS

`POST /api/v1/source/delivery-orders/{orderRef}/cancel` — new on 2026-09-25.
Cancels a whole delivery order, addressed by **your** order reference.

The companion to [cancelling a single shipment](oms-trip-cancel-api.md), and the
bigger of the two: it reaches orders that have no shipment yet.

---

## Read this before you build anything

**This is the wider hammer, not the narrower one.**

`POST /api/v1/source/trips/{tripId}/cancel` can only touch an order we have
already dispatched — before that there is no trip id for you to name. Most
cancellations happen in exactly that window: you have submitted the order, we
have accepted it, and nothing has started moving. Only this endpoint reaches it.

Where shipments *do* exist, cancelling the order calls every one of them off as
well. **On an AMR shipment that stops a physical robot mid-route.** Same effect
as a DTMS operator pressing Cancel.

It is not reversible. A cancelled order does not come back; re-doing the work
means submitting a new order under a new reference — the old one stays taken.

---

## The call

```
POST /api/v1/source/delivery-orders/OD-240926-0147-BUSRUN/cancel
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
| `actionBy` | **yes** | Whoever pressed cancel on your side. Rejected if blank. See the note below on where it goes. |
| `reason` | no, but please | Free text. The one field here that an operator will actually read. |
| `actedAt` | no | When it happened in your system, ISO-8601. |

Same body as the shipment-cancel route on purpose, so one payload builder serves
both.

### About `{orderRef}`

It is your own reference, exactly as you sent it to us on create — we never ask
you to store our ids.

Two things follow from it being a URL path segment:

- **It must not contain `/`.** Such a reference cannot be addressed this way at
  all; tell us if you have one and we will take it in the body instead.
- **It is matched exactly, including case.** `od-240926-0147` will not find
  `OD-240926-0147`. Send it the way you created it.

Your token already carries the permission (`dtms:source:oms:order:cancel`). If
it does not yet, you will get a 403 — one click on our side.

---

## What you get back

| code | meaning |
|---|---|
| **204** | Cancelled. No body. |
| **204** | Also what you get if it was already cancelled — see "Retrying is safe". |
| **400** | Refused: the order has finished and cannot be cancelled, or `actionBy` was missing. |
| **401** | Token missing, expired, or not ours. |
| **403** | Your token lacks the cancel permission. |
| **404** | No order with that reference **for you**. |
| **500** | Our fault. The body carries a `traceId` — send it to us. |

Errors are RFC 7807 problem documents (served as `application/json`). Read
`detail`; the `error` member repeats it only so older clients keep working.

```
400  Cannot cancel an order in Completed status.
400  actionBy is required.
404  No delivery order with ref 'OD-240926-9999' for this source system.
```

**The 404 does not distinguish "no such reference" from "that one is not
yours".** That is deliberate: references are sequential and guessable, so
confirming one exists would tell you about another customer's orders. If you are
certain the reference is right, the thing to check is which token you sent.

This differs from the shipment route, which answers **403** for a shipment
belonging to someone else. Shipment ids are random, so naming the owner there
leaks nothing and saves you a support ticket.

---

## Retrying is safe

If you lose the connection and never see our response, **send the same request
again**. A cancel on an already-cancelled order answers 204 and does nothing: no
second robot command, no duplicate event, no second entry in the timeline.

You do not need an idempotency key on this endpoint — unlike order creation,
where reusing a reference is a 409 and the key is the only safe way to retry.

The 400s are the ones not worth repeating: they mean we refused, and repeating
changes nothing.

---

## Please send a reason

`reason` is optional so a missing one never fails your call, but it is the only
field here that reaches a human. It becomes the line on the order timeline a
DTMS operator reads when they ask why this order stopped.

Leave it out and we record `"Cancelled by source system"`, which tells nobody
anything. We log a warning every time, so we will notice.

`actionBy` and `actedAt` are **not** stored — they go to our application log and
no further. The order timeline records that OMS cancelled the order, not which
of your users did. We would rather say that plainly than let you assume
otherwise; if you need the person visible on our side, tell us and we will wire
it through properly.

---

## Which endpoint should you call?

| you want to | call |
|---|---|
| stop the whole order, whatever state it is in | **this one** |
| stop one shipment of a multi-shipment order, leaving the rest running | [the shipment route](oms-trip-cancel-api.md) |

On a single-shipment order — most of yours — the two do the same thing, because
cancelling the only shipment cancels its order too. Prefer this one anyway: it
says what you mean, and it works before dispatch.

---

## Notes for DTMS engineers

- Endpoint + body: [`SourceSystemDeliveryOrderEndpoints`](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Presentation/SourceSystemDeliveryOrderEndpoints.cs); handler: [`SourceCancelDeliveryOrderCommandHandler`](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Commands/SourceCancelDeliveryOrder/SourceCancelDeliveryOrderCommandHandler.cs)
- The origin guard is the lookup: `GetByRefAsync` filters on `(SourceSystemKey, OrderRef)` against the unique index, so a foreign order is not found rather than refused. There is no separate ownership check to reorder or forget
- Two reads on purpose — `GetByRefAsync` is `AsNoTracking` with no Includes, right for a guard read and wrong for a mutation. It resolves the id and hands off to `CancelDeliveryOrderCommand`, which owns the tracked load, the unbound-item sweep, the concurrency catch and the logging
- Cancelling reaches trips through `OrderCancelledCascadeConsumer`, which now forwards the caller's identity: `Origin` for a system, `ActionBy` for a person, decided by the event's `Channel` — never `SourceSystem`, which is the order's owner rather than the caller
- Status codes come from exceptions via `ExceptionHandlingMiddleware`; `NotFoundException` → 404, `ForbiddenException` → 403
