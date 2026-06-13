-- ============================================================================
-- Phase C — Demo seed: 30 orders distributed across the last 7 days with
-- enough variety that every /reports tab paints meaningful charts.
--
-- Distribution (loosely):
--   - 18 Completed (mix of fast / medium / slow lead times)
--   - 3 PartiallyCompleted
--   - 2 Failed (vendor)
--   - 2 Cancelled (operator)
--   - 1 Held
--   - 2 InProgress (live)
--   - 1 Dispatched (live)
--   - 1 Confirmed (waiting on planner)
--
-- For Completed / Failed / Cancelled / InProgress / Dispatched orders we
-- also synthesize:
--   - a planning.Jobs row (one per order)
--   - a dispatch.Trips row with a vendor key from {RIOT3-AAA, RIOT3-BBB,
--     RIOT3-CCC} so the Vendor performance report has multiple buckets
--   - cascading status-history rows in both Job + Trip tables
--
-- Idempotent-ish: re-running adds 30 MORE rows (OrderRef is UNIQUE per
-- SourceSystem so the second run will hit a unique violation). To
-- repeat: DELETE FROM deliveryorder."DeliveryOrders" WHERE
-- "OrderRef" LIKE 'DEMO-%'; then re-run.
--
-- After this script:
--   1. Run scripts/backfill-p1-*.sql (Order / Job / Trip status history)
--   2. Run scripts/backfill-p2-order-activity.sql
--   3. Run scripts/backfill-p3-order-funnel-hourly.sql
--   4. Run scripts/backfill-p4-order-list-view.sql
--   5. Run scripts/backfill-p5-order-facts.sql
--   6. Run scripts/backfill-p5-trip-facts.sql
--   7. Run scripts/backfill-p5-job-facts.sql
-- ============================================================================

BEGIN;

-- Cache the run anchor so every "minutes ago" is consistent across CTEs.
-- All timestamps below are derived from this anchor, never re-reading
-- NOW() (which would drift across statements in the same transaction).
WITH cfg AS (SELECT NOW() AT TIME ZONE 'UTC' AS now_utc),

-- ── 30 base orders with deterministic IDs derived from md5(seed) ────────
seed AS (
    SELECT
        n,
        -- Stable UUIDs derived from seed → re-runs after a DELETE produce
        -- the same IDs, which is handy for diff-ing reports between runs.
        md5('demo-order:' || n)::uuid AS order_id,
        'DEMO-' || LPAD(n::text, 3, '0') AS order_ref,
        c.now_utc
    FROM cfg c, generate_series(1, 30) AS n
),

-- ── Per-order attribute matrix: status, priority, vendor, timing ────────
-- Index n → deterministic bucket.
-- Day offset: orders 1-5 = -6d, 6-10 = -5d, ..., 26-30 = -1d
-- so the trailing-7d window catches all 30.
orders AS (
    SELECT
        s.order_id,
        s.order_ref,
        -- Status mix — encoded as a static lookup keyed by n.
        CASE
            WHEN n IN (1, 2, 3, 4, 5, 7, 8, 9, 11, 12, 13, 14, 16, 17, 18, 21, 22, 26)
                THEN 'Completed'
            WHEN n IN (6, 19, 23)        THEN 'PartiallyCompleted'
            WHEN n IN (10, 24)           THEN 'Failed'
            WHEN n IN (15, 25)           THEN 'Cancelled'
            WHEN n = 20                  THEN 'Held'
            WHEN n IN (27, 28)           THEN 'InProgress'
            WHEN n = 29                  THEN 'Dispatched'
            WHEN n = 30                  THEN 'Confirmed'
        END                              AS status,
        -- Priority mix — drives the "Orders by priority" + SLA reports.
        CASE
            WHEN n IN (4, 10, 17, 25)                       THEN 'Critical'
            WHEN n IN (2, 7, 11, 15, 19, 22, 26, 28, 30)    THEN 'High'
            WHEN n IN (1, 5, 8, 12, 14, 16, 18, 21, 23, 27, 29, 6) THEN 'Normal'
            WHEN n IN (3, 9, 13, 20, 24)                    THEN 'Low'
        END                              AS priority,
        -- Source mix.
        CASE WHEN n % 3 = 0 THEN 'Sap' ELSE 'Manual' END AS source_system,
        -- Vendor key per order (jobs/trips inherit it).
        -- RIOT3-AAA: 60%, RIOT3-BBB: 27%, RIOT3-CCC: 13%
        CASE
            WHEN n IN (3, 6, 13, 22)        THEN 'RIOT3-CCC'
            WHEN n IN (2, 5, 9, 14, 18, 25, 28) THEN 'RIOT3-BBB'
            ELSE 'RIOT3-AAA'
        END                              AS vendor_key,
        -- CreatedDate spreads across last 7d with hour-of-day variance.
        s.now_utc - (((30 - n) * 5.5 + (n * 1.3)) || ' hours')::interval AS created_at,
        s.now_utc                        AS now_utc,
        n
    FROM seed s
),

