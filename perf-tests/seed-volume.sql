-- Bulk-seed orders directly via SQL to test list/filter performance
-- against a large dataset. Only inserts DeliveryOrders rows (no children)
-- since we are measuring read path against status/listing endpoints.

DO $$
DECLARE
  target_count integer := 500000;
  batch_size integer := 10000;
  inserted bigint;
BEGIN
  SELECT COUNT(*) INTO inserted FROM deliveryorder."DeliveryOrders";
  RAISE NOTICE 'Volume seed start. Current rows = %', inserted;

  WHILE inserted < target_count LOOP
    INSERT INTO deliveryorder."DeliveryOrders" (
      "Id", "Priority", "Status", "OrderRef",
      "CreatedDate", "UpdatedDate",
      "TotalQuantity", "TotalWeightKg", "TotalItems",
      "SourceSystem", "CreatedBy", "SubmittedAt",
      "ServiceWindow_EarliestUtc", "ServiceWindow_LatestUtc",
      "Notes", "RequestedBy", "RequestedTransportMode",
      "RequiresDropPod", "RequiresPickupPod"
    )
    SELECT
      gen_random_uuid(),
      (ARRAY['LOW','NORMAL','HIGH','URGENT'])[1 + (n % 4)],
      (ARRAY['DRAFT','VALIDATED','PLANNED','IN_PROGRESS','COMPLETED','FAILED','CANCELLED'])[1 + (n % 7)],
      'VOL-' || lpad((inserted + n)::text, 9, '0'),
      now() - (random() * interval '30 days'),
      now() - (random() * interval '30 days'),
      1.0, 250.0, 1,
      'SAP',
      'vol-seed',
      now() - (random() * interval '30 days'),
      now() + interval '1 hour',
      now() + interval '1 day',
      'volume seed', 'vol-seed', 'AMR',
      false, false
    FROM generate_series(1, batch_size) AS n;

    inserted := inserted + batch_size;
    IF (inserted % 50000) = 0 THEN
      RAISE NOTICE '  inserted % rows so far', inserted;
    END IF;
  END LOOP;

  RAISE NOTICE 'Volume seed complete. Total = %', inserted;
END $$;
