# Draft: Phase S.5 — Unify OMS callbacks into the federated pipeline (fix B)

> Status: DRAFT for review. Supersedes the OMS-specific `UpstreamOms__Enabled`
> env kill-switch (P4 fix A) with uniform, data-driven per-system config.

## Context — why

OMS is the only source system with a **bespoke, hardcoded** outbound-callback path:

| Event | OMS today | Everyone else (erp/sap/…) |
|---|---|---|
| shipment **started** | legacy adapter, env `UpstreamOms__Enabled`, `POST /api/shipments` | — (no started callback exists) |
| shipment **arrived** | legacy adapter, `POST /api/shipments/{id}/arrived` | — |
| order **delivered** | **federated** (`oms.shipment.v1` formatter → `/events`) | federated |
| order **cancelled** | **federated** | federated |

Consequences of the split:
- Enabling/disabling/mocking OMS started+arrived is an **env var** (`UpstreamOms__*`), while every other callback is **data** (`SystemEventSubscriptions` + `SystemCredentials`). Two config surfaces, no uniformity — the reason P4 fix A is "OMS-specific."
- The `SourceSystemKey == Oms` gate is **duplicated inline in 4 places** ([TripStartedOmsNotifyConsumer:135](../../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Consumers/TripStartedOmsNotifyConsumer.cs#L135), [TripDropCompletedOmsNotifyConsumer:97](../../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Consumers/TripDropCompletedOmsNotifyConsumer.cs#L97), the two Resend handlers) and has already drifted.

**Goal:** move OMS started/arrived onto the federated pipeline so **every source system — OMS included — is configured the same way** (subscription `Enabled` + `SystemCredentials.CallbackBaseUrl`). Delete the OMS-special env + the 4 duplicated gates + the legacy adapter.

## Non-goals (explicit)

- **NOT** fixing the fabricated-lot 404 (P4 root cause). B unifies *config*; a test order with fake lots still 404s whatever OMS it hits. That stays a data/provenance concern (a separate `ExpectsSourceCallback` flag or mock target).
- **NOT** adding new business behavior — payloads stay byte-compatible with today's OMS contract.

## The core decision: URL contract — **LOCKED: B2 (keep OMS's existing paths)**

Federated dispatcher today posts to a **single** `POST {CallbackBaseUrl}/events`, header-routed by `X-DTMS-Event-Type` ([HttpSourceCallbackDispatcher:70-74](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/HttpSourceCallbackDispatcher.cs#L70-L74)). OMS legacy started/arrived use **distinct REST paths + shipmentId in the path**:
- started → `POST {base}/api/shipments` (id in **body**)
- arrived → `POST {base}/api/shipments/{shipmentId}/arrived` (id in **path**)
- delivered/cancelled (already federated) → `POST {base}/events` (id in body)

**Decision: B2 — the dispatcher keeps OMS's existing paths; no OMS-side change.** (B1, converging OMS onto `/events`, was considered and rejected to avoid an OMS-team contract change.) The dispatcher gains an optional per-message route (path + verb); when absent it falls back to today's `POST /events`, so delivered/cancelled and every future system are unaffected.

### B2 design detail

Only **arrived** needs a dynamic token (`{shipmentId}` in the path); started is a static path, delivered/cancelled stay `/events`. So the route is a property of *(formatter, event)* and, for arrived, of the concrete shipmentId — which the **formatter already has**. Keep substitution in the formatter and carry the *resolved* route to dispatch time:

1. **`CallbackPayload` gains the route** ([ICallbackPayloadFormatter.cs](../../src/Modules/Iam/DTMS.Iam.Application/Callbacks/ICallbackPayloadFormatter.cs)):
   ```
   record CallbackPayload(string ContentType, byte[] Body,
                          string? RelativePath = null,   // null → "/events"
                          string? HttpMethod = null);    // null → "POST"
   ```
   Formatters resolve `{shipmentId}` themselves, so the dispatcher never templates:
   - `OmsShipmentStartedFormatter` → `RelativePath = "/api/shipments"`
   - `OmsShipmentArrivedFormatter` → `RelativePath = $"/api/shipments/{rootTripId}/arrived"`
   - delivered/cancelled formatters → leave null (→ `/events`), unchanged.
2. **Outbox row carries the resolved route.** Add nullable `CallbackPath` + `CallbackMethod` to `outbox.OutboxMessages` (+ `OutboxMessage`), written by the fanout from the payload. Nullable = backward-compatible; existing rows and non-OMS systems default to `POST /events`. (Minor, acknowledged coupling of the generic outbox to a callback hint — the alternative, re-parsing the body in the dispatcher to rebuild the URL, is worse.)
3. **Dispatcher uses them** ([HttpSourceCallbackDispatcher:70-71](../../src/Modules/Iam/DTMS.Iam.Infrastructure/Callbacks/HttpSourceCallbackDispatcher.cs#L70-L71)):
   ```
   var path = string.IsNullOrEmpty(msg.CallbackPath) ? "/events" : msg.CallbackPath;
   var method = new HttpMethod(msg.CallbackMethod ?? "POST");
   var url = new Uri(cred.CallbackBaseUrl.TrimEnd('/') + path, UriKind.Absolute);
   ```
   Headers/auth/timeout/`EnsureSuccessStatusCode` retry path all stay identical.

Net: OMS receives byte-identical requests at its **existing** endpoints; DTMS config becomes uniform (subscription + credential).

## Concept mapping (legacy → federated)

| Legacy concern | Federated equivalent | Work needed |
|---|---|---|
| `UpstreamOms__Enabled` | `SystemEventSubscription.Enabled` per (oms, event) | seed subscriptions; delete env |
| `SourceSystemKey==Oms` gate (×4) | fanout source-routing (`SystemKey == order.SourceSystem`, [OrderDeliveredCallbackFanoutConsumer:69](../../src/DTMS.Api/Infrastructure/Callbacks/OrderDeliveredCallbackFanoutConsumer.cs#L69)) | delete the 4 inline gates |
| `UpstreamOms:BaseUrl` / target resolver | `SystemCredentials.CallbackBaseUrl` (oms already has one) | consolidate to credential |
| `IOmsShipmentClient` (started/arrived) | `ICallbackPayloadFormatter` + shared dispatcher | new formatters |
| retry + fast-cap (item 3) | `MultiPartitionOutboxProcessor` + `OutboxRetryPolicy` | map cap behavior (below) |
| audit `UpstreamOmsNotified/Rejected/Failed` + OrderActivity | **currently missing on federated path** | add (below) |
| Resend endpoints | re-enqueue outbox row / re-fire | new resend path |
| shipmentId = root trip id | formatter/fanout computes `GetRootTripIdAsync` | thread into fanout |
| `deliveryBy` = VendorVehicleName | formatter reads it from event/trip | ensure event carries name (below) |

## Hard parts (must design, not skip)

1. **`deliveryBy` timing (started).** Formatters run at **producer time** and the outbox freezes the bytes ([ICallbackPayloadFormatter](../../src/Modules/Iam/DTMS.Iam.Application/Callbacks/ICallbackPayloadFormatter.cs#L13-L16) — deterministic, never re-formatted). Legacy retries the whole consume to re-read the vehicle name. Post item-1/P1, the name is now set **atomically** with `MarkVendorStarted`, so at `TripStartedIntegrationEvent` time it's present — but the event must **carry `VendorVehicleName`** (today it carries the key only). → add `VendorVehicleName` to `TripStartedIntegrationEvent`, or have the started-fanout look the trip up before formatting. Keep item-3's "give up fast if never present" as a fanout-level guard.
2. **Audit / order-detail UI.** The frontend OMS notification section reads `OrderActivity` for `UpstreamOms*` rows. The federated dispatch path only logs — it does **not** write per-order audit. → the started/arrived fanout (or a dispatch-completion hook) must write the same audit/activity rows, or the UI loses OMS visibility. This is the biggest hidden dependency.
3. **Dedup semantics.** Arrived must fire once per shipment; started must skip pool trips. Fanout dedups on outbox `(PartitionKey, CorrelationId)`; P2 fire-once already collapses duplicate drop events. Preserve "pool trip already notified at dispatch" and "one arrived per root shipment" in the fanout/formatter.
4. **New event types are order-vs-trip.** Existing federated events (`order.delivered/cancelled`) are order-level; started/arrived are **trip-level** (`TripStartedIntegrationEvent` / `TripDropCompletedIntegrationEvent`). New fanout consumers subscribe to the **trip** events but must resolve the order's SourceSystem for routing. Fine, just note the join.

## Phased steps

**Phase 0 — DONE.** URL contract decided: **B2** (keep OMS paths). No OMS-team coordination needed.

**Phase 1 — additive, parallel, dark.** Nothing removed yet.
- **Route plumbing (B2):** extend `CallbackPayload` with `RelativePath` + `HttpMethod`; add nullable `CallbackPath` + `CallbackMethod` to `OutboxMessage` + `outbox.OutboxMessages` (migration); teach `HttpSourceCallbackDispatcher` to honor them (default `POST /events`).
- Add `CallbackEventTypes.ShipmentStartedV1` / `ShipmentArrivedV1` + to `All` ([CallbackEventTypes.cs](../../src/Modules/Iam/DTMS.Iam.Application/Callbacks/CallbackEventTypes.cs)).
- Add formatters `OmsShipmentStartedFormatter` (`RelativePath="/api/shipments"`, body = today's `OmsShipmentNotification`) / `OmsShipmentArrivedFormatter` (`RelativePath="/api/shipments/{rootTripId}/arrived"`, body = today's arrived) — **byte-identical** to legacy.
- Add fanout consumers for `TripStartedIntegrationEvent` / `TripDropCompletedIntegrationEvent` (mirror `OrderDeliveredCallbackFanoutConsumer`; source-routed; resolve rootTripId + deliveryBy; apply the legacy pool/self-managed/manual skips).
- Add the audit/activity write on dispatch success/failure (hard part #2).
- Guard the new fanout behind a flag (`Callbacks__ShipmentEventsEnabled=false`) so it's dark until verified.

**Phase 2 — verify byte-compat.** Diff the federated payload/headers vs legacy against a mock OMS `/events`. Confirm audit rows + UI unchanged. Run e2e (`scripts/e2e-single-group.sh`) with both paths and compare.

**Phase 3 — cut over.**
- Seed `SystemEventSubscriptions` rows for `(oms, shipment.started.v1)` + `(oms, shipment.arrived.v1)` (migration, respecting [[project_shared_migration_history]] + [[project_system_clients_seeded]] FK-safety).
- Flip the flag on; flip legacy `UpstreamOms__Enabled=false` (or gate the legacy consumers off).
- Run in production dark→live window; watch audit parity.

**Phase 4 — remove legacy.**
- Delete `TripStartedOmsNotifyConsumer` + fault, `TripDropCompletedOmsNotifyConsumer` + fault, `IOmsShipmentClient` + `HttpOmsShipmentClient`, `UpstreamOmsOptions` + env, `OmsCallbackTargetResolver` (if now unused), the 4 inline `SourceSystemKey==Oms` gates.
- Repoint `ResendOmsNotification` / `ResendOmsArrivedNotification` to re-enqueue the federated outbox row (or delete if the generic resend covers it).
- Remove `.env` / compose `UpstreamOms__*` (the P4 fix A lines).

## Files (indicative)

- **Add:** `CallbackEventTypes` (+2 consts); `OmsShipment{Started,Arrived}Formatter`; `Shipment{Started,Arrived}CallbackFanoutConsumer`; a dispatch-audit writer; migration for the 2 outbox columns; seed migration for oms subscriptions.
- **Change:** `CallbackPayload` (+RelativePath, +HttpMethod); `OutboxMessage` + `outbox.OutboxMessages` (+CallbackPath, +CallbackMethod); `HttpSourceCallbackDispatcher` (honor route, default `/events`); `TripStartedIntegrationEvent` (+VendorVehicleName); DI registration.
- **Remove (Phase 4):** the 2 legacy notify consumers + faults, `IOmsShipmentClient`/`HttpOmsShipmentClient`, `UpstreamOmsOptions`, the 4 gates, env/compose `UpstreamOms__*`.

## Verification

1. Unit: formatters produce byte-identical bodies to legacy (golden-file test); fanout source-routes + dedups.
2. Integration: mock OMS `/events` receives started/arrived with correct headers/body; audit + OrderActivity rows match legacy.
3. e2e: `scripts/e2e-single-group.sh` green on both paths; order-detail OMS notification UI identical.
4. Config parity: disabling the oms subscription (`PATCH .../subscriptions/{type} {enabled:false}`) suppresses the callback exactly like the old env flag did.

## Risks & rollback

- **No OMS-side change (B2):** OMS keeps its existing `/api/shipments` + `/{id}/arrived` endpoints — the byte-compat requirement (Phase 2) is what protects the contract.
- **Outbox coupling:** the 2 new nullable columns add a callback-routing hint to generic outbox infra. Contained (nullable, default `/events`); no impact on non-callback outbox use.
- **Audit UI regression:** easy to miss — the federated path doesn't write order audit today. Explicitly tested in Phase 2.
- **`deliveryBy` race:** relies on item-1/P1 having made the vehicle name atomic; verify the started event carries the name.
- **Rollback:** the flag + keeping legacy code until Phase 4 means cutover is reversible by flipping the flag back and re-enabling `UpstreamOms__Enabled`.

## Payoff

After Phase 4: **one** way to configure every source callback — a subscription row (`Enabled`) + a credential (`CallbackBaseUrl`/auth), managed via `/api/v1/iam/systems/{key}/subscriptions`. "Disable/mock OMS in dev" becomes the same operation as for erp/sap: no env var, no code special-case. The question "what about ERP/WMS?" stops existing.

---

## Progress (as implemented)

- **Phase 1 — DONE.** Route plumbing: `CallbackPayload` +RelativePath/HttpMethod; `OutboxMessage` +CallbackPath/CallbackMethod/RelatedOrderId/RelatedTripId (migration `20260708150000`, applied); dispatcher honors the route (default `POST /events`). Backward-compatible; unit-tested.
- **Phase 2 — DONE (dark).** 409-as-success (all systems); `CallbackEventTypes` +shipment.started/arrived.v1; `OmsShipment{Started,Arrived}Formatter` (byte-identical, tested); `Shipment{Started,Arrived}CallbackFanoutConsumer` (source-routed, enrich rootTripId/vehicleName/lots, legacy skips, flag-gated); `SourceCallbackOutcome` → `MultiPartitionOutboxProcessor` emit (terminal) → `SourceCallbackOutcomeConsumer` writes the UI audit. All behind `Callbacks:ShipmentEventsEnabled=false`.
- **Phase 3 — prepared (dev stays dark).** Seed migration `20260708160000` (oms shipment.started/arrived subscriptions, enabled) applied — inert while the flag is off. Fanout + outcome + byte-compat unit tests green. Dev keeps OMS callbacks OFF (P4 posture: WIP orders have fabricated lots). **Live cutover is prod-only** (below).

## Cutover checklist (prod)

Pre-flight (verify):
- Code with Phases 1–2 deployed; migrations `20260708150000` + `20260708160000` applied.
- `iam.SystemCredentials['oms']` has `CallbackBaseUrl` + `CallbackAuthScheme='bearer'` + a valid token (the same token OMS validates today). No new token needed.
- Decide the `X-DTMS-Event-Type/Event-Id/Correlation-Id` headers are harmless to OMS (it ignores unknown headers); if OMS strict-rejects, gate them off for oms first.

Cutover:
1. `Callbacks__ShipmentEventsEnabled=true` (api + outbox-worker).
2. `UpstreamOms__Enabled=false` (api + outbox-worker) — legacy adapter off.
3. Restart api + outbox-worker.
4. Verify one real trip: federated POSTs to OMS `/api/shipments` then `/api/shipments/{id}/arrived` (2xx/409); `OrderActivity` shows `UpstreamOmsNotified` / `UpstreamOmsArrivedNotified`; order-detail "Upstream OMS notification" UI unchanged.
5. Watch outbox dead-letters + OMS 4xx for the first window.

Rollback: `Callbacks__ShipmentEventsEnabled=false` + `UpstreamOms__Enabled=true` + restart → legacy resumes (both paths preserved until Phase 4).

Ops after cutover (uniform): disable OMS callbacks =
`PATCH /api/v1/iam/systems/oms/subscriptions/shipment.started.v1 {"enabled":false}` — identical to erp/sap.

## Phase 4 (later) — remove legacy
Delete the two `*OmsNotifyConsumer` + fault consumers, `IOmsShipmentClient`/`HttpOmsShipmentClient`, `UpstreamOmsOptions`, the 4 `SourceSystemKey==Oms` gates, env/compose `UpstreamOms__*`, and the transitional `Callbacks:ShipmentEventsEnabled` flag (subscription `Enabled` becomes the sole switch). Repoint the Resend* handlers to re-enqueue the federated outbox row.
