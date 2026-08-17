-- ─────────────────────────────────────────────────────────────────────
-- Full wipe of order / job / trip data + projections + bi facts.
-- Preserves: reference data (facility, fleet, auth, planning templates,
-- vendoradapter config, EF migrations history).
--
-- Run with:
--   Get-Content scripts/wipe-test-data.sql | docker exec -i dtms-postgres `
--     psql -U postgres -d amr_delivery_planning -v ON_ERROR_STOP=1
-- ─────────────────────────────────────────────────────────────────────

BEGIN;

-- Single TRUNCATE so Postgres resolves FK order itself via CASCADE.
-- RESTART IDENTITY resets any IDENTITY sequences (none of these tables
-- use them at present, but safe to include).
TRUNCATE TABLE
  -- DeliveryOrder module ────────────────────────────────────────────
  deliveryorder."DeliveryOrders",
  deliveryorder."Items",
  deliveryorder."ItemPodEvents",
  deliveryorder."OrderAmendments",
  deliveryorder."OrderAuditEvents",
  deliveryorder."OrderListView",
  deliveryorder."OrderActivity",
  deliveryorder."OrderStatusHistory",
  deliveryorder."OrderFunnelHourly",
  deliveryorder."ProjectionInbox",
  deliveryorder."OutboxMessages",
  -- Planning module (skip templates/configs — reference data) ───────
  planning."Jobs",
  planning."Legs",
  planning."JobDependencies",
  planning."JobStatusHistory",
  planning."MilkRunStops",
  planning."ProjectionInbox",
  planning."OutboxMessages",
  -- Dispatch module ─────────────────────────────────────────────────
  dispatch."Trips",
  dispatch."TripItems",
  dispatch."TripStatusHistory",
  dispatch."TripExceptions",
  dispatch."TripMissionEvents",
  dispatch."TripRetryEvents",
  dispatch."ExecutionEvents",
  dispatch."ProofsOfDelivery",
  dispatch."ProjectionInbox",
  dispatch."OutboxMessages",
  -- BI fact tables ──────────────────────────────────────────────────
  bi."OrderFacts",
  bi."JobFacts",
  bi."TripFacts"
RESTART IDENTITY CASCADE;

-- Saga orchestration (T2 POC) ────────────────────────────────────────
-- orchestration."DeliveryOrderSagas" only exists when the Workflow:UseSaga
-- flag is on (OrchestrationSchemaInitializer creates it at startup). It is
-- kept OUT of the single TRUNCATE above on purpose: naming a missing
-- relation there aborts the whole statement, so with the flag off nothing
-- would be wiped at all. Truncate it only when present.
DO $$
BEGIN
  IF to_regclass('orchestration."DeliveryOrderSagas"') IS NOT NULL THEN
    EXECUTE 'TRUNCATE TABLE orchestration."DeliveryOrderSagas"';
  END IF;
END $$;

COMMIT;

\echo === Post-wipe row counts ===
SELECT 'deliveryorder.DeliveryOrders' AS tbl, COUNT(*) AS rows FROM deliveryorder."DeliveryOrders"
UNION ALL SELECT 'deliveryorder.OrderListView',  COUNT(*) FROM deliveryorder."OrderListView"
UNION ALL SELECT 'deliveryorder.OrderActivity',  COUNT(*) FROM deliveryorder."OrderActivity"
UNION ALL SELECT 'deliveryorder.ProjectionInbox', COUNT(*) FROM deliveryorder."ProjectionInbox"
UNION ALL SELECT 'deliveryorder.OutboxMessages',  COUNT(*) FROM deliveryorder."OutboxMessages"
UNION ALL SELECT 'planning.Jobs',                 COUNT(*) FROM planning."Jobs"
UNION ALL SELECT 'dispatch.Trips',                COUNT(*) FROM dispatch."Trips"
UNION ALL SELECT 'bi.OrderFacts',                 COUNT(*) FROM bi."OrderFacts"
UNION ALL SELECT 'bi.JobFacts',                   COUNT(*) FROM bi."JobFacts"
UNION ALL SELECT 'bi.TripFacts',                  COUNT(*) FROM bi."TripFacts"
ORDER BY 1;
