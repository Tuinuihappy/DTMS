-- ============================================================================
-- Phase P3 backfill — seed deliveryorder."OrderFunnelHourly" from
-- existing OrderStatusHistory rows (which P1 already populated). Each
-- history row's ToStatus contributes +1 to its hour bucket's matching
-- column. Re-uses the P1 work so we don't need to re-derive from raw
-- audit events.
--
-- Idempotent: ON CONFLICT (BucketHour) DO UPDATE adds the counts on top
-- of any existing row, so running this twice would double-count. The
-- script wipes the funnel table first to keep semantics simple — re-run
-- whenever the source projection or backfill criteria change.
-- ============================================================================

BEGIN;

-- Clear existing — backfill is destructive by design (see header).
TRUNCATE deliveryorder."OrderFunnelHourly";

-- Aggregate OrderStatusHistory → hour buckets × status, then pivot the
-- result into the wide row shape OrderFunnelHourly expects.
INSERT INTO deliveryorder."OrderFunnelHourly"
    ("Id", "BucketHour",
     "Confirmed", "Dispatched", "InProgress",
     "Completed", "PartiallyCompleted",
     "Failed", "Cancelled", "Rejected",
     "Held", "Released")
SELECT
    gen_random_uuid(),
    agg.bucket_hour,
    agg.confirmed, agg.dispatched, agg.in_progress,
    agg.completed, agg.partially_completed,
    agg.failed, agg.cancelled, agg.rejected,
    agg.held, agg.released
FROM (
    SELECT
        date_trunc('hour', "OccurredAt") AS bucket_hour,
        SUM(CASE WHEN "ToStatus" = 'Confirmed'          THEN 1 ELSE 0 END)::int AS confirmed,
        SUM(CASE WHEN "ToStatus" = 'Dispatched'         THEN 1 ELSE 0 END)::int AS dispatched,
        SUM(CASE WHEN "ToStatus" = 'InProgress'         THEN 1 ELSE 0 END)::int AS in_progress,
        SUM(CASE WHEN "ToStatus" = 'Completed'          THEN 1 ELSE 0 END)::int AS completed,
        SUM(CASE WHEN "ToStatus" = 'PartiallyCompleted' THEN 1 ELSE 0 END)::int AS partially_completed,
        SUM(CASE WHEN "ToStatus" = 'Failed'             THEN 1 ELSE 0 END)::int AS failed,
        SUM(CASE WHEN "ToStatus" = 'Cancelled'          THEN 1 ELSE 0 END)::int AS cancelled,
        SUM(CASE WHEN "ToStatus" = 'Rejected'           THEN 1 ELSE 0 END)::int AS rejected,
        SUM(CASE WHEN "ToStatus" = 'Held'               THEN 1 ELSE 0 END)::int AS held,
        SUM(CASE WHEN "ToStatus" = 'Released'           THEN 1 ELSE 0 END)::int AS released
    FROM deliveryorder."OrderStatusHistory"
    WHERE "Reason" IS DISTINCT FROM 'backfill-p1-b12'   -- skip the P1 seed marker rows;
                                                         -- they aren't real transitions
    GROUP BY date_trunc('hour', "OccurredAt")
) AS agg;

-- Register synthetic inbox markers — one per source row — so a future
-- replay of OrderStatusHistory events through the projector skips
-- double-counting. The projector's bucket math is deterministic so a
-- specific EventId always lands in a specific bucket.
INSERT INTO deliveryorder."ProjectionInbox"
    ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'OrderFunnelProjector',
    h."EventId",
    NOW() AT TIME ZONE 'UTC'
FROM deliveryorder."OrderStatusHistory" h
WHERE h."Reason" IS DISTINCT FROM 'backfill-p1-b12'
  AND NOT EXISTS (
    SELECT 1 FROM deliveryorder."ProjectionInbox" i
    WHERE i."ProjectorName" = 'OrderFunnelProjector'
      AND i."EventId" = h."EventId"
);

-- Sanity summary
SELECT
    (SELECT COUNT(*) FROM deliveryorder."OrderStatusHistory"
        WHERE "Reason" IS DISTINCT FROM 'backfill-p1-b12') AS source_status_rows,
    (SELECT COUNT(*) FROM deliveryorder."OrderFunnelHourly") AS funnel_buckets_total,
    (SELECT MIN("BucketHour") FROM deliveryorder."OrderFunnelHourly") AS earliest_bucket,
    (SELECT MAX("BucketHour") FROM deliveryorder."OrderFunnelHourly") AS latest_bucket,
    (SELECT SUM("Confirmed" + "Dispatched" + "InProgress" + "Completed"
              + "PartiallyCompleted" + "Failed" + "Cancelled" + "Rejected"
              + "Held" + "Released")
        FROM deliveryorder."OrderFunnelHourly") AS total_transitions_counted;

COMMIT;
