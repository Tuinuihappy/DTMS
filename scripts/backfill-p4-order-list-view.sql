-- ============================================================================
-- Phase P4 backfill — seed deliveryorder."OrderListView" with one row per
-- existing DeliveryOrder, joining trips/jobs/items to compute the
-- derived fields the projector maintains going forward.
--
-- Idempotent: TRUNCATE-then-insert, so re-running just reflects the
-- latest write-side snapshot. The projector handles new transitions
-- after this script runs.
--
-- SearchText concatenates the same fields the projector emits at
-- Confirmed time, plus every item id under the order so a search like
-- "SKU-123" finds the parent order.
-- ============================================================================

BEGIN;

TRUNCATE deliveryorder."OrderListView";

INSERT INTO deliveryorder."OrderListView" (
    "OrderId", "OrderRef",
    "Status", "SourceSystem", "Priority", "TransportMode",
    "HasFailedTrip", "HasActiveJob", "LatestTripId", "LatestJobStatus",
    "RequestedBy", "CreatedBy", "Notes",
    "TotalItems", "TotalQuantity", "TotalWeightKg",
    "RequiresDropPod", "RequiresPickupPod",
    "CreatedAt", "UpdatedAt", "SubmittedAt",
    "ServiceWindowEarliestUtc", "ServiceWindowLatestUtc",
    "SearchText"
)
SELECT
    o."Id",
    o."OrderRef",
    o."Status",
    o."SourceSystem",
    o."Priority",
    o."RequestedTransportMode",
    COALESCE(t.has_failed_trip, false),
    COALESCE(j.has_active_job, false),
    t.latest_trip_id,
    j.latest_job_status,
    o."RequestedBy",
    o."CreatedBy",
    o."Notes",
    o."TotalItems",
    o."TotalQuantity",
    o."TotalWeightKg",
    o."RequiresDropPod",
    o."RequiresPickupPod",
    o."CreatedDate",
    o."UpdatedDate",
    o."SubmittedAt",
    o."ServiceWindow_EarliestUtc",
    o."ServiceWindow_LatestUtc",
    -- SearchText: every column ops actually scan for + item ids so a
    -- raw "SKU-123" or station-code lookup finds the parent order.
    concat_ws(' ',
        o."OrderRef",
        o."RequestedBy",
        o."CreatedBy",
        o."Notes",
        items.item_text,
        items.pickup_codes,
        items.drop_codes
    )
FROM deliveryorder."DeliveryOrders" o
LEFT JOIN LATERAL (
    -- Per-order trip aggregation: any non-terminal-good ever = HasFailedTrip
    SELECT
        bool_or(tr."Status" IN ('Failed', 'Cancelled'))                          AS has_failed_trip,
        (SELECT t2."Id" FROM dispatch."Trips" t2
            WHERE t2."DeliveryOrderId" = o."Id"
            ORDER BY t2."CreatedAt" DESC LIMIT 1)                                AS latest_trip_id
    FROM dispatch."Trips" tr
    WHERE tr."DeliveryOrderId" = o."Id"
) t ON true
LEFT JOIN LATERAL (
    -- Per-order job aggregation: any non-terminal currently = HasActiveJob
    SELECT
        bool_or(jb."Status" IN ('Created','Assigned','Committed','Dispatched','Executing'))  AS has_active_job,
        (SELECT j2."Status" FROM planning."Jobs" j2
            WHERE j2."DeliveryOrderId" = o."Id"
            ORDER BY j2."CreatedAt" DESC LIMIT 1)                                AS latest_job_status
    FROM planning."Jobs" jb
    WHERE jb."DeliveryOrderId" = o."Id"
) j ON true
LEFT JOIN LATERAL (
    -- Item-level free text: concat ItemIds + pickup/drop codes for full-text search
    SELECT
        string_agg(i."ItemId", ' ')           AS item_text,
        string_agg(i."PickupLocationCode", ' ') AS pickup_codes,
        string_agg(i."DropLocationCode", ' ')   AS drop_codes
    FROM deliveryorder."Items" i
    WHERE i."DeliveryOrderId" = o."Id"
) items ON true;

-- Register synthetic inbox markers so a replay of historical events
-- through the projector doesn't double-write rows (the dedup table is
-- per-projector + per-EventId, so we only need a placeholder per order).
-- We use the deterministic ProjectionInbox shape from P0/P1.
INSERT INTO deliveryorder."ProjectionInbox" ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'OrderListViewProjector',
    md5('p4-backfill:' || o."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM deliveryorder."DeliveryOrders" o
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."ProjectionInbox" i
    WHERE i."ProjectorName" = 'OrderListViewProjector'
      AND i."EventId" = md5('p4-backfill:' || o."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM deliveryorder."DeliveryOrders")     AS source_orders,
    (SELECT COUNT(*) FROM deliveryorder."OrderListView")      AS list_view_rows,
    (SELECT COUNT(*) FROM deliveryorder."OrderListView" WHERE "HasFailedTrip") AS orders_with_failed_trip,
    (SELECT COUNT(*) FROM deliveryorder."OrderListView" WHERE "HasActiveJob")  AS orders_with_active_job;

COMMIT;
