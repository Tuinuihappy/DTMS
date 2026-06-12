-- ============================================================================
-- Phase P2 backfill — seed deliveryorder."OrderActivity" from the four
-- historical sources the legacy GetFullOrderAuditQueryHandler unioned at
-- query time:
--   1. deliveryorder."OrderAuditEvents"   → category "OrderLifecycle"
--   2. deliveryorder."OrderAmendments"    → category "Amendment"
--   3. dispatch."ExecutionEvents"         → category "TripExecution"
--   4. dispatch."TripRetryEvents"         → category "TripRetry"
--
-- ProjectionInbox rows are added in parallel so the projector recognises
-- the synthetic event ids as already-processed (prevents an event arriving
-- through the bus with a colliding id from re-inserting).
--
-- Idempotent: NOT EXISTS guards on every INSERT — safe to re-run.
-- ============================================================================

BEGIN;

-- ── Source 1: OrderAuditEvents ──────────────────────────────────────────
INSERT INTO deliveryorder."OrderActivity"
    ("Id", "EventId", "OrderId", "Category", "EventType",
     "Details", "ActorId", "OccurredAt", "RelatedTripId", "AttemptNumber")
SELECT
    gen_random_uuid(),
    md5('p2-backfill-audit:' || a."Id"::text)::uuid,
    a."DeliveryOrderId",
    'OrderLifecycle',
    a."EventType",
    a."Details",
    a."ActorId",
    a."OccurredAt",
    NULL, NULL
FROM deliveryorder."OrderAuditEvents" a
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."OrderActivity" x
    WHERE x."EventId" = md5('p2-backfill-audit:' || a."Id"::text)::uuid
);

-- ── Source 2: OrderAmendments ───────────────────────────────────────────
INSERT INTO deliveryorder."OrderActivity"
    ("Id", "EventId", "OrderId", "Category", "EventType",
     "Details", "ActorId", "OccurredAt", "RelatedTripId", "AttemptNumber")
SELECT
    gen_random_uuid(),
    md5('p2-backfill-amend:' || am."Id"::text)::uuid,
    am."DeliveryOrderId",
    'Amendment',
    'Amendment:' || am."Type",
    am."Reason",
    am."AmendedBy",
    am."AmendedAt",
    NULL, NULL
FROM deliveryorder."OrderAmendments" am
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."OrderActivity" x
    WHERE x."EventId" = md5('p2-backfill-amend:' || am."Id"::text)::uuid
);

-- ── Source 3: Trip ExecutionEvents (join Trips for OrderId) ─────────────
INSERT INTO deliveryorder."OrderActivity"
    ("Id", "EventId", "OrderId", "Category", "EventType",
     "Details", "ActorId", "OccurredAt", "RelatedTripId", "AttemptNumber")
SELECT
    gen_random_uuid(),
    md5('p2-backfill-exec:' || e."Id"::text)::uuid,
    t."DeliveryOrderId",
    'TripExecution',
    e."EventType",
    e."Details",
    NULL,                              -- ExecutionEvents are system-emitted
    e."OccurredAt",
    e."TripId",
    t."AttemptNumber"
FROM dispatch."ExecutionEvents" e
JOIN dispatch."Trips" t ON t."Id" = e."TripId"
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."OrderActivity" x
    WHERE x."EventId" = md5('p2-backfill-exec:' || e."Id"::text)::uuid
);

-- ── Source 4: TripRetryEvents ───────────────────────────────────────────
INSERT INTO deliveryorder."OrderActivity"
    ("Id", "EventId", "OrderId", "Category", "EventType",
     "Details", "ActorId", "OccurredAt", "RelatedTripId", "AttemptNumber")
SELECT
    gen_random_uuid(),
    md5('p2-backfill-retry:' || r."Id"::text)::uuid,
    r."DeliveryOrderId",
    'TripRetry',
    'TripRetry:' || r."RetrySource",
    COALESCE(r."RetryReason", 'retry of ' || r."OriginalStatus" || ' trip'),
    r."RetriedBy",
    r."OccurredAt",
    r."NewTripId",
    r."AttemptNumber"
FROM dispatch."TripRetryEvents" r
WHERE NOT EXISTS (
    SELECT 1 FROM deliveryorder."OrderActivity" x
    WHERE x."EventId" = md5('p2-backfill-retry:' || r."Id"::text)::uuid
);

-- ── Register synthetic event ids in projector inbox ─────────────────────
-- Prevents a redelivered or replayed bus event with a colliding id from
-- writing a duplicate row through OrderActivityProjector.
INSERT INTO deliveryorder."ProjectionInbox"
    ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT gen_random_uuid(), 'OrderActivityProjector', x."EventId", NOW() AT TIME ZONE 'UTC'
FROM deliveryorder."OrderActivity" x
WHERE x."EventId" IN (
    SELECT md5('p2-backfill-audit:' || a."Id"::text)::uuid FROM deliveryorder."OrderAuditEvents" a UNION ALL
    SELECT md5('p2-backfill-amend:' || am."Id"::text)::uuid FROM deliveryorder."OrderAmendments" am UNION ALL
    SELECT md5('p2-backfill-exec:'  || e."Id"::text)::uuid  FROM dispatch."ExecutionEvents" e UNION ALL
    SELECT md5('p2-backfill-retry:' || r."Id"::text)::uuid  FROM dispatch."TripRetryEvents" r
)
  AND NOT EXISTS (
    SELECT 1 FROM deliveryorder."ProjectionInbox" i
    WHERE i."ProjectorName" = 'OrderActivityProjector' AND i."EventId" = x."EventId"
);

-- ── Sanity summary ──────────────────────────────────────────────────────
SELECT
    (SELECT COUNT(*) FROM deliveryorder."OrderAuditEvents")     AS source_audit_events,
    (SELECT COUNT(*) FROM deliveryorder."OrderAmendments")      AS source_amendments,
    (SELECT COUNT(*) FROM dispatch."ExecutionEvents")           AS source_execution_events,
    (SELECT COUNT(*) FROM dispatch."TripRetryEvents")           AS source_trip_retries,
    (SELECT COUNT(*) FROM deliveryorder."OrderActivity")        AS activity_rows_total,
    (SELECT COUNT(*) FROM deliveryorder."OrderActivity"
        WHERE "Category" = 'OrderLifecycle')                    AS activity_order_rows,
    (SELECT COUNT(*) FROM deliveryorder."OrderActivity"
        WHERE "Category" = 'Amendment')                         AS activity_amendment_rows,
    (SELECT COUNT(*) FROM deliveryorder."OrderActivity"
        WHERE "Category" = 'TripExecution')                     AS activity_trip_rows,
    (SELECT COUNT(*) FROM deliveryorder."OrderActivity"
        WHERE "Category" = 'TripRetry')                         AS activity_retry_rows;

COMMIT;
