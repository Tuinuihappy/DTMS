-- ============================================================================
-- Phase P5.3 backfill — seed dispatch."TripItems" by joining
-- dispatch."Trips" with deliveryorder."Items" and the owning
-- deliveryorder."DeliveryOrders" for OrderRef/Status snapshot.
--
-- Cross-schema join is intentional — this script runs as a one-shot
-- operator procedure, not as runtime code. The projector takes over
-- on-write for new trips.
--
-- Idempotent via ON CONFLICT (TripId, ItemPk) DO NOTHING — safe to
-- re-run after a fresh dispatch without wiping live writes.
--
-- Run order:
--   1. Apply migration 20260615021500_AddTripItemsProjection
--   2. Run this script
--   3. Spot-check: SELECT COUNT(*) FROM dispatch."TripItems";
--      should equal COUNT of (Trip, Item) pairs in the source tables.
-- ============================================================================

BEGIN;

INSERT INTO dispatch."TripItems"
    ("TripId", "ItemPk", "EventId",
     "DeliveryOrderId", "OrderRef", "OrderStatus",
     "LotNo", "ItemSeq", "ItemStatus",
     "PickupCode", "DropCode", "WeightKg",
     "BoundAt", "LastEventAt")
SELECT
    t."Id"                                AS "TripId",
    i."Id"                                AS "ItemPk",
    gen_random_uuid()                     AS "EventId",
    o."Id"                                AS "DeliveryOrderId",
    o."OrderRef"                          AS "OrderRef",
    o."Status"                            AS "OrderStatus",
    i."ItemId"                            AS "LotNo",
    i."ItemSeq"                           AS "ItemSeq",
    -- Re-derive a useful ItemStatus from the trip's terminal state so
    -- backfilled rows match what the projector would write for live events.
    CASE
        WHEN t."Status" = 'Completed' THEN 'Delivered'
        WHEN t."Status" IN ('Failed', 'Cancelled') THEN 'Unbound'
        ELSE i."Status"
    END                                   AS "ItemStatus",
    i."PickupLocationCode"                AS "PickupCode",
    i."DropLocationCode"                  AS "DropCode",
    i."WeightKg"                          AS "WeightKg",
    COALESCE(t."StartedAt", t."CreatedAt") AS "BoundAt",
    now() AT TIME ZONE 'UTC'              AS "LastEventAt"
FROM dispatch."Trips" t
JOIN deliveryorder."Items" i        ON i."TripId" = t."Id"
JOIN deliveryorder."DeliveryOrders" o ON o."Id" = t."DeliveryOrderId"
ON CONFLICT ("TripId", "ItemPk") DO NOTHING;

COMMIT;

-- Verification queries (run after commit):
--   SELECT COUNT(*) FROM dispatch."TripItems";
--   SELECT COUNT(*) FROM dispatch."Trips" t
--      JOIN deliveryorder."Items" i ON i."TripId" = t."Id";
-- The two counts should match (modulo any (TripId, ItemPk) duplicates
-- which DO NOTHING absorbed).
