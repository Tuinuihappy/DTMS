-- ============================================================================
-- Phase P5.2 backfill — seed bi."JobFacts" by joining planning."Jobs"
-- with planning.JobStatusHistory for the lifecycle timestamps.
--
-- Idempotent: TRUNCATE + INSERT.
-- ============================================================================

BEGIN;

TRUNCATE bi."JobFacts";

WITH pivot AS (
    SELECT
        h."JobId" AS job_id,
        MIN(CASE WHEN h."ToStatus" = 'Assigned'    THEN h."OccurredAt" END) AS assigned_at,
        MIN(CASE WHEN h."ToStatus" = 'Committed'   THEN h."OccurredAt" END) AS committed_at,
        MIN(CASE WHEN h."ToStatus" = 'Dispatched'  THEN h."OccurredAt" END) AS dispatched_at,
        MIN(CASE WHEN h."ToStatus" = 'Executing'   THEN h."OccurredAt" END) AS executing_at,
        MIN(CASE WHEN h."ToStatus" = 'Completed'   THEN h."OccurredAt" END) AS completed_at,
        MIN(CASE WHEN h."ToStatus" = 'Failed'      THEN h."OccurredAt" END) AS failed_at,
        MIN(CASE WHEN h."ToStatus" = 'Cancelled'   THEN h."OccurredAt" END) AS cancelled_at,
        MAX(h."OccurredAt") AS last_event_at
    FROM planning."JobStatusHistory" h
    GROUP BY h."JobId"
),
failure_reason AS (
    SELECT DISTINCT ON (h."JobId")
        h."JobId" AS job_id,
        h."Reason"  AS reason
    FROM planning."JobStatusHistory" h
    WHERE h."ToStatus" IN ('Failed', 'Cancelled')
    ORDER BY h."JobId", h."OccurredAt" DESC
)
INSERT INTO bi."JobFacts" (
    "JobId", "DeliveryOrderId", "AssignedVehicleId", "LatestTripId",
    "VendorOrderKey", "FinalStatus", "FailureReason", "FailureCategory",
    "AttemptNumber",
    "CreatedAt", "AssignedAt", "CommittedAt", "DispatchedAt", "ExecutingAt",
    "CompletedAt", "FailedAt", "CancelledAt",
    "UpdatedAt"
)
SELECT
    j."Id",
    j."DeliveryOrderId",
    j."AssignedVehicleId",
    j."TripId",
    NULL,                       -- VendorOrderKey lives on trips, not jobs
    j."Status",
    COALESCE(j."FailureReason", fr.reason),
    -- Phase #9 — source of truth is the write side. Defaults to 'None'
    -- via the column DEFAULT for pre-b13 rows.
    COALESCE(NULLIF(j."FailureCategory", ''), 'None'),
    j."AttemptNumber",
    j."CreatedAt",
    p.assigned_at,
    p.committed_at,
    p.dispatched_at,
    p.executing_at,
    p.completed_at,
    p.failed_at,
    p.cancelled_at,
    COALESCE(p.last_event_at, j."CreatedAt")
FROM planning."Jobs" j
LEFT JOIN pivot p           ON p.job_id = j."Id"
LEFT JOIN failure_reason fr ON fr.job_id = j."Id";

INSERT INTO planning."ProjectionInbox" ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'JobFactsProjector',
    md5('p5-backfill:' || j."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM planning."Jobs" j
WHERE NOT EXISTS (
    SELECT 1 FROM planning."ProjectionInbox" i
    WHERE i."ProjectorName" = 'JobFactsProjector'
      AND i."EventId" = md5('p5-backfill:' || j."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM planning."Jobs") AS source_jobs,
    (SELECT COUNT(*) FROM bi."JobFacts")   AS bi_rows,
    (SELECT COUNT(*) FROM bi."JobFacts" WHERE "DispatchedAt" IS NOT NULL)        AS dispatched_jobs,
    (SELECT COUNT(*) FROM bi."JobFacts" WHERE "CompletedAt" IS NOT NULL)         AS completed_jobs,
    (SELECT COUNT(*) FROM bi."JobFacts" WHERE "SlaDispatchBreached" = true)      AS sla_dispatch_breach,
    (SELECT COUNT(*) FROM bi."JobFacts" WHERE "AttemptNumber" > 1)               AS retried_jobs;

COMMIT;