-- ── Lifecycle timestamp matrix: derives Submitted/Confirmed/Dispatched/
--    Completed offsets per status so reports have realistic durations
--    + SLA breach rows + lead-time distribution spread.
lifecycle AS (
    SELECT
        o.*,
        -- Submitted: 1-30 min after created.
        o.created_at + (((n % 10) + 1) || ' minutes')::interval AS submitted_at,
        -- Confirmed: most fast (<30min). n IN (4, 17) breach 4h SLA.
        CASE
            WHEN n IN (4, 17) THEN o.created_at + interval '5 hours'
            ELSE o.created_at + (((n % 25) + 5) || ' minutes')::interval
        END                              AS confirmed_at,
        -- Dispatched: 5-15 min after confirmed.
        CASE
            WHEN n IN (4, 17) THEN o.created_at + interval '5 hours 8 minutes'
            ELSE o.created_at + (((n % 25) + 5) || ' minutes')::interval
                                          + ((5 + (n % 10)) || ' minutes')::interval
        END                              AS dispatched_at,
        -- TimeToComplete spread — fast / medium / slow buckets.
        CASE
            WHEN n IN (1, 7, 12, 18)                THEN interval '25 minutes'   -- fast
            WHEN n IN (2, 8, 13, 21)                THEN interval '55 minutes'
            WHEN n IN (3, 9, 14, 22)                THEN interval '2 hours 10 minutes'  -- medium
            WHEN n IN (4, 11)                       THEN interval '6 hours'
            WHEN n IN (5, 16, 17)                   THEN interval '3 hours 30 minutes'
            WHEN n IN (26)                          THEN interval '14 hours'     -- slow
            ELSE                                          interval '28 hours'    -- 24h+ SLA breach
        END                              AS time_to_complete
    FROM orders o
)

INSERT INTO deliveryorder."DeliveryOrders" (
    "Id", "OrderRef", "SourceSystem", "Priority", "Status",
    "CreatedDate", "UpdatedDate", "SubmittedAt",
    "TotalItems", "TotalQuantity", "TotalWeightKg",
    "RequestedTransportMode", "RequestedBy", "CreatedBy", "Notes",
    "ServiceWindow_EarliestUtc", "ServiceWindow_LatestUtc",
    "RequiresDropPod", "RequiresPickupPod"
)
SELECT
    l.order_id,
    l.order_ref,
    l.source_system,
    l.priority,
    l.status,
    l.created_at,
    l.now_utc,
    l.submitted_at,
    1 + (n % 4),
    (1 + (n % 4))::double precision,
    (5 + (n * 1.7))::double precision,
    CASE WHEN n % 5 = 0 THEN 'Manual' WHEN n % 3 = 0 THEN 'Fleet' ELSE 'Amr' END,
    'demo-requestor-' || (1 + (n % 4)),
    'demo-seed',
    'Phase C demo seed — auto-generated',
    l.created_at + interval '2 hours',
    l.created_at + interval '6 hours',
    false, false
