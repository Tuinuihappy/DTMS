# Runbook: Operator Pool Ops

Common ops procedures for the Manual/Fleet operator pool (introduced in [ADR-011](../multi-mode-transport/adr/adr-011-operator-pool-model.md)).

- **Owner:** solo dev (2026-07)
- **Depth:** dev-machine level — every command below assumes local Docker + admin DB access. Prod runbook is TODO once ops has DBA-level access.

---

## Table of contents

1. [Trip stuck in pool](#1-trip-stuck-in-pool)
2. [Reset a claimed trip back into the pool](#2-reset-a-claimed-trip-back-into-the-pool)
3. [Cancel a pooled trip (never assigned)](#3-cancel-a-pooled-trip-never-assigned)
4. [SignalR broadcast not reaching clients](#4-signalr-broadcast-not-reaching-clients)
5. [OMS notify duplicated / missing](#5-oms-notify-duplicated--missing)
6. [Inspect the pool right now](#6-inspect-the-pool-right-now)
7. [Auth reset (session 401 loop)](#7-auth-reset-session-401-loop)

---

## Pool predicate reference

Every check below relies on this predicate — memorize it:

```sql
Status = 'Created' AND DispatchedAt IS NOT NULL AND ClaimedByOperatorId IS NULL
```

Covered by partial index `dispatch."IX_Trips_Pool"`; queries against it are O(log n).

---

## 1. Trip stuck in pool

**Symptom:** A trip has been visible in `/m/pool` for hours and nobody's claimed it. Operators say "we saw it but nobody took it."

**Diagnose:**

```sql
SELECT "Id", "OrderRef", "DispatchedAt",
       NOW() - "DispatchedAt" AS waited,
       "PickupWmsLocationId", "DropWmsLocationId"
  FROM dispatch."Trips" t
  JOIN dispatch."TripItems" ti ON ti."TripId" = t."Id"
 WHERE t."Status" = 'Created'
   AND t."DispatchedAt" IS NOT NULL
   AND t."ClaimedByOperatorId" IS NULL
 ORDER BY t."DispatchedAt";
```

**Common causes:**
- **Nobody logged in.** Check `iam."Sessions"` for active session tokens in the last hour. If none, the pool is legitimately visible-to-zero-people.
- **Wrong WMS location.** Operators might not recognize the pickup code and pass on the card. Cross-check with the order upstream (OMS).
- **SignalR failed to broadcast.** See section 4.

**Fix:** No auto-fix. This is a demand-side problem — either ping operators, cancel the trip (section 3), or force-claim (section 2 in reverse: pick an operator ID and manually run the CAS).

---

## 2. Reset a claimed trip back into the pool

**Use case:** Operator claimed a trip by mistake, or an operator went offline mid-work and needs to hand off. Only use in dev / QA / during an incident with explicit ops approval.

```sql
BEGIN;

UPDATE dispatch."Trips"
   SET "Status"              = 'Created',
       "ClaimedByOperatorId" = NULL,
       "ClaimedAt"           = NULL,
       "StartedAt"           = NULL
 WHERE "Id" = '<tripId>';

DELETE FROM transportmanual."ManualTripExtensions"
 WHERE "TripId" = '<tripId>';

COMMIT;
```

Then broadcast a fake `PoolTripAdded` if any operator PWA is currently open, or ask them to refresh. (There is no `AdminReassignToPool` endpoint yet — deliberate, see ADR-011 §"Consequences".)

**Do not use in prod without approval.** Resetting `StartedAt` breaks Trip lifecycle history; consider `TripCancelled` + resubmit instead.

---

## 3. Cancel a pooled trip (never assigned)

**Use case:** Order was submitted in error, or ops decides not to fulfill.

Preferred path (once implemented — PR-G):
```
POST /api/v1/dispatch/pool/{tripId}/cancel
Body: { reason: "…" }
```

Current stopgap (direct DB):
```sql
UPDATE dispatch."Trips"
   SET "Status" = 'Cancelled',
       "EndedAt" = NOW()
 WHERE "Id" = '<tripId>'
   AND "ClaimedByOperatorId" IS NULL;
```

Then unblock the outbox to fan out `TripCancelled` (or let it propagate naturally):
```sql
-- Not usually needed; the aggregate would emit the event on save.
-- Only relevant if you edited the row directly.
```

Trigger a `PoolTripRemoved` SignalR broadcast (currently manual — pass through `IOperatorPoolBroadcaster.BroadcastRemovedAsync` from an admin tool):

```bash
# TODO once PR-G ships an admin endpoint.
```

---

## 4. SignalR broadcast not reaching clients

**Symptom:** Operator claims a trip via `/m/pool` and other operators' PWAs still show the card.

**Diagnose:**

```bash
# 1. Confirm the broadcast fired server-side.
docker logs dtms-api --since 5m 2>&1 | grep -E "\[PoolBroadcast\]"
```

Expected output on a successful claim:
```
[PoolBroadcast] Claimed Trip <tripId> by operator <opId> (<name>) → operator-pool
```

If missing:
- Handler didn't reach the broadcast line (check for exception before `_poolBroadcaster.BroadcastClaimedAsync(...)`).
- Broadcaster is null-injected (check DI registration in `ModuleServiceRegistration.cs` — search for `IOperatorPoolBroadcaster`).

If present but clients didn't receive:
- Redis backplane is off in multi-instance mode. Check `SignalR__UseRedisBackplane=true` in the env.
- Client dropped the connection. Ask operator to check the top-right pill in `/m/pool` — should read "Live" (green dot). If "Reconnecting…" (yellow), the WebSocket dropped.
- Auth issue. See section 7.

**Fix:** If the broadcaster is fine but clients disconnected, operators can force-refresh (Ctrl+R) — the reducer will `REFETCH` on reconnect.

---

## 5. OMS notify duplicated / missing

### Duplicated (rare — should be prevented by the DispatchedAt guard)

**Symptom:** OMS logs show two `POST /api/shipments` for the same `shipmentId` on a Manual pool trip.

**Diagnose:**
```bash
docker logs dtms-api --since 30m 2>&1 | grep -E "\[OmsAdapter\] POST.*<shipmentId>"
```

If two POSTs appear:
- Consumer's skip guard failed. Check the code: `if (eventType == "TripStarted" && trip?.DispatchedAt is not null) return;`
- Or the trip's `DispatchedAt` got NULLed post-claim (should never happen — investigate the SQL history).

**Fix:** File a bug. Manually PATCH OMS to correct the shipment record if operational.

### Missing (the common case)

**Symptom:** OMS says "we never got this shipment."

**Diagnose:**
```bash
docker logs dtms-api --since 30m 2>&1 | grep -E "\[OmsNotify\].*<tripId>|\[OmsAdapter\] POST.*shipmentId=<shipmentId>"
```

Look for:
- `outcome=Success` → we sent it; OMS side problem
- `outcome=Failed latencyMs=1000[0-5]` → 10s HTTP timeout, OMS unreachable
- No log at all → consumer never fired (check `dispatch."OutboxMessages"` for the event's `ProcessedOnUtc`)

**Fix by category:**
- Outbox row unprocessed → verify `dtms-outbox-worker` is running (`docker ps`)
- Outbox has `Error = "Type not found"` → outbox worker image is out of sync; run `docker compose up -d outbox-worker --force-recreate`
- HTTP timeout → confirm OMS URL in `iam."SystemCredentials"` where `SystemKey='oms'`
- Consumer threw but retry succeeded on its own → no action, retry policy handled it

To force-retry a stuck outbox row:
```sql
UPDATE dispatch."OutboxMessages"
   SET "NextRetryAtUtc" = NOW() - INTERVAL '1 minute',
       "RetryCount" = 0,
       "Error" = NULL
 WHERE "Id" = '<outboxRowId>';
```

---

## 6. Inspect the pool right now

```sql
-- Depth (single number)
SELECT COUNT(*)
  FROM dispatch."Trips"
 WHERE "Status" = 'Created'
   AND "DispatchedAt" IS NOT NULL
   AND "ClaimedByOperatorId" IS NULL;

-- Detailed view
SELECT t."Id"                                       AS "TripId",
       ti."OrderRef",
       ti."PickupCode",
       ti."DropCode",
       t."DispatchedAt",
       AGE(NOW(), t."DispatchedAt")                 AS "Waited",
       t."PriorityAtDispatch"
  FROM dispatch."Trips" t
  LEFT JOIN dispatch."TripItems" ti ON ti."TripId" = t."Id"
 WHERE t."Status" = 'Created'
   AND t."DispatchedAt" IS NOT NULL
   AND t."ClaimedByOperatorId" IS NULL
 ORDER BY t."DispatchedAt";

-- Active operators (who could claim right now)
SELECT COUNT(*)
  FROM transportmanual."Operators"
 WHERE "Status" = 'Active';

-- Currently claimed trips (who's working on what)
SELECT t."Id"                                       AS "TripId",
       o."DisplayName"                              AS "Operator",
       t."ClaimedAt",
       AGE(NOW(), t."ClaimedAt")                    AS "InProgressFor"
  FROM dispatch."Trips" t
  JOIN transportmanual."Operators" o ON o."Id" = t."ClaimedByOperatorId"
 WHERE t."Status" IN ('InProgress', 'Paused')
 ORDER BY t."ClaimedAt";
```

---

## 7. Auth reset (session 401 loop)

**Symptom:** Operator PWA loads `/m/pool`, sees the page shell, but every REST call returns 401. Log shows `IDX10517: Signature validation failed. The token's kid is missing.`

**Cause:** Session JWT expired (typical lifetime: 1h from External Auth); the frontend has a stale cookie.

**Fix:**
1. In the operator PWA: click avatar → **Sign out**.
2. Sign in again with real LDAP credentials.
3. Retry `/m/pool`.

**Do not**: re-enable `DTMS_AUTH_BYPASS=true` to work around a 401. That flag bypasses LDAP entirely and lets any password log in as anyone; it was explicitly turned off on 2026-07-03 (see [project_auth_bypass_disabled](../../../.claude/projects/d--DTMS/memory/project_auth_bypass_disabled.md) if this repo has it, or ask the dev).

**If LDAP is unreachable in dev**: temporarily re-enable bypass in `.env` (`DTMS_AUTH_BYPASS=true`), recreate the frontend container (`docker compose --profile prod up -d frontend --force-recreate`), do your test, then revert to `false` and recreate again.
