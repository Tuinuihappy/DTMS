-- ============================================================================
-- Phase P1 backfill — seed deliveryorder.OrderStatusHistory with one
-- "initial" row per existing DeliveryOrder so the timeline endpoint
-- returns *something* for legacy data instead of an empty list.
--
-- Idempotent: ON CONFLICT skips rows already seeded. Safe to re-run.
--
-- One row per order:
--   FromStatus  = NULL  (intentional — see docs/event-projection-plan.md decision log)
--   ToStatus    = order's current status
--   OccurredAt  = COALESCE(UpdatedDate, CreatedDate)
--   Reason      = 'backfill-p1-b12'  (marker; ops can grep audit history later)
--
-- Future projection events will continue the chain from this seed row.
-- The corresponding inbox marker uses a deterministic UUID derived from
-- the order id so re-running this script does not register a second
-- "fake" event in the inbox.
-- ============================================================================

BEGIN;

INSERT INTO deliveryorder."OrderStatusHistory"
    ("Id", "EventId", "OrderId", "FromStatus", "ToStatus", "OccurredAt", "Reason")
SELECT
    gen_random_uuid(),
    -- Deterministic synthetic event id per order — derived from order id +
    -- a backfill nonce so a second run produces the same id and trips the
    -- uniqueness guards below.
    md5('p1-backfill:' || o."Id"::text)::uuid,
    o."Id",
    NULL,
    o."Status",
    COALESCE(o."UpdatedDate", o."CreatedDate"),
    'backfill-p1-b12'
FROM deliveryorder."DeliveryOrders" o
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."OrderStatusHistory" h
    WHERE h."OrderId" = o."Id"
);

-- Register the synthetic event id in the projector inbox so a real event
-- that happens to share the same EventId (vanishingly unlikely) does not
-- collide, and so a future replay of pre-backfill state recognises the
-- seed rows as already-processed.
INSERT INTO deliveryorder."ProjectionInbox"
    ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'OrderStatusHistoryProjector',
    md5('p1-backfill:' || o."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM deliveryorder."DeliveryOrders" o
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."ProjectionInbox" i
    WHERE i."ProjectorName" = 'OrderStatusHistoryProjector'
      AND i."EventId" = md5('p1-backfill:' || o."Id"::text)::uuid
);

-- Quick summary — should match: orders backfilled = rows added in this run
SELECT
    (SELECT COUNT(*) FROM deliveryorder."DeliveryOrders")          AS total_orders,
    (SELECT COUNT(*) FROM deliveryorder."OrderStatusHistory")      AS history_rows_total,
    (SELECT COUNT(*) FROM deliveryorder."OrderStatusHistory"
        WHERE "Reason" = 'backfill-p1-b12')                        AS history_rows_from_backfill;

COMMIT;