FROM lifecycle l;

-- ── Items: 1-4 items per order, deterministic IDs ───────────────────────
INSERT INTO deliveryorder."Items" (
    "Id", "DeliveryOrderId", "ItemId", "PickupLocationCode", "DropLocationCode",
    "Status", "Quantity", "Uom", "WeightKg", "ItemSeq",
    "HandlingInstructions"
)
SELECT
    md5('demo-item:' || o."Id"::text || ':' || i)::uuid,
    o."Id",
    'SKU-DEMO-' || LPAD((((ROW_NUMBER() OVER (ORDER BY o."Id", i))::int)) ::text, 4, '0'),
    'PICKUP-' || ((i % 3) + 1),
    'DROP-' || ((i % 4) + 1),
    CASE
        WHEN o."Status" = 'Completed'           THEN 'Delivered'
        WHEN o."Status" = 'PartiallyCompleted'  THEN CASE WHEN i = 1 THEN 'Delivered' ELSE 'Failed' END
        WHEN o."Status" IN ('Failed', 'Cancelled', 'Rejected') THEN 'Cancelled'
        WHEN o."Status" = 'Held'                THEN 'Pending'
        WHEN o."Status" IN ('InProgress', 'Dispatched') THEN 'Picked'
        ELSE 'Pending'
    END,
    (1 + (i % 3))::double precision,
    'EA',
    (2.5 + (i * 1.1))::double precision,
    i,
    ARRAY[]::text[]
FROM deliveryorder."DeliveryOrders" o
CROSS JOIN LATERAL generate_series(1, 1 + (abs(hashtext(o."Id"::text)) % 4)) AS i
WHERE o."OrderRef" LIKE 'DEMO-%';

-- ── Per-order status history rows reflecting each order's lifecycle ─────
INSERT INTO deliveryorder."OrderStatusHistory" (
    "Id", "EventId", "OrderId", "FromStatus", "ToStatus", "OccurredAt", "Reason"
)
SELECT
    gen_random_uuid(),
    md5('demo-osh:' || o."Id"::text || ':' || step)::uuid,
    o."Id",
    NULL,
    step,
    occurred_at,
    reason
FROM deliveryorder."DeliveryOrders" o
CROSS JOIN LATERAL (
    SELECT * FROM (
        VALUES
            ('Submitted',  o."SubmittedAt",                                                    NULL::text),
            ('Validated',  o."SubmittedAt" + interval '2 minutes',                              NULL),
            ('Confirmed',  COALESCE(o."SubmittedAt" + interval '4 minutes', o."CreatedDate"),   NULL)
    ) v(step, occurred_at, reason)
    UNION ALL
    SELECT step, occurred_at, reason FROM (
        VALUES
            ('Dispatched',         o."CreatedDate" + interval '15 minutes', NULL::text),
            ('InProgress',         o."CreatedDate" + interval '20 minutes', NULL),
            ('Completed',          o."CreatedDate" + (
                CASE
                    WHEN o."OrderRef" IN ('DEMO-001','DEMO-007','DEMO-012','DEMO-018')               THEN interval '25 minutes'
                    WHEN o."OrderRef" IN ('DEMO-002','DEMO-008','DEMO-013','DEMO-021')               THEN interval '55 minutes'
                    WHEN o."OrderRef" IN ('DEMO-003','DEMO-009','DEMO-014','DEMO-022')               THEN interval '2 hours 10 minutes'
                    WHEN o."OrderRef" IN ('DEMO-004','DEMO-011')                                     THEN interval '6 hours'
                    WHEN o."OrderRef" IN ('DEMO-005','DEMO-016','DEMO-017')                          THEN interval '3 hours 30 minutes'
                    WHEN o."OrderRef" IN ('DEMO-026')                                                THEN interval '14 hours'
                    ELSE                                                                                  interval '28 hours'
                END), NULL)
    ) c(step, occurred_at, reason)
    WHERE o."Status" = 'Completed'
    UNION ALL
    SELECT 'Dispatched', o."CreatedDate" + interval '15 minutes', NULL
    WHERE o."Status" IN ('PartiallyCompleted','Failed','InProgress','Dispatched','Held','Cancelled')
    UNION ALL
    SELECT 'InProgress', o."CreatedDate" + interval '20 minutes', NULL
    WHERE o."Status" IN ('PartiallyCompleted','Failed','InProgress','Held','Cancelled')
    UNION ALL
    SELECT 'PartiallyCompleted', o."CreatedDate" + interval '90 minutes', NULL
    WHERE o."Status" = 'PartiallyCompleted'
    UNION ALL
    SELECT 'Failed', o."CreatedDate" + interval '45 minutes',
           CASE WHEN o."OrderRef" = 'DEMO-010' THEN 'vendor execution failed: device offline'
                ELSE 'RIOT3 returned 429 Too Many Requests' END
    WHERE o."Status" = 'Failed'
    UNION ALL
    SELECT 'Cancelled', o."CreatedDate" + interval '50 minutes', 'operator cancellation requested'
    WHERE o."Status" = 'Cancelled'
    UNION ALL
    SELECT 'Held', o."CreatedDate" + interval '40 minutes', 'awaiting clarification from ops'
    WHERE o."Status" = 'Held'
) steps
WHERE o."OrderRef" LIKE 'DEMO-%';

