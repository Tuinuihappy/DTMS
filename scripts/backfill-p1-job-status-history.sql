-- ============================================================================
-- Phase P1 backfill — seed planning.JobStatusHistory with one "initial" row
-- per existing Job. Mirrors the DeliveryOrder backfill SQL shape.
--
-- Idempotent: NOT EXISTS guards prevent double-seed on re-run.
-- ============================================================================

BEGIN;

INSERT INTO planning."JobStatusHistory"
    ("Id", "EventId", "JobId", "DeliveryOrderId", "FromStatus", "ToStatus", "OccurredAt", "Reason")
SELECT
    gen_random_uuid(),
    md5('p1-job-backfill:' || j."Id"::text)::uuid,
    j."Id",
    j."DeliveryOrderId",
    NULL,
    j."Status",
    j."CreatedAt",
    'backfill-p1-b12'
FROM planning."Jobs" j
WHERE NOT EXISTS (
    SELECT 1 FROM planning."JobStatusHistory" h
    WHERE h."JobId" = j."Id"
);

INSERT INTO planning."ProjectionInbox"
    ("Id", "ProjectorName", "EventId", "ProcessedAtUtc")
SELECT
    gen_random_uuid(),
    'JobStatusHistoryProjector',
    md5('p1-job-backfill:' || j."Id"::text)::uuid,
    NOW() AT TIME ZONE 'UTC'
FROM planning."Jobs" j
WHERE NOT EXISTS (
    SELECT 1 FROM planning."ProjectionInbox" i
    WHERE i."ProjectorName" = 'JobStatusHistoryProjector'
      AND i."EventId" = md5('p1-job-backfill:' || j."Id"::text)::uuid
);

SELECT
    (SELECT COUNT(*) FROM planning."Jobs")                         AS total_jobs,
    (SELECT COUNT(*) FROM planning."JobStatusHistory")             AS history_rows_total,
    (SELECT COUNT(*) FROM planning."JobStatusHistory"
        WHERE "Reason" = 'backfill-p1-b12')                        AS history_rows_from_backfill;

COMMIT;
