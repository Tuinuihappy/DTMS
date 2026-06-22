-- Count rows in every table touched by the k6 perf test pipeline.
-- The k6 `scenario-b-write.js` posts orders with OrderRef like
-- `LOAD-<uuid>` and RequestedBy='k6-load' to /api/v1/delivery-orders/upstream.
-- Those rows fan out across all DeliveryOrder write tables + projections,
-- the bi.OrderFacts fact table, and (if Planning consumed them) planning.Jobs
-- and dispatch.Trips. This script just reports counts; it does not delete.

\echo === DeliveryOrder module ===
SELECT 'deliveryorder.DeliveryOrders'      AS tbl, COUNT(*) AS rows FROM deliveryorder."DeliveryOrders"
UNION ALL SELECT 'deliveryorder.Items',                COUNT(*) FROM deliveryorder."Items"
UNION ALL SELECT 'deliveryorder.ItemPodEvents',        COUNT(*) FROM deliveryorder."ItemPodEvents"
UNION ALL SELECT 'deliveryorder.OrderAmendments',      COUNT(*) FROM deliveryorder."OrderAmendments"
UNION ALL SELECT 'deliveryorder.OrderAuditEvents',     COUNT(*) FROM deliveryorder."OrderAuditEvents"
UNION ALL SELECT 'deliveryorder.OrderListView',        COUNT(*) FROM deliveryorder."OrderListView"
UNION ALL SELECT 'deliveryorder.OrderActivity',        COUNT(*) FROM deliveryorder."OrderActivity"
UNION ALL SELECT 'deliveryorder.OrderStatusHistory',   COUNT(*) FROM deliveryorder."OrderStatusHistory"
UNION ALL SELECT 'deliveryorder.OrderFunnelHourly',    COUNT(*) FROM deliveryorder."OrderFunnelHourly"
UNION ALL SELECT 'deliveryorder.ProjectionInbox',      COUNT(*) FROM deliveryorder."ProjectionInbox"
UNION ALL SELECT 'deliveryorder.OutboxMessages',       COUNT(*) FROM deliveryorder."OutboxMessages"
ORDER BY 1;

\echo === BI fact tables ===
SELECT 'bi.OrderFacts' AS tbl, COUNT(*) AS rows FROM bi."OrderFacts"
UNION ALL SELECT 'bi.JobFacts',  COUNT(*) FROM bi."JobFacts"
UNION ALL SELECT 'bi.TripFacts', COUNT(*) FROM bi."TripFacts"
ORDER BY 1;

\echo === Downstream modules (if Planning/Dispatch ran) ===
\dt planning.*
\dt dispatch.*
\dt orchestration.*

\echo === LOAD-* vs non-LOAD orders breakdown ===
SELECT
  CASE WHEN "OrderRef" LIKE 'LOAD-%' THEN 'LOAD-* (k6 test data)' ELSE 'other (manual / real)' END AS source,
  COUNT(*) AS rows
FROM deliveryorder."DeliveryOrders"
GROUP BY 1
ORDER BY 1;
