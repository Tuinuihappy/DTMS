using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Events;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Domain;

namespace DTMS.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>, IAuditable
{
    public string OrderRef { get; private set; } = string.Empty;

    // Origin identity — soft-FK to iam.SystemClients. Slug is lowercase
    // (validated by SystemClient.Key). DisplayName is a snapshot taken
    // at create time so admin renames don't retro-update historical rows
    // (audit immutability).
    public string SourceSystemKey { get; private set; } = string.Empty;
    public string SourceSystemDisplayName { get; private set; } = string.Empty;

    public Priority Priority { get; private set; }
    public OrderStatus Status { get; private set; }
    public ServiceWindow? ServiceWindow { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? RequestedBy { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? UpdatedDate { get; private set; }
    public double TotalWeightKg { get; private set; }
    public double TotalQuantity { get; private set; }
    public int TotalItems { get; private set; }
    public TransportMode? RequestedTransportMode { get; private set; }

    /// <summary>
    /// When true, items DO NOT auto-Deliver on TASK_FINISHED — they sit
    /// at DroppedOff until an operator confirms via /pod-scan. Defaults
    /// to false (opt-in) so vendor flow auto-completes unless the order
    /// or its template opts into POD. Per-order override; null falls
    /// back to OrderTemplate.RequiresDropPod.
    /// </summary>
    public bool? RequiresDropPod { get; private set; }

    /// <summary>
    /// When true, operator must scan each item at the pickup station for
    /// chain-of-custody audit. Pickup POD is audit-only — it never blocks
    /// the vendor flow; the only consequence of a missed scan is an
    /// "audit gap" warning in the UI. Defaults to false (opt-in).
    /// Per-order override; null falls back to OrderTemplate.RequiresPickupPod.
    /// </summary>
    public bool? RequiresPickupPod { get; private set; }

    /// <summary>
    /// When true, an external system (OMS/WMS/ERP) executes the physical
    /// transport itself and reports lifecycle to DTMS via the federated
    /// <c>/api/v1/source/trips/*</c> endpoints. Instead of the operator pool,
    /// the trip is auto-acked + auto-picked-up at creation (attributed to
    /// <see cref="RequestedBy"/>), and the external system sends drop +
    /// complete. Only supported for <see cref="TransportMode.Manual"/> (it
    /// replaces the pool path, not AMR's vendor-driven RIOT3 lifecycle), only
    /// settable on upstream orders, and requires <see cref="RequestedBy"/>.
    /// </summary>
    public bool SelfManaged { get; private set; }

    private readonly List<Item> _items = new();
    public IReadOnlyCollection<Item> Items => _items.AsReadOnly();

    private DeliveryOrder() { }

    void IAuditable.SetCreatedAt(DateTime createdAt) => CreatedDate = createdAt;
    void IAuditable.SetUpdatedAt(DateTime updatedAt) => UpdatedDate = updatedAt;

    /// <summary>
    /// Create a Draft order. Origin key + display come from
    /// <c>IOrderOriginResolver</c> in production — never accept them from
    /// the wire. The defaults let unit tests construct orders without
    /// wiring a resolver; production handlers pass explicit values.
    /// </summary>
    public static DeliveryOrder Create(string orderRef, Priority priority,
        ServiceWindow? serviceWindow,
        string sourceSystemKey = WellKnownSourceSystems.Internal,
        string sourceSystemDisplayName = WellKnownSourceSystems.InternalDisplayName,
        string? createdBy = null, string? requestedBy = null, string? notes = null,
        TransportMode? requestedTransportMode = Enums.TransportMode.Amr)
    {
        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            ServiceWindow = serviceWindow,
            Status = OrderStatus.Draft,
            SourceSystemKey = sourceSystemKey,
            SourceSystemDisplayName = sourceSystemDisplayName,
            CreatedBy = createdBy,
            RequestedBy = requestedBy,
            Notes = notes,
            RequestedTransportMode = requestedTransportMode,
            RequiresDropPod = false,
            RequiresPickupPod = false
        };

        order.AddDomainEvent(new DeliveryOrderDraftedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    /// <summary>
    /// Create an already-Submitted order from an upstream system. Rejects
    /// the 'internal' key because that slug identifies the UI path — which
    /// must go through Draft + Submit separately.
    /// </summary>
    public static DeliveryOrder CreateFromUpstream(string orderRef, Priority priority,
        ServiceWindow? serviceWindow,
        string sourceSystemKey, string sourceSystemDisplayName,
        string? createdBy = null, string? requestedBy = null, string? notes = null,
        TransportMode? requestedTransportMode = Enums.TransportMode.Amr,
        bool selfManaged = false)
    {
        if (string.Equals(sourceSystemKey, WellKnownSourceSystems.Internal, StringComparison.Ordinal))
            throw new InvalidOperationException("Upstream orders cannot use the 'internal' source key.");

        // Self-managed is only supported for Manual transport — the auto
        // ack + pickup path replaces the operator-pool execution, not AMR's
        // RIOT3 lifecycle (which is vendor-driven).
        if (selfManaged && requestedTransportMode != Enums.TransportMode.Manual)
            throw new InvalidOperationException(
                "A self-managed order is only supported for Manual transport mode.");

        // Self-managed orders auto-acknowledge + auto-pickup on trip creation,
        // attributing the action to RequestedBy — so it must be present.
        if (selfManaged && string.IsNullOrWhiteSpace(requestedBy))
            throw new InvalidOperationException(
                "A self-managed order requires RequestedBy — it is the actor recorded on the auto acknowledge + pickup.");

        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            ServiceWindow = serviceWindow,
            Status = OrderStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
            SourceSystemKey = sourceSystemKey,
            SourceSystemDisplayName = sourceSystemDisplayName,
            CreatedBy = createdBy,
            RequestedBy = requestedBy,
            Notes = notes,
            RequestedTransportMode = requestedTransportMode,
            RequiresDropPod = false,
            RequiresPickupPod = false,
            SelfManaged = selfManaged
        };

        order.AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public void UpdateDraft(string orderRef, Priority priority, ServiceWindow? serviceWindow,
        string? requestedBy = null, string? notes = null,
        TransportMode? requestedTransportMode = Enums.TransportMode.Amr)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException($"Only Draft orders can be edited. Current status: {Status}.");

        OrderRef = orderRef;
        Priority = priority;
        ServiceWindow = serviceWindow;
        RequestedBy = requestedBy;
        Notes = notes;
        RequestedTransportMode = requestedTransportMode;

        _items.Clear();
        TotalWeightKg = 0;
        TotalQuantity = 0;
        TotalItems = 0;

        AddDomainEvent(new DeliveryOrderDraftUpdatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Submit()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only Draft orders can be submitted.");

        Status = OrderStatus.Submitted;
        // SLA clock starts at first submit. Subsequent transitions (e.g., release back to Confirmed
        // after a Hold) do not re-route through Submit(), so this assignment runs exactly once.
        SubmittedAt ??= DateTime.UtcNow;
        AddDomainEvent(new DeliveryOrderSubmittedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    // Phase P4.5: emits a snapshot of the freshly-created order so the
    // OrderListView projection can materialize a row immediately, instead
    // of waiting for Confirm. Command handlers MUST call this AFTER the
    // AddItem loop — items are read from the current aggregate state.
    // PickupStationId / DropStationId are nullable here because pre-Validated
    // orders haven't been resolved against the Facility map yet.
    public void RaiseCreatedEvent()
    {
        // Phase 3a — drop the Guid.Empty fallbacks now that the DTO
        // accepts nullable station + warehouse Ids. Pre-Validated orders
        // (which is when this event commonly fires) carry null for both
        // pairs; OrderListView consumers tolerate that already.
        var itemDtos = _items
            .Select(p => new ItemEventDto(
                p.ItemId, p.WeightKg ?? 0,
                p.PickupStationId, p.DropStationId,
                p.Hazmat is { } hz ? new ItemHazmatDto(hz.ClassCode, hz.PackingGroup?.ToString()) : null,
                p.Temperature is { } tr ? new ItemTemperatureDto(tr.MinC, tr.MaxC) : null,
                p.HandlingInstructions.Count > 0
                    ? p.HandlingInstructions.Select(h => h.ToString()).ToList()
                    : null,
                p.PickupWmsLocationId, p.DropWmsLocationId))
            .ToList();

        AddDomainEvent(new DeliveryOrderCreatedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id,
            OrderRef, SourceSystemKey, Status.ToString(), Priority.ToString(),
            RequestedTransportMode?.ToString(),
            RequestedBy, CreatedBy, Notes,
            ServiceWindow?.EarliestUtc, ServiceWindow?.LatestUtc, SubmittedAt,
            RequiresDropPod, RequiresPickupPod,
            TotalItems, TotalQuantity, TotalWeightKg,
            itemDtos));
    }

    public void AddItem(
        string pickupLocationCode, string dropLocationCode,
        int itemSeq, string itemId, string? description,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, Quantity quantity,
        HazmatInfo? hazmat = null,
        TemperatureRange? temperature = null,
        IReadOnlyList<HandlingInstruction>? handlingInstructions = null)
    {
        if (_items.Any(p => p.ItemSeq == itemSeq))
            throw new InvalidOperationException($"An item with seq '{itemSeq}' already exists in this order.");

        _items.Add(new Item(Id, pickupLocationCode, dropLocationCode, itemSeq, itemId, description, loadUnitProfileCode, dimensions, weightKg, quantity, hazmat, temperature, handlingInstructions));
        TotalWeightKg += weightKg ?? 0;
        TotalQuantity += quantity.Value;
        TotalItems++;
    }

    public void UpdateAllItemStatuses(ItemStatus status)
    {
        foreach (var item in _items)
            item.UpdateStatus(status);
    }

    public void MarkItemsDelivered(IEnumerable<string> itemIds)
    {
        var idSet = new HashSet<string>(itemIds, StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
            if (idSet.Contains(item.ItemId))
                item.UpdateStatus(ItemStatus.Delivered);
    }

    // ── Trip-aware item lifecycle (Option D: item-level state derivation) ──

    /// <summary>
    /// Bind the items of a station pair to a Trip. Called by the dispatcher
    /// right after a Trip is created so subsequent webhook callbacks can
    /// update only THIS trip's items, not the whole order. Idempotent —
    /// re-binding the same trip is a no-op; binding to a higher attempt
    /// resets any Failed/Returned items back to Pending so the new trip
    /// can drive them terminal.
    /// </summary>
    public int AssignItemsToTrip(
        Guid tripId, int attemptNumber,
        Guid pickupStationId, Guid dropStationId,
        Guid? pickupWmsLocationId = null, Guid? dropWmsLocationId = null)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));

        var bound = 0;
        foreach (var item in _items)
        {
            // Match by station pair (AMR) OR WMS location pair (Manual/Fleet).
            // Empty-Guid station sentinel from the Manual consumer doesn't
            // match items' null station Ids; the WMS branch picks them up.
            var matchesStation =
                pickupStationId != Guid.Empty && dropStationId != Guid.Empty
                && item.PickupStationId == pickupStationId
                && item.DropStationId == dropStationId;

            var matchesWms =
                pickupWmsLocationId.HasValue && dropWmsLocationId.HasValue
                && item.PickupWmsLocationId == pickupWmsLocationId
                && item.DropWmsLocationId == dropWmsLocationId;

            if (!matchesStation && !matchesWms) continue;

            // Skip terminal items the operator already finalised (Cancelled
            // by admin etc.) — they don't ride retries.
            if (item.Status is ItemStatus.Cancelled or ItemStatus.Delivered)
                continue;

            item.AssignToTrip(tripId, attemptNumber);
            bound++;
        }

        if (bound > 0)
            AddDomainEvent(new TripItemsAssignedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, tripId, attemptNumber, bound));
        return bound;
    }

    /// <summary>
    /// Mark every item bound to a Trip as Delivered. Called when the Trip
    /// completes — the trip-aware filter keeps multi-group orders from
    /// finalising prematurely.
    /// </summary>
    public int MarkTripItemsDelivered(Guid tripId)
    {
        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId != tripId) continue;
            if (item.Status == ItemStatus.Delivered) continue;
            item.UpdateStatus(ItemStatus.Delivered);
            changed++;
        }
        if (changed > 0)
            AddDomainEvent(new TripItemsDeliveredDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, tripId, changed));
        return changed;
    }

    /// <summary>Sets the per-order POD policy override. Null = fall back
    /// to OrderTemplate.RequiresDropPod at TASK_FINISHED time.</summary>
    public void SetRequiresDropPod(bool? requiresDropPod) => RequiresDropPod = requiresDropPod;

    /// <summary>Sets the per-order pickup POD policy override. Null = fall
    /// back to OrderTemplate.RequiresPickupPod.</summary>
    public void SetRequiresPickupPod(bool? requiresPickupPod) => RequiresPickupPod = requiresPickupPod;

    /// <summary>
    /// Mark every Picked item bound to a Trip as DroppedOff (vendor
    /// reported the robot finished its drop action at the drop station).
    /// Idempotent. Items still Pending (race: drop event arrives before
    /// pickup processed) are skipped — they'll skip DroppedOff entirely
    /// and land at Delivered on TASK_FINISHED or POD scan.
    /// </summary>
    public int MarkTripItemsDroppedOff(Guid tripId)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected) return 0;

        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId != tripId) continue;
            if (item.Status is not ItemStatus.Picked) continue;
            item.MarkDroppedOff();
            changed++;
        }
        if (changed > 0)
            AddDomainEvent(new TripItemsDroppedOffDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, tripId, changed));
        return changed;
    }

    /// <summary>
    /// Record a POD scan for a single item at the given checkpoint.
    /// Pickup: audit-only, never changes Status. Drop: same audit row +
    /// transitions Picked/DroppedOff → Delivered. Idempotent — a second
    /// scan against the same (itemId, scanType) pair returns 0.
    /// Recompute is the caller's responsibility so a batch scan can do
    /// one pass at the end.
    /// </summary>
    public int RecordItemPod(Guid itemId, PodScanType scanType, string scannedBy, string method, string? reference)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return 0;

        var recorded = scanType switch
        {
            PodScanType.Pickup => item.RecordPickupPod(scannedBy, method, reference),
            PodScanType.Drop   => item.RecordDropPod(scannedBy, method, reference),
            _ => false
        };
        if (!recorded) return 0;

        AddDomainEvent(new ItemPodRecordedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, itemId, scanType, scannedBy, method));
        return 1;
    }

    /// <summary>
    /// TASK_FINISHED arrived. Behaviour splits on the effective POD
    /// policy: if POD is required, leave DroppedOff items alone — they
    /// need /pod-scan to land at Delivered. Otherwise the existing
    /// MarkTripItemsDelivered runs (Pending/Picked/DroppedOff all jump
    /// to Delivered).
    /// </summary>
    public int MarkTripItemsDeliveredOrLeaveForPod(Guid tripId, bool templateRequiresDropPod)
    {
        var effective = RequiresDropPod ?? templateRequiresDropPod;
        if (effective) return 0;   // POD required — wait for scans
        return MarkTripItemsDelivered(tripId);
    }

    /// <summary>
    /// Mark every Pending item bound to a Trip as Picked (vendor reports
    /// the robot finished its pickup action). Idempotent. Items already
    /// Picked, Delivered, or in a terminal failure state are untouched.
    /// </summary>
    public int MarkTripItemsPicked(Guid tripId)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected) return 0;

        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId != tripId) continue;
            if (item.Status is not ItemStatus.Pending) continue;
            item.MarkPicked();
            changed++;
        }
        if (changed > 0)
            AddDomainEvent(new TripItemsPickedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, tripId, changed));
        return changed;
    }

    /// <summary>
    /// Mark every item bound to a Trip as Failed. Picked items stay Picked
    /// (operator may resolve them manually); only in-flight items
    /// transition. Already-Delivered items are untouched.
    /// </summary>
    public int MarkTripItemsFailed(Guid tripId, string reason)
    {
        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId != tripId) continue;
            if (item.Status is ItemStatus.Pending)
            {
                item.UpdateStatus(ItemStatus.Failed);
                changed++;
            }
        }
        if (changed > 0)
            AddDomainEvent(new TripItemsFailedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, tripId, changed, reason));
        return changed;
    }

    /// <summary>
    /// Release items from a Trip binding without marking them terminal.
    /// Used when a Trip is Cancelled and the operator can still retry —
    /// items go back to "awaiting dispatch" so the next /retry rebinds
    /// them cleanly.
    /// </summary>
    public int UnassignItemsFromTrip(Guid tripId)
    {
        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId != tripId) continue;
            item.UnassignFromTrip();
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Mark every unbound, non-terminal item Cancelled. Used by the
    /// trip-cancelled consumer when the order's own status is already
    /// Cancelled / Rejected — those orders won't dispatch again, so
    /// unbound items left at Pending would never reach a terminal state.
    /// Delivered items (and anything else already terminal) are skipped.
    /// </summary>
    public int CancelUnboundItems()
    {
        var changed = 0;
        foreach (var item in _items)
        {
            if (item.TripId is not null) continue;
            if (item.IsTerminal) continue;
            item.MarkCancelled();
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Derive the order's terminal status from item states. No-op when
    /// the order is in an admin-overriden state (Cancelled / Rejected)
    /// or while any item is still in-flight (Pending / Picked).
    /// </summary>
    public void RecomputeStatusFromItems()
    {
        // Admin overrides — never auto-transition out of these.
        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected) return;
        // Already terminal — RecomputeStatusFromItems should be idempotent.
        if (Status is OrderStatus.Completed or OrderStatus.Failed
                   or OrderStatus.PartiallyCompleted) return;
        // Held — operator paused; don't auto-terminal while held. They must
        // release first; items may still be transitioning underneath.
        if (Status is OrderStatus.Held) return;

        var total = _items.Count;
        if (total == 0) return;

        var delivered = 0;
        var inFlight  = 0;
        foreach (var item in _items)
        {
            switch (item.Status)
            {
                case ItemStatus.Delivered:                          delivered++; break;
                // DroppedOff joins the in-flight set: the order isn't
                // terminal until POD confirms it Delivered (or operator
                // marks it Failed/Cancelled).
                case ItemStatus.Pending or ItemStatus.Picked or ItemStatus.DroppedOff:
                                                                    inFlight++;  break;
                // Failed / Returned / Cancelled → terminal, not delivered
            }
        }

        if (inFlight > 0) return;   // still waiting for the rest

        if (delivered == total)
        {
            Status = OrderStatus.Completed;
            AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, SourceSystemKey));
        }
        else if (delivered == 0)
        {
            Status = OrderStatus.Failed;
            AddDomainEvent(new DeliveryOrderFailedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id,
                $"All {total} items failed delivery."));
        }
        else
        {
            Status = OrderStatus.PartiallyCompleted;
            AddDomainEvent(new DeliveryOrderPartiallyCompletedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id,
                delivered, total - delivered, total));
        }
    }

    // AMR validation path — thin wrapper over the mode-aware overload below.
    public void MarkAsValidated(IReadOnlyDictionary<string, Guid> stationMap)
        => MarkAsValidated(stationMap, wmsLocationMap: null);

    /// <summary>
    /// Mode-aware validation. The order's <see cref="RequestedTransportMode"/>
    /// determines which map applies:
    ///   - AMR → stationMap only.
    ///   - Manual/Fleet → wmsLocationMap only.
    /// At least one map must be non-null.
    /// </summary>
    public void MarkAsValidated(
        IReadOnlyDictionary<string, Guid>? stationMap,
        IReadOnlyDictionary<string, Guid>? wmsLocationMap)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");
        if (stationMap is null && wmsLocationMap is null)
            throw new ArgumentException(
                "At least one of stationMap / wmsLocationMap must be provided",
                nameof(stationMap));

        foreach (var item in _items)
        {
            if (stationMap is not null)
            {
                if (!stationMap.TryGetValue(item.PickupLocationCode, out var pickupId))
                    throw new InvalidOperationException(
                        $"Missing station mapping for pickup {item.PickupLocationCode}.");
                if (!stationMap.TryGetValue(item.DropLocationCode, out var dropId))
                    throw new InvalidOperationException(
                        $"Missing station mapping for drop {item.DropLocationCode}.");
                item.SetStationIds(pickupId, dropId);
            }

            if (wmsLocationMap is not null)
            {
                if (!wmsLocationMap.TryGetValue(item.PickupLocationCode, out var pickupLoc))
                    throw new InvalidOperationException(
                        $"Missing WMS location mapping for pickup {item.PickupLocationCode}.");
                if (!wmsLocationMap.TryGetValue(item.DropLocationCode, out var dropLoc))
                    throw new InvalidOperationException(
                        $"Missing WMS location mapping for drop {item.DropLocationCode}.");
                item.SetWmsLocationIds(pickupLoc, dropLoc);
            }
        }

        Status = OrderStatus.Validated;
        AddDomainEvent(new DeliveryOrderValidatedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Confirm(double weightFallbackKg)
    {
        if (Status != OrderStatus.Validated)
            throw new InvalidOperationException($"Only Validated orders can be confirmed. Current status: {Status}.");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(BuildConfirmedEvent(weightFallbackKg));
    }

    public void Reject(string reason)
    {
        if (Status is not (OrderStatus.Submitted or OrderStatus.Validated or OrderStatus.Confirmed))
            throw new InvalidOperationException($"Cannot reject an order in {Status} status.");

        Status = OrderStatus.Rejected;
        AddDomainEvent(new DeliveryOrderRejectedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    // ── In-flight state machine (Option A: 4-state envelope progression) ─
    //
    // Planning → Planned → Dispatched → InProgress are forward-only states
    // entered as the Planning consumer makes progress. Transitions are
    // idempotent because the RabbitMQ consumer may redeliver, and the
    // TASK_PROCESSING webhook can race ahead of the dispatch consumer.
    // We use a rank to express "you can advance but never go back".
    private static int FlowRank(OrderStatus s) => s switch
    {
        OrderStatus.Confirmed   => 1,
        OrderStatus.Planning    => 2,
        OrderStatus.Planned     => 3,
        OrderStatus.Dispatched  => 4,
        OrderStatus.InProgress  => 5,
        _                       => -1   // terminal / admin / pre-Confirmed
    };

    public void MarkPlanning()
    {
        if (FlowRank(Status) >= FlowRank(OrderStatus.Planning)) return;   // idempotent forward-only
        if (Status != OrderStatus.Confirmed)
            throw new InvalidOperationException($"Cannot enter Planning from {Status}.");

        Status = OrderStatus.Planning;
        AddDomainEvent(new DeliveryOrderPlanningStartedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    private DeliveryOrderConfirmedDomainEvent BuildConfirmedEvent(double weightFallbackKg)
    {
        // Phase 3a — pass station + warehouse Ids through as nullable.
        // AMR orders populate PickupStationId/DropStationId; Manual/Fleet
        // populate the warehouse pair. Consumers pick by RequestedTransportMode.
        // BuildStation handler emits both as null for pre-Validated orders
        // (defensive — Confirm requires Validated so this shouldn't fire).
        var itemDtos = _items
            .Select(p => new ItemEventDto(
                p.ItemId, p.WeightKg ?? weightFallbackKg,
                p.PickupStationId, p.DropStationId,
                p.Hazmat is { } hz ? new ItemHazmatDto(hz.ClassCode, hz.PackingGroup?.ToString()) : null,
                p.Temperature is { } tr ? new ItemTemperatureDto(tr.MinC, tr.MaxC) : null,
                p.HandlingInstructions.Count > 0
                    ? p.HandlingInstructions.Select(h => h.ToString()).ToList()
                    : null,
                p.PickupWmsLocationId, p.DropWmsLocationId))
            .ToList();

        return new DeliveryOrderConfirmedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, Priority.ToString(),
            ServiceWindow?.EarliestUtc, ServiceWindow?.LatestUtc,
            SubmittedAt, itemDtos, RequestedTransportMode?.ToString(),
            SelfManaged, RequestedBy);
    }

    public void MarkPlanned()
    {
        if (FlowRank(Status) >= FlowRank(OrderStatus.Planned)) return;
        if (Status is not (OrderStatus.Planning or OrderStatus.Confirmed))
            throw new InvalidOperationException($"Cannot mark Planned from {Status}.");

        Status = OrderStatus.Planned;
        AddDomainEvent(new DeliveryOrderPlannedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void MarkDispatched()
    {
        if (FlowRank(Status) >= FlowRank(OrderStatus.Dispatched)) return;
        if (FlowRank(Status) < FlowRank(OrderStatus.Confirmed))
            throw new InvalidOperationException($"Cannot mark Dispatched from {Status}.");

        Status = OrderStatus.Dispatched;
        AddDomainEvent(new DeliveryOrderDispatchedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    /// <summary>
    /// Idempotent forward-only transition triggered by the first
    /// TripStarted webhook. Accepts any in-flight state — including
    /// Confirmed (e.g. after Reopen+retry, the order skips Dispatched).
    /// </summary>
    public void MarkInProgressIfNotYet()
    {
        if (FlowRank(Status) >= FlowRank(OrderStatus.InProgress)) return;
        if (FlowRank(Status) < FlowRank(OrderStatus.Confirmed))
            // Terminal / admin / pre-Confirmed — don't auto-transition out.
            return;

        Status = OrderStatus.InProgress;
        AddDomainEvent(new DeliveryOrderInProgressDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
    }

    public void Hold(string reason)
    {
        if (Status is OrderStatus.Completed or OrderStatus.PartiallyCompleted
                    or OrderStatus.Cancelled or OrderStatus.Failed)
            throw new InvalidOperationException($"Cannot hold an order in {Status} status.");

        Status = OrderStatus.Held;
        AddDomainEvent(new DeliveryOrderHeldDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void Release(double weightFallbackKg)
    {
        if (Status != OrderStatus.Held)
            throw new InvalidOperationException("Only held orders can be released.");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(new DeliveryOrderReleasedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        AddDomainEvent(BuildConfirmedEvent(weightFallbackKg));
    }

    public void MarkFailed(string reason)
    {
        if (Status is OrderStatus.Completed or OrderStatus.PartiallyCompleted or OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot fail an order in {Status} status.");

        Status = OrderStatus.Failed;
        AddDomainEvent(new DeliveryOrderFailedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    /// <summary>
    /// Bring a Failed or Cancelled order back to Confirmed so an operator
    /// can retry the failed/cancelled trip(s). Mirrors the Held → Release
    /// flow but is an explicit admin override: only Failed / Cancelled
    /// (terminal) orders qualify — Rejected and the Completed family stay
    /// locked. Does NOT re-fire the confirmed integration event — the
    /// operator must trigger Trip-level retry separately so the audit
    /// trail distinguishes "who reopened" from "who retried".
    ///
    /// Reopening a Cancelled order also reinstates items the cancel
    /// cascade terminated (Cancelled → Pending). AssignItemsToTrip skips
    /// Cancelled items ("they don't ride retries"), so without this the
    /// retry trip would dispatch a robot with 0 items bound and the order
    /// would later recompute straight to Failed. Delivered items are never
    /// touched; Failed/Returned items are left as-is — the retry rebind
    /// already resets those to Pending.
    /// </summary>
    /// <returns>Number of Cancelled items reinstated to Pending.</returns>
    public int Reopen(string reason)
    {
        if (Status is not (OrderStatus.Failed or OrderStatus.Cancelled))
            throw new InvalidOperationException(
                $"Only Failed or Cancelled orders can be reopened. Current status: {Status}.");

        var reinstated = 0;
        if (Status == OrderStatus.Cancelled)
        {
            foreach (var item in _items)
            {
                if (item.Status != ItemStatus.Cancelled) continue;
                item.ReinstateFromCancel();
                reinstated++;
            }
        }

        Status = OrderStatus.Confirmed;
        AddDomainEvent(new DeliveryOrderReopenedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
        return reinstated;
    }

    /// <summary>
    /// Re-fires the Confirmed integration event so the Planning consumer
    /// re-runs its dispatch loop. The standard Trip-level retry path
    /// (POST /trips/{id}/retry) covers orders whose dispatch produced a
    /// Trip that later went wrong. This is the "no Trip ever
    /// materialised" recovery path — every group failed dispatch
    /// (template not registered, vendor rejected, etc.) and the order
    /// landed at Failed without any Trip the operator can /retry from.
    ///
    /// Required precondition: caller has already moved the order back
    /// to Confirmed via Reopen. Items still Failed are fine — the
    /// Planning consumer rebinds them via AssignItemsToTrip, which
    /// resets Failed items back to Pending as part of the rebind.
    /// </summary>
    public void Redispatch(double weightFallbackKg, string reason)
    {
        if (Status != OrderStatus.Confirmed)
            throw new InvalidOperationException(
                $"Only Confirmed orders can be redispatched. Current status: {Status}. " +
                "If the order is Failed, reopen it first.");

        // Same event the original Confirm raises — the Planning consumer
        // is idempotent for the dispatch loop (it groups items by route
        // and dispatches per group, skipping anything already bound to
        // an in-flight Trip).
        AddDomainEvent(BuildConfirmedEvent(weightFallbackKg));
        // Audit trail entry distinct from the original Confirm so the
        // history makes it clear an operator re-triggered planning.
        AddDomainEvent(new DeliveryOrderRedispatchedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled) return;
        // Cancel during InProgress IS now allowed — the OrderCancelledCascadeConsumer
        // propagates the cancel to all in-flight trips, which in turn calls the
        // vendor's cancel API. Only true terminal / settled states are forbidden.
        if (Status is OrderStatus.Completed or OrderStatus.PartiallyCompleted
                    or OrderStatus.Rejected)
            throw new InvalidOperationException($"Cannot cancel an order in {Status} status.");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason, SourceSystemKey));
    }

    /// <summary>
    /// Mark every item of a (pickup, drop) group as Failed after the
    /// dispatcher couldn't place a vendor order for that group. Used by
    /// the Planning consumer when one group fails but others succeeded —
    /// keeps the failed group's items from blocking the order's eventual
    /// transition to terminal.
    ///
    /// Accepts station Ids (AMR) or WMS location Ids (Manual/Fleet).
    /// An item matches if EITHER pair matches: AMR items match by station,
    /// Manual/Fleet by WMS location.
    /// </summary>
    public int MarkGroupItemsAsDispatchFailed(
        Guid? pickupStationId, Guid? dropStationId,
        Guid? pickupWmsLocationId, Guid? dropWmsLocationId,
        string reason)
    {
        var changed = 0;
        foreach (var item in _items)
        {
            // Station match — both sides of the pair must be supplied AND
            // equal the item's. Empty-Guid sentinels (non-AMR) won't match
            // the items' null IDs and fall through to the WMS check.
            var matchesStation =
                pickupStationId.HasValue && dropStationId.HasValue
                && pickupStationId.Value != Guid.Empty && dropStationId.Value != Guid.Empty
                && item.PickupStationId == pickupStationId
                && item.DropStationId == dropStationId;

            // WMS location match — Manual/Fleet items have WMS Ids populated.
            var matchesWms =
                pickupWmsLocationId.HasValue && dropWmsLocationId.HasValue
                && item.PickupWmsLocationId == pickupWmsLocationId
                && item.DropWmsLocationId == dropWmsLocationId;

            if (!matchesStation && !matchesWms) continue;

            // Don't override items the operator already finalised or items
            // that successfully bound to a Trip (different attempt etc.).
            if (item.Status is ItemStatus.Pending && item.TripId is null)
            {
                item.UpdateStatus(ItemStatus.Failed);
                changed++;
            }
        }
        if (changed > 0)
            AddDomainEvent(new TripItemsFailedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id, Guid.Empty, changed,
                $"Group dispatch failed: {reason}"));
        return changed;
    }

    // Envelope-flow completion: vendor (RIOT3) reported the order finished.
    // No POD scans happen in envelope flow, so we treat the vendor's "task
    // finished" as authoritative and mark all items Delivered. Idempotent —
    // safe to call multiple times if multiple trips complete for the same
    // multi-group order.
    public void MarkVendorCompleted()
    {
        if (Status == OrderStatus.Completed)
            return; // already finalized
        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Failed)
            throw new InvalidOperationException($"Cannot complete an order in {Status} status.");

        foreach (var item in _items)
            if (item.Status != ItemStatus.Delivered)
                item.UpdateStatus(ItemStatus.Delivered);

        Status = OrderStatus.Completed;
        AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, SourceSystemKey));
    }

    // Envelope-flow failure: vendor reported the order failed. Idempotent.
    public void MarkVendorFailed(string reason)
    {
        if (Status == OrderStatus.Failed)
            return; // already finalized
        if (Status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Rejected)
            throw new InvalidOperationException($"Cannot fail an order in {Status} status.");

        Status = OrderStatus.Failed;
        AddDomainEvent(new DeliveryOrderFailedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.InProgress && Status != OrderStatus.Dispatched)
            throw new InvalidOperationException($"Cannot complete an order in {Status} status.");

        // Source of truth = item statuses set by POD scan. Anything not Delivered
        // (Pending/Picked/Failed/Returned/Cancelled) counts as not-delivered.
        var totalCount     = _items.Count;
        var deliveredCount = _items.Count(i => i.Status == ItemStatus.Delivered);
        var notDelivered   = totalCount - deliveredCount;

        if (deliveredCount == 0)
        {
            Status = OrderStatus.Failed;
            AddDomainEvent(new DeliveryOrderFailedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id,
                $"Trip completed but no items were delivered ({totalCount} not delivered)."));
        }
        else if (notDelivered > 0)
        {
            Status = OrderStatus.PartiallyCompleted;
            AddDomainEvent(new DeliveryOrderPartiallyCompletedDomainEvent(
                Guid.NewGuid(), DateTime.UtcNow, Id,
                deliveredCount, notDelivered, totalCount));
        }
        else
        {
            Status = OrderStatus.Completed;
            AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, SourceSystemKey));
        }
    }

    public void AmendServiceWindow(ServiceWindow? newServiceWindow, string reason)
    {
        if (Status is OrderStatus.Draft)
            throw new InvalidOperationException("Cannot amend a Draft order — use UpdateDraft instead.");

        if (Status is OrderStatus.Completed or OrderStatus.PartiallyCompleted or OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot amend a {Status} order.");

        ServiceWindow = newServiceWindow;
        AddDomainEvent(new DeliveryOrderAmendedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }
}
