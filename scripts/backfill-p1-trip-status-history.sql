-- ============================================================================
-- Phase P1 backfill — seed dispatch.TripStatusHistory with one "initial"
-- row per existing Trip. Mirrors the Order/Job backfill shape.
--
-- Trip rows carry DeliveryOrderId + JobId in the source table so the seed
-- row gets both fields populated (unlike pause/resume events later, which
-- carry only TripId).
--
-- Idempotent: NOT EXISTS guards prevent double-seed.
-- ============================================================================

BEGIN;

INSERT INTO dispatch."TripStatusHistory"
    ("Id", "EventId", "TripId", "DeliveryOrderId", "JobId",
     "FromStatus", "ToStatus", "OccurredAt", "Reason")
SELECT
    gen_random_uuid(),
    md5('p1-trip-backfill:' || t."Id"::text)::uuid,
    t."Id",
    t."DeliveryOrderId",
    NULLIF(t."JobId", '00000000-0000-0000-0000-000000000000'::uuid),
    NULL,
    t."Status",
    t."CreatedAt",
    'backfill-p1-b12'
FROM dispatch."Trips" t
WHERE NOT EXISTS (
    SELECT 1 FROM dispatch."TripStatusHistory" h
    WHERE h."TripId" = t."Id"
);

INSERT INTO dispatch."ProjectionInbox"
    ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'TripStatusHistoryProjector',
    md5('p1-trip-backfill:' || t."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM dispatch."Trips" t
WHERE NOT EXISTS (
    SELECT 1 FROM dispatch."ProjectionInbox" i
    WHERE i."ProjectorName" = 'TripStatusHistoryProjector'
      AND i."EventId" = md5('p1-trip-backfill:' || t."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM dispatch."Trips")                        AS total_trips,
    (SELECT COUNT(*) FROM dispatch."TripStatusHistory")            AS history_rows_total,
    (SELECT COUNT(*) FROM dispatch."TripStatusHistory"
        WHERE "Reason" = 'backfill-p1-b12')                        AS history_rows_from_backfill;

COMMIT;