-- ── planning.Jobs — one job per non-Draft/non-Submitted/non-Validated order ──
INSERT INTO planning."Jobs" (
    "Id", "DeliveryOrderId", "Status", "Priority", "CreatedAt",
    "Pattern", "TotalWeight", "AttemptNumber", "GroupIndex",
    "EstimatedDuration", "EstimatedDistance",
    "DerivedFromOrders", "PackageBarcodes",
    "TransportMode", "FailureReason", "FailureCategory",
    "VendorOrderKey", "TripId"
)
SELECT
    md5('demo-job:' || o."Id"::text)::uuid,
    o."Id",
    CASE o."Status"
        WHEN 'Completed'          THEN 'Completed'
        WHEN 'PartiallyCompleted' THEN 'Completed'
        WHEN 'Failed'             THEN 'Failed'
        WHEN 'Cancelled'          THEN 'Cancelled'
        WHEN 'InProgress'         THEN 'Executing'
        WHEN 'Dispatched'         THEN 'Dispatched'
        WHEN 'Held'               THEN 'Dispatched'
        WHEN 'Confirmed'          THEN 'Created'
    END,
    o."Priority",
    o."CreatedDate" + interval '5 minutes',
    'Direct',
    o."TotalWeightKg",
    1, 1,
    900.0, 1200.0,
    jsonb_build_array(o."Id"::text),
    '',
    o."RequestedTransportMode",
    CASE WHEN o."Status" = 'Failed' AND o."OrderRef" = 'DEMO-010'
              THEN 'vendor execution failed: device offline'
         WHEN o."Status" = 'Failed' THEN 'RIOT3 returned 429 Too Many Requests'
         WHEN o."Status" = 'Cancelled' THEN 'operator cancellation requested'
         ELSE NULL
    END,
    CASE WHEN o."Status" = 'Failed' AND o."OrderRef" = 'DEMO-010' THEN 'VendorExecutionFailed'
         WHEN o."Status" = 'Failed'    THEN 'VendorRateLimited'
         WHEN o."Status" = 'Cancelled' THEN 'OperatorCancelled'
         ELSE 'None'
    END,
    -- Vendor key reused from a deterministic md5 → vendor mapping.
    CASE WHEN o."Status" IN ('Confirmed') THEN NULL
         ELSE (
            CASE WHEN o."OrderRef" IN ('DEMO-003','DEMO-006','DEMO-013','DEMO-022') THEN 'VOK-CCC-' || substr(md5(o."Id"::text), 1, 6)
                 WHEN o."OrderRef" IN ('DEMO-002','DEMO-005','DEMO-009','DEMO-014','DEMO-018','DEMO-025','DEMO-028') THEN 'VOK-BBB-' || substr(md5(o."Id"::text), 1, 6)
                 ELSE 'VOK-AAA-' || substr(md5(o."Id"::text), 1, 6) END
         )
    END,
    CASE WHEN o."Status" IN ('Confirmed') THEN NULL
         ELSE md5('demo-trip:' || o."Id"::text)::uuid
    END
