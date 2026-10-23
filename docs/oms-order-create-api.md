# Creating an order from OMS

`POST /api/v1/source/delivery-orders` — how to send DTMS an order, what every
answer means, and which ones to retry.

Companions: [cancelling an order](oms-order-cancel-api.md) ·
[cancelling a shipment](oms-trip-cancel-api.md) ·
[system onboarding / tokens](system-onboarding.md).

---

## Read this before you build anything

**A 201 means "accepted into planning", nothing more.** We have validated the
order and queued it; robots have not moved. Progress reaches you later through
the shipment callbacks (started → pickup-arrived → dropped-off).

**Since 2026-10-23 we refuse an order we cannot carry out — at once, with 422.**
Before, an order on a route with no robot program was accepted (201), failed
inside DTMS a few seconds later, and you were never told. Now you get the 422 in
the same response, nothing is created, and you can fix it and send the **same
`orderRef`** again.

**Do not retry 4xx answers automatically.** They mean we refused the content;
sending the identical request again gets the identical refusal. We currently see
OMS repeat a refused order three times in ~20 s. Retry only what the table below
marks as retryable.

---

## The call

```
POST /api/v1/source/delivery-orders
Authorization: Bearer <your system token>
Idempotency-Key: 4f1c2a9e-0b7d-4c55-9a3e-2d6f8e1b7c40
Content-Type: application/json

{
  "orderRef": "OD-231026-0161-BUSRUN",
  "priority": "NORMAL",
  "requestedTransportMode": "AMR",
  "requestedBy": "sunisa",
  "serviceWindow": { "earliestUtc": "2026-10-23T03:00:00Z", "latestUtc": "2026-10-23T05:00:00Z" },
  "items": [
    {
      "itemId": "LOT-01M58YAMB5",
      "pickupLocationCode": "SHELF1",
      "dropLocationCode": "STF_05",
      "quantity": { "value": 2160, "uom": "PCE" },
      "weightKg": 0.4
    }
  ]
}
```

### Headers

| header | required | notes |
|---|---|---|
| `Authorization` | **yes** | `Bearer` + the token from `POST /oauth/token` (client credentials). Your system identity comes from the token — there is no system key in the URL or body. |
| `Idempotency-Key` | **yes** | A new random value (UUID) per order. Re-send the **same** value when retrying a request whose answer you didn't get — see [Retrying](#retrying-safely). Max 200 characters. |

### Body

