-- ============================================================================
-- Phase P5.2 backfill — seed bi."TripFacts" by joining dispatch."Trips"
-- with dispatch.TripStatusHistory for the timeline timestamps.
--
-- dispatch."Trips" carries CreatedAt/StartedAt/CompletedAt + UpperKey
-- (= VendorUpperKey) + DeliveryOrderId/JobId. TripStatusHistory adds
-- Paused/Resumed/Failed/Cancelled timestamps the Trips table doesn't
-- model directly.
--
-- Idempotent: TRUNCATE + INSERT.
-- ============================================================================

BEGIN;

TRUNCATE bi."TripFacts";

WITH pivot AS (
    SELECT
        h."TripId" AS trip_id,
        MIN(CASE WHEN h."ToStatus" = 'Paused'      THEN h."OccurredAt" END) AS first_paused_at,
        MAX(CASE WHEN h."ToStatus" = 'InProgress'  THEN h."OccurredAt" END) AS last_resumed_at,
        MIN(CASE WHEN h."ToStatus" = 'Failed'      THEN h."OccurredAt" END) AS failed_at,
        MIN(CASE WHEN h."ToStatus" = 'Cancelled'   THEN h."OccurredAt" END) AS cancelled_at,
        SUM(CASE WHEN h."ToStatus" = 'Paused'      THEN 1 ELSE 0 END)        AS pause_count,
        MAX(h."OccurredAt") AS last_event_at
    FROM dispatch."TripStatusHistory" h
    GROUP BY h."TripId"
)
INSERT INTO bi."TripFacts" (
    "TripId", "DeliveryOrderId", "JobId", "VehicleId",
    "VendorUpperKey", "VendorVehicleKey",
    "FinalStatus", "FailureReason", "PauseCount",
    "CreatedAt", "StartedAt", "FirstPausedAt", "LastResumedAt",
    "CompletedAt", "FailedAt", "CancelledAt",
    "UpdatedAt"
)
SELECT
    t."Id",
    NULLIF(t."DeliveryOrderId", '00000000-0000-0000-0000-000000000000'::uuid),
    NULLIF(t."JobId", '00000000-0000-0000-0000-000000000000'::uuid),
    t."VehicleId",
    NULLIF(t."UpperKey", ''),
    NULLIF(t."VendorVehicleKey", ''),
    t."Status",
    t."FailureReason",
    COALESCE(p.pause_count, 0)::int,
    t."CreatedAt",
    t."StartedAt",
    p.first_paused_at,
    -- Only count resume timestamps that come after the first pause —
    -- otherwise normal InProgress entries on a never-paused trip would
    -- masquerade as a resume.
    CASE WHEN p.first_paused_at IS NOT NULL THEN p.last_resumed_at ELSE NULL END,
    t."CompletedAt",
    p.failed_at,
    p.cancelled_at,
    COALESCE(p.last_event_at, t."CompletedAt", t."StartedAt", t."CreatedAt")
FROM dispatch."Trips" t
LEFT JOIN pivot p ON p.trip_id = t."Id";

INSERT INTO dispatch."ProjectionInbox" ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'TripFactsProjector',
    md5('p5-backfill:' || t."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM dispatch."Trips" t
WHERE NOT EXISTS (
    SELECT 1 FROM dispatch."ProjectionInbox" i
    WHERE i."ProjectorName" = 'TripFactsProjector'
      AND i."EventId" = md5('p5-backfill:' || t."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM dispatch."Trips") AS source_trips,
    (SELECT COUNT(*) FROM bi."TripFacts")   AS bi_rows,
    (SELECT COUNT(*) FROM bi."TripFacts" WHERE "StartedAt" IS NOT NULL)        AS started_trips,
    (SELECT COUNT(*) FROM bi."TripFacts" WHERE "CompletedAt" IS NOT NULL)      AS completed_trips,
    (SELECT COUNT(*) FROM bi."TripFacts" WHERE "SlaCompleteBreached" = true)   AS sla_complete_breach;

COMMIT;
