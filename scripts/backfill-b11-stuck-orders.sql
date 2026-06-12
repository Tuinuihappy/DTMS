-- ============================================================================
-- Phase b11 backfill: terminate orders stranded at an in-flight status with
-- zero active trips remaining (every Trip already Completed/Failed/Cancelled).
--
-- Pre-b11 behavior: TripCancelledConsumer released items back to Pending so
-- the operator could /retry the trip. If no retry came, the order sat at
-- Dispatched forever — RecomputeStatusFromItems treats Pending items as
-- in-flight and won't auto-terminate.
--
-- Post-b11 fix: TripCancelledConsumer now cascades Order→Cancelled when no
-- active sibling trip remains. This script handles legacy rows that landed
-- in the stuck state before the fix was deployed.
--
-- Equivalent to calling POST /api/v1/delivery-orders/{id}/abandon-after-trip-cancel
-- for each stuck order, but in bulk with a single transaction.
--
-- USAGE:
--   1. Connect as the application DB user (NOT a superuser):
--        psql -h <host> -U <app-user> -d amr_delivery_planning
--   2. Run the PREVIEW block first to verify the candidate set
--   3. Set actor name on the next line — appears in the audit log:
--        \set actor 'ops-lead-01 (b11 cleanup)'
--   4. Run the EXECUTE block in a transaction
--
-- SAFETY: Wrapped in BEGIN/COMMIT — review row counts before COMMIT.
-- Idempotent: re-running this script does nothing (terminal orders are
-- already excluded by the WHERE clause).
-- ============================================================================

-- ── PREVIEW ─────────────────────────────────────────────────────────────────
-- Run this query first to see what would be terminated. Expected to match
-- the roadmap finding (~3 orders as of 2026-06-11).

SELECT
    o."Id",
    o."OrderRef",
    o."Status"          AS current_status,
    o."CreatedDate",
    COUNT(t."Id")                                                     AS total_trips,
    COUNT(t."Id") FILTER (WHERE t."Status" IN ('Created','InProgress','Paused')) AS active_trips,
    COUNT(t."Id") FILTER (WHERE t."Status" = 'Cancelled') AS cancelled_trips,
    COUNT(t."Id") FILTER (WHERE t."Status" = 'Failed')    AS failed_trips,
    COUNT(t."Id") FILTER (WHERE t."Status" = 'Completed') AS completed_trips,
    COUNT(i."Id") FILTER (WHERE i."Status" = 'Pending' AND i."TripId" IS NULL) AS stranded_items
FROM deliveryorder."DeliveryOrders" o
LEFT JOIN dispatch."Trips" t ON t."DeliveryOrderId" = o."Id"
LEFT JOIN deliveryorder."Items" i ON i."DeliveryOrderId" = o."Id"
WHERE o."Status" IN ('Confirmed','Planning','Planned','Dispatched','InProgress')
GROUP BY o."Id", o."OrderRef", o."Status", o."CreatedDate"
HAVING COUNT(t."Id") > 0                                          -- must have trips
   AND COUNT(t."Id") FILTER (WHERE t."Status" IN ('Created','InProgress','Paused')) = 0  -- none active
ORDER BY o."CreatedDate";

-- ── EXECUTE ─────────────────────────────────────────────────────────────────
-- Uncomment the block below + set :actor, then run as a unit.

-- \set actor 'ops-cleanup-b11'
-- BEGIN;
--
-- WITH stuck AS (
--     SELECT o."Id" AS order_id, o."OrderRef" AS order_ref
--     FROM deliveryorder."DeliveryOrders" o
--     LEFT JOIN dispatch."Trips" t ON t."DeliveryOrderId" = o."Id"
--     WHERE o."Status" IN ('Confirmed','Planning','Planned','Dispatched','InProgress')
--     GROUP BY o."Id", o."OrderRef"
--     HAVING COUNT(t."Id") > 0
--        AND COUNT(t."Id") FILTER (WHERE t."Status" IN ('Created','InProgress','Paused')) = 0
-- ),
-- order_update AS (
--     UPDATE deliveryorder."DeliveryOrders" o
--     SET "Status" = 'Cancelled',
--         "UpdatedDate" = NOW() AT TIME ZONE 'UTC'
--     WHERE o."Id" IN (SELECT order_id FROM stuck)
--     RETURNING o."Id"
-- ),
-- item_update AS (
--     -- Stranded items (no Trip binding, not yet terminal) must follow the
--     -- order to a terminal state — leaving them Pending strands them
--     -- because the order won't dispatch again.
--     UPDATE deliveryorder."Items" i
--     SET "Status" = 'Cancelled'
--     WHERE i."DeliveryOrderId" IN (SELECT order_id FROM stuck)
--       AND i."TripId" IS NULL
--       AND i."Status" NOT IN ('Delivered','Failed','Returned','Cancelled')
--     RETURNING i."Id"
-- )
-- INSERT INTO deliveryorder."OrderAuditEvents" (
--     "Id", "DeliveryOrderId", "EventType", "Details", "ActorId", "OccurredAt"
-- )
-- SELECT
--     gen_random_uuid(),
--     s.order_id,
--     'OrderAbandoned',
--     format('Order ''%s'' abandoned by %s: Phase b11 backfill — every Trip terminal, no active dispatch (stranded items terminated)',
--            s.order_ref, :'actor'),
--     :'actor',
--     NOW() AT TIME ZONE 'UTC'
-- FROM stuck s;
--
-- -- Verify row counts match the preview before COMMIT
-- SELECT COUNT(*) AS audit_rows_inserted FROM deliveryorder."OrderAuditEvents"
--  WHERE "EventType" = 'OrderAbandoned'
--    AND "OccurredAt" > NOW() AT TIME ZONE 'UTC' - interval '1 minute';
--
-- COMMIT;