FROM deliveryorder."DeliveryOrders" o
WHERE o."OrderRef" LIKE 'DEMO-%'
  AND o."Status" NOT IN ('Draft', 'Submitted', 'Validated');

-- ── JobStatusHistory for the seeded jobs ────────────────────────────────
INSERT INTO planning."JobStatusHistory" (
    "Id", "EventId", "JobId", "DeliveryOrderId",
    "FromStatus", "ToStatus", "OccurredAt", "Reason"
)
SELECT
    gen_random_uuid(),
    md5('demo-jsh:' || j."Id"::text || ':' || step)::uuid,
    j."Id",
    j."DeliveryOrderId",
    NULL,
    step,
    j."CreatedAt" + offset_interval,
    NULL
FROM planning."Jobs" j
CROSS JOIN LATERAL (
    SELECT step, offset_interval FROM (
        VALUES
            ('Created',    interval '0 minutes'),
            ('Dispatched', interval '5 minutes')
    ) v(step, offset_interval)
    UNION ALL
    SELECT 'Executing', interval '10 minutes' WHERE j."Status" IN ('Executing','Completed')
    UNION ALL
    SELECT 'Completed', interval '25 minutes' WHERE j."Status" = 'Completed'
    UNION ALL
    SELECT 'Failed',    interval '15 minutes' WHERE j."Status" = 'Failed'
    UNION ALL
    SELECT 'Cancelled', interval '15 minutes' WHERE j."Status" = 'Cancelled'
) steps
WHERE EXISTS (
    SELECT 1 FROM deliveryorder."DeliveryOrders" o
    WHERE o."Id" = j."DeliveryOrderId" AND o."OrderRef" LIKE 'DEMO-%'
);

-- ── dispatch.Trips for each job that got dispatched ─────────────────────
INSERT INTO dispatch."Trips" (
    "Id", "JobId", "VehicleId", "Status", "CreatedAt", "StartedAt", "CompletedAt",
    "DeliveryOrderId", "UpperKey", "VendorOrderKey", "VendorVehicleKey",
    "FailureReason", "AttemptNumber"
)
SELECT
    j."TripId",
    j."Id",
    md5('demo-vehicle:' || (1 + (abs(hashtext(j."Id"::text)) % 8))::text)::uuid,
    CASE j."Status"
        WHEN 'Completed'  THEN 'Completed'
        WHEN 'Executing'  THEN 'InProgress'
        WHEN 'Dispatched' THEN 'Created'
        WHEN 'Failed'     THEN 'Failed'
        WHEN 'Cancelled'  THEN 'Cancelled'
    END,
    j."CreatedAt" + interval '5 minutes',
    CASE WHEN j."Status" IN ('Executing','Completed','Failed','Cancelled')
         THEN j."CreatedAt" + interval '10 minutes' END,
    CASE WHEN j."Status" = 'Completed' THEN (
        j."CreatedAt" + interval '10 minutes' + (
            CASE
                WHEN o."OrderRef" IN ('DEMO-001','DEMO-007','DEMO-012','DEMO-018')         THEN interval '15 minutes'
                WHEN o."OrderRef" IN ('DEMO-002','DEMO-008','DEMO-013','DEMO-021')         THEN interval '45 minutes'
                WHEN o."OrderRef" IN ('DEMO-003','DEMO-009','DEMO-014','DEMO-022')         THEN interval '2 hours'
                WHEN o."OrderRef" IN ('DEMO-004','DEMO-011')                               THEN interval '5 hours 50 minutes'
                WHEN o."OrderRef" IN ('DEMO-005','DEMO-016','DEMO-017')                    THEN interval '3 hours 20 minutes'
                WHEN o."OrderRef" IN ('DEMO-026')                                          THEN interval '13 hours 50 minutes'
                ELSE                                                                            interval '27 hours 50 minutes'
            END)
    ) END,
    j."DeliveryOrderId",
    replace(j."DeliveryOrderId"::text, '-', '') || '-G1',
    j."VendorOrderKey",
    'VEH-' || substr(md5(j."Id"::text), 1, 6),
    j."FailureReason",
    1