| field | required | notes |
|---|---|---|
| `orderRef` | **yes** | Your reference, ≤ 200 chars. Unique per source system — a second order with the same ref is a 409. It is how you address the order later (cancel, callbacks). Avoid `/` (it can't be used in the cancel URL). |
| `serviceWindow` | **yes** | `{ earliestUtc, latestUtc }`, ISO-8601. At least one bound; `earliestUtc` ≤ `latestUtc`. Times without an offset are treated as UTC. |
| `items` | **yes** | At least one. See below. |
| `requestedTransportMode` | no | `AMR` (default) or `MANUAL`. **Decides how location codes are read** — see the next section. |
| `priority` | no | `LOW`, `NORMAL` (default), `HIGH`, `CRITICAL`. |
| `requestedBy` | no* | The person on your side, ≤ 200 chars. Shown on the order. *Required when `selfManaged` is true. |
| `notes` | no | ≤ 1000 chars. |
| `requiresPickupPod` / `requiresDropPod` | no | `true` makes an operator scan each item at that checkpoint. Default `false`. |
| `selfManaged` | no | `true` only with `MANUAL`: your system moves the goods and reports drop/complete through `/api/v1/source/trips/*`. |

Each **item**:

| field | required | notes |
|---|---|---|
| `itemId` | **yes** | Your lot / item id, ≤ 100 chars, unique within the order. |
| `pickupLocationCode`, `dropLocationCode` | **yes** | ≤ 50 chars each, and different from each other. Meaning depends on the mode. |
| `quantity` | **yes** | `{ value > 0, uom }` — `uom` is free text (`PCE`, `EA`, `KG`…), ≤ 20 chars. |
| `weightKg` | no | > 0. If missing the order is still accepted with a warning (see the response). |
| `description` | no | Free text. |
| `dimensions` | no | `{ lengthMm, widthMm, heightMm }`, all > 0. |
| `hazmat` | no | `{ classCode: "3" \| "2.1" …, packingGroup: "I" \| "II" \| "III" }`. |
| `temperature` | no | `{ minC, maxC }`, at least one. |
| `handlingInstructions` | no | Any of `FRAGILE`, `THIS_SIDE_UP`, `DO_NOT_STACK`, `HEAVY_LIFT`, `SHARP`, `KEEP_DRY`, `KEEP_DARK`, `PINCH_HAZARD`. |

Enum values are accepted in either `UPPER_SNAKE` or `PascalCase`; responses use
`UPPER_SNAKE`.

### AMR or MANUAL — the location codes are different

This is the most common cause of a refused order.

| mode | `pickupLocationCode` / `dropLocationCode` are | example |
|---|---|---|
| `AMR` (robot) | **DTMS station codes** — the robot map | `SHELF1`, `STF_05`, `WG1-PROD-001` |
| `MANUAL` (person) | **WMS location codes** | `LOC-000166`, `LOC-000307` |

A WMS display name is not a station code. On 2026-10-23 an order was sent as
`AMR` with drop `PREGNANT`: DTMS has a robot station called PREGNANT, but it is
switched off, while WMS has an active location *named* PREGNANT whose code is
`LOC-000312`. The order was refused (400). If the move is done by a person, send
`MANUAL` + `LOC-000312`.

For `AMR` we also need a robot program for every pickup → drop pair in the order
(see 422 below). Items that share a pickup → drop pair travel together as one
shipment; an order with two different pairs becomes two shipments.

---

## What you get back

### 201 — accepted

`Location: /api/v1/delivery-orders/{dtms-id}`

```json
{
  "order": { "id": "…", "orderRef": "OD-231026-0161-BUSRUN", "orderStatus": "CONFIRMED", "items": [ … ] },
  "warnings": [
    { "code": "ITEM_WEIGHT_MISSING", "severity": "warning", "field": "items[seq=1].weightKg",
      "message": "…" }
  ]
}
```

`warnings` never block the order. Today the only one is `ITEM_WEIGHT_MISSING`
(we plan with a default weight).

### Every answer

| code | `title` | meaning | retry? |
|---|---|---|---|
| **201** | — | Accepted. | — |
| **400** | `Idempotency-Key required` | Header missing (or longer than 200 chars). | Fix, then send. |
| **400** | `Validation Error` | Body breaks a rule above; `errors[]` lists each field. | **No** — fix the data. |
| **400** | `Business Rule Violation` | A location code is unknown, switched off or offline, or a `selfManaged` rule is broken. `detail` names it. | **No** — fix the data. |
| **401** | — | Token missing, expired, invalid, or your system is inactive. | Get a new token, then send. |
| **403** | — (empty body) | Your token lacks `dtms:source:{system}:order:write`. One click on our side. | **No** — tell us. |
| **409** | `Duplicate Value` | `orderRef` already exists for your system. | **No** — see [409](#409--this-orderref-already-exists). |
| **422** | `Route Not Dispatchable` | No usable robot program for a pickup → drop route. Nothing created. | **No** — see [422](#422--route-not-dispatchable). |
| **422** | `Idempotency-Key conflict` | You reused an `Idempotency-Key` with a **different** body. | **No** — new order, new key. |
| **422** | `Transport Mode Not Enabled` | The requested mode is switched off on our side. | **No** — tell us. |
| **429** | — | Rate limit. Read `Retry-After` (seconds). | **Yes**, after `Retry-After`, same key. |
| **5xx** / timeout / connection error | — | Our side or the network. | **Yes**, with backoff, **same key**. |

Errors are RFC 7807 problem documents. Read **`title`** to tell the kinds apart
(two different 422s exist) and **`detail`** for the human-readable reason; the
`error` member repeats `detail` only for older clients and will be removed.
5xx bodies carry a `traceId` — send it to us.

Typical `detail` texts:

```
400  'PREGNANT' is deactivated and cannot be used for new orders.
400  'SHELF9' is not a valid station code.
400  'STF_05' is currently offline (manual override): blocked aisle
400  'LOC-999' is not a valid WMS location.
400  'LOC-000312' is no longer active in WMS and cannot be used for new orders.
409  Order ref 'OD-231026-0161-BUSRUN' already exists for source system 'oms' (order …, CONFIRMED). …
422  Order 'OD-231026-0161-BUSRUN' can't be dispatched — SHELF3 → STF31_FRAME_IN: no active
     OrderTemplate is registered for this route. Add or activate an order template for the
     route, then send the order again.
```

### 422 — Route Not Dispatchable

The order's locations are valid, but DTMS has no robot program (order template)
for at least one pickup → drop pair, or the program is broken. We refuse the
**whole** order and name every failing route.

What to do: show the `detail` to your user, and ask the DTMS team to add the
template for that route. Once it exists, send the order again — **same
`orderRef`, new `Idempotency-Key`** (the key belongs to the refused request). No
409: nothing was created.

### 409 — this orderRef already exists

The order is already in DTMS. Two ways to get here:

- **A real duplicate** — you meant a new order but reused a reference. Use a new
  `orderRef`.
- **Your retry used a new `Idempotency-Key`.** The first request did succeed;
  you just didn't see the answer. Treat the 409 as "we already have it" — the
  `detail` includes our id and the current status. Avoid this by retrying with
  the **same** key (next section).

---

## Retrying safely

The `Idempotency-Key` is what makes a retry safe:

| your first request… | your retry with the **same key** gets |
|---|---|
| succeeded (201), but you lost the answer | the **same 201** replayed (header `Idempotency-Replayed: true`) — no second order |
| was refused (4xx) | the request evaluated again — same refusal unless you fixed something on our side |
| never reached us | normal processing |

- Keys are remembered for **24 hours** and only for **your** system — another
  system using the same value never sees your answer.
- Same key + different body = 422 `Idempotency-Key conflict`. A changed order is
  a new request: new key.
- Retry only network errors, timeouts, 5xx and 429 (after `Retry-After`). Use
  backoff (e.g. 2 s, 10 s, 30 s, 2 min). Do **not** auto-retry 400 / 409 / 422.

---

## After 201

The order is planned and handed to the robots (AMR) or the operator pool
(MANUAL). You hear about each shipment through the callbacks your system is
subscribed to — `shipment.started`, `shipment.pickedup` (pickup-arrived),
`shipment.droppedoff` — keyed by `shipmentId` and carrying your `orderRef`.

Not there yet: a callback when an accepted order **fails later** (robot fault,
vendor rejection). That needs a `/failed` route on your side; until it exists,
such orders are handled by DTMS operators (*Edit & resend* or cancel), and a
resend produces normal `shipment.*` callbacks.

To stop an accepted order, see [cancelling an order](oms-order-cancel-api.md).

---

## Notes for DTMS engineers

- Endpoint + body: [`SourceSystemDeliveryOrderEndpoints`](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Presentation/SourceSystemDeliveryOrderEndpoints.cs) (`CreateSourceOrderRequest`); handler: [`CreateUpstreamDeliveryOrderCommandHandler`](../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Commands/CreateUpstreamDeliveryOrder/CreateUpstreamDeliveryOrderCommandHandler.cs); body rules: `CreateUpstreamDeliveryOrderCommandValidator`
- Location messages come from `StationValidationService` (AMR stations vs WMS locations, by mode); `ClassifyFailure` maps them to the intake-metric reasons — keep the texts here in sync with it
- 422 route check: `IRouteDispatchGuard` → `IDispatchStrategy.CheckRoutesAsync` (template exists + resolves; nothing sent to RIOT3); `RouteNotDispatchableException` → 422 in `ExceptionHandlingMiddleware`
- Idempotency: `IdempotencyKeyFilter` — cache key `idempotency:v3:{caller}:{key}`, 24 h, only returned results (2xx) are replayed; thrown 4xx are re-evaluated. The filter's own 400/422 come from `Results.Problem` (no `error` shim)
- Refusals are counted in `dtms_order_intake_rejected_total{path="source"}` — Grafana *DTMS — Order Intake*
- Enum JSON: `JsonStringEnumConverter(SnakeCaseUpper)` in `Program.cs`; reads are case-insensitive
