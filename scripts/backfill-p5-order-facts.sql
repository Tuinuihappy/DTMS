-- ============================================================================
-- Phase P5 backfill — seed bi."OrderFacts" by pivoting deliveryorder.OrderStatusHistory
-- (P1) over each DeliveryOrder, so historical orders show up in reports the
-- moment P5 ships.
--
-- One row per DeliveryOrder. For each status the row already passed through,
-- the corresponding timestamp column is filled in from the *first* StatusHistory
-- entry that records the transition. Subsequent transitions to the same status
-- (e.g. Held → Released → Held again) keep the original timestamp — that
-- matches the projector's behaviour, which would only overwrite if the second
-- event has a newer OccurredAt (and the projector currently uses event time as
-- the source of truth, so this is fine).
--
-- Idempotent: TRUNCATE + INSERT.
-- ============================================================================

BEGIN;

TRUNCATE bi."OrderFacts";

WITH pivot AS (
    SELECT
        h."OrderId" AS order_id,
        MIN(CASE WHEN h."ToStatus" = 'Submitted'           THEN h."OccurredAt" END) AS submitted_at,
        MIN(CASE WHEN h."ToStatus" = 'Confirmed'           THEN h."OccurredAt" END) AS confirmed_at,
        MIN(CASE WHEN h."ToStatus" = 'Dispatched'          THEN h."OccurredAt" END) AS dispatched_at,
        MIN(CASE WHEN h."ToStatus" = 'InProgress'          THEN h."OccurredAt" END) AS in_progress_at,
        MIN(CASE WHEN h."ToStatus" = 'Completed'           THEN h."OccurredAt" END) AS completed_at,
        MIN(CASE WHEN h."ToStatus" = 'PartiallyCompleted'  THEN h."OccurredAt" END) AS partially_completed_at,
        MIN(CASE WHEN h."ToStatus" = 'Failed'              THEN h."OccurredAt" END) AS failed_at,
        MIN(CASE WHEN h."ToStatus" = 'Cancelled'           THEN h."OccurredAt" END) AS cancelled_at,
        MIN(CASE WHEN h."ToStatus" = 'Rejected'            THEN h."OccurredAt" END) AS rejected_at,
        MIN(CASE WHEN h."ToStatus" = 'Held'                THEN h."OccurredAt" END) AS held_at,
        MIN(CASE WHEN h."ToStatus" = 'Released'            THEN h."OccurredAt" END) AS released_at,
        MAX(h."OccurredAt") AS last_event_at
    FROM deliveryorder."OrderStatusHistory" h
    GROUP BY h."OrderId"
),
failure_reason AS (
    -- Pull the latest failure-bearing event so FailureReason matches the
    -- terminal explanation the order ended in.
    SELECT DISTINCT ON (h."OrderId")
        h."OrderId" AS order_id,
        h."Reason"  AS reason
    FROM deliveryorder."OrderStatusHistory" h
    WHERE h."ToStatus" IN ('Failed', 'Cancelled', 'Rejected', 'Held')
    ORDER BY h."OrderId", h."OccurredAt" DESC
)
INSERT INTO bi."OrderFacts" (
    "OrderId", "OrderRef", "SourceSystem", "Priority", "TransportMode",
    "RequestedBy", "FinalStatus", "FailureReason",
    "TotalItems", "TotalQuantity", "TotalWeightKg",
    "CreatedAt", "SubmittedAt", "ConfirmedAt", "DispatchedAt", "InProgressAt",
    "CompletedAt", "PartiallyCompletedAt", "FailedAt", "CancelledAt", "RejectedAt",
    "HeldAt", "ReleasedAt",
    "UpdatedAt"
)
SELECT
    o."Id",
    o."OrderRef",
    o."SourceSystem",
    o."Priority",
    o."RequestedTransportMode",
    o."RequestedBy",
    o."Status",
    fr.reason,
    o."TotalItems",
    o."TotalQuantity",
    o."TotalWeightKg",
    o."CreatedDate",
    COALESCE(p.submitted_at,           o."SubmittedAt"),
    p.confirmed_at,
    p.dispatched_at,
    p.in_progress_at,
    p.completed_at,
    p.partially_completed_at,
    p.failed_at,
    p.cancelled_at,
    p.rejected_at,
    p.held_at,
    p.released_at,
    COALESCE(p.last_event_at, o."UpdatedDate", o."CreatedDate")
FROM deliveryorder."DeliveryOrders" o
LEFT JOIN pivot p           ON p.order_id = o."Id"
LEFT JOIN failure_reason fr ON fr.order_id = o."Id";

-- Register synthetic inbox markers so a replay of the historical events the
-- pivot just consumed doesn't double-write rows (the dedup table is per
-- projector + per EventId; one placeholder per order is enough — backfill
-- handles every event with one synthetic id).
INSERT INTO deliveryorder."ProjectionInbox" ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'OrderFactsProjector',
    md5('p5-backfill:' || o."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM deliveryorder."DeliveryOrders" o
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."ProjectionInbox" i
    WHERE i."ProjectorName" = 'OrderFactsProjector'
      AND i."EventId" = md5('p5-backfill:' || o."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM deliveryorder."DeliveryOrders") AS source_orders,
    (SELECT COUNT(*) FROM bi."OrderFacts")                AS bi_rows,
    (SELECT COUNT(*) FROM bi."OrderFacts" WHERE "ConfirmedAt" IS NOT NULL)        AS confirmed_orders,
    (SELECT COUNT(*) FROM bi."OrderFacts" WHERE "CompletedAt" IS NOT NULL)        AS completed_orders,
    (SELECT COUNT(*) FROM bi."OrderFacts" WHERE "SlaConfirmBreached" = true)      AS sla_confirm_breach,
    (SELECT COUNT(*) FROM bi."OrderFacts" WHERE "SlaCompleteBreached" = true)     AS sla_complete_breach;

COMMIT;