FROM planning."Jobs" j
JOIN deliveryorder."DeliveryOrders" o ON o."Id" = j."DeliveryOrderId"
WHERE o."OrderRef" LIKE 'DEMO-%' AND j."TripId" IS NOT NULL;

-- ── TripStatusHistory ───────────────────────────────────────────────────
INSERT INTO dispatch."TripStatusHistory" (
    "Id", "EventId", "TripId", "DeliveryOrderId", "JobId",
    "FromStatus", "ToStatus", "OccurredAt", "Reason"
)
SELECT
    gen_random_uuid(),
    md5('demo-tsh:' || t."Id"::text || ':' || step)::uuid,
    t."Id",
    t."DeliveryOrderId",
    t."JobId",
    NULL,
    step,
    t."CreatedAt" + offset_interval,
    reason
FROM dispatch."Trips" t
CROSS JOIN LATERAL (
    SELECT 'InProgress'::text AS step, interval '5 minutes' AS offset_interval, NULL::text AS reason
    WHERE t."Status" IN ('InProgress','Completed','Failed','Cancelled')
    UNION ALL
    SELECT 'Completed', t."CompletedAt" - t."CreatedAt", NULL
    WHERE t."Status" = 'Completed' AND t."CompletedAt" IS NOT NULL
    UNION ALL
    SELECT 'Failed', interval '15 minutes', t."FailureReason"
    WHERE t."Status" = 'Failed'
    UNION ALL
    SELECT 'Cancelled', interval '15 minutes', 'operator cancellation requested'
    WHERE t."Status" = 'Cancelled'
) steps
WHERE EXISTS (
    SELECT 1 FROM deliveryorder."DeliveryOrders" o
    WHERE o."Id" = t."DeliveryOrderId" AND o."OrderRef" LIKE 'DEMO-%'
);

SELECT
    (SELECT COUNT(*) FROM deliveryorder."DeliveryOrders" WHERE "OrderRef" LIKE 'DEMO-%') AS demo_orders,
    (SELECT COUNT(*) FROM deliveryorder."Items" i
         JOIN deliveryorder."DeliveryOrders" o ON o."Id" = i."DeliveryOrderId"
         WHERE o."OrderRef" LIKE 'DEMO-%') AS demo_items,
    (SELECT COUNT(*) FROM planning."Jobs" j
         JOIN deliveryorder."DeliveryOrders" o ON o."Id" = j."DeliveryOrderId"
         WHERE o."OrderRef" LIKE 'DEMO-%') AS demo_jobs,
    (SELECT COUNT(*) FROM dispatch."Trips" t
         JOIN deliveryorder."DeliveryOrders" o ON o."Id" = t."DeliveryOrderId"
         WHERE o."OrderRef" LIKE 'DEMO-%') AS demo_trips,
    (SELECT COUNT(*) FROM deliveryorder."OrderStatusHistory" h
         JOIN deliveryorder."DeliveryOrders" o ON o."Id" = h."OrderId"
         WHERE o."OrderRef" LIKE 'DEMO-%') AS demo_osh_rows;

COMMIT;
