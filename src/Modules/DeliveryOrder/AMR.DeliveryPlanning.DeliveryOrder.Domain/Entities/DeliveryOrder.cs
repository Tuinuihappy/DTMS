using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class DeliveryOrder : AggregateRoot<Guid>, IAuditable
{
    public string OrderRef { get; private set; } = string.Empty;
    public SourceSystem SourceSystem { get; private set; }
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
    /// to false (current behaviour). Per-order override; null falls back
    /// to whatever the route's OrderTemplate.RequiresPod says.
    /// </summary>
    public bool? RequiresPod { get; private set; }

    private readonly List<Item> _items = new();
    public IReadOnlyCollection<Item> Items => _items.AsReadOnly();

    private DeliveryOrder() { }

    void IAuditable.SetCreatedAt(DateTime createdAt) => CreatedDate = createdAt;
    void IAuditable.SetUpdatedAt(DateTime updatedAt) => UpdatedDate = updatedAt;

    public static DeliveryOrder Create(string orderRef, Priority priority,
        ServiceWindow? serviceWindow, SourceSystem sourceSystem = SourceSystem.Manual,
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
            SourceSystem = sourceSystem,
            CreatedBy = createdBy,
            RequestedBy = requestedBy,
            Notes = notes,
            RequestedTransportMode = requestedTransportMode
        };

        order.AddDomainEvent(new DeliveryOrderDraftedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id));
        return order;
    }

    public static DeliveryOrder CreateFromUpstream(string orderRef, Priority priority,
        ServiceWindow? serviceWindow, SourceSystem sourceSystem, string? createdBy = null,
        string? requestedBy = null, string? notes = null,
        TransportMode? requestedTransportMode = Enums.TransportMode.Amr)
    {
        if (sourceSystem == SourceSystem.Manual)
            throw new InvalidOperationException("Upstream orders cannot have Manual source system.");

        var order = new DeliveryOrder
        {
            Id = Guid.NewGuid(),
            OrderRef = orderRef,
            Priority = priority,
            ServiceWindow = serviceWindow,
            Status = OrderStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
            SourceSystem = sourceSystem,
            CreatedBy = createdBy,
            RequestedBy = requestedBy,
            Notes = notes,
            RequestedTransportMode = requestedTransportMode
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
    public int AssignItemsToTrip(Guid tripId, int attemptNumber, Guid pickupStationId, Guid dropStationId)
    {
        if (tripId == Guid.Empty)
            throw new ArgumentException("TripId must not be empty.", nameof(tripId));

        var bound = 0;
        foreach (var item in _items)
        {
            if (item.PickupStationId != pickupStationId || item.DropStationId != dropStationId)
                continue;
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
    /// to OrderTemplate.RequiresPod at TASK_FINISHED time.</summary>
    public void SetRequiresPod(bool? requiresPod) => RequiresPod = requiresPod;

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
    /// Confirm POD for a single item — Picked/DroppedOff → Delivered.
    /// Called from the POST /pod-scan endpoint. Returns the count of
    /// items that transitioned (0 or 1). Recompute is the caller's
    /// responsibility so a batch scan can do one pass at the end.
    /// </summary>
    public int ConfirmItemPod(Guid itemId, string scannedBy, string method, string? reference)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return 0;
        if (item.Status is ItemStatus.Delivered) return 0;
        var before = item.Status;
        item.ConfirmPodAndDeliver(scannedBy, method, reference);
        if (item.Status == before) return 0;
        AddDomainEvent(new ItemPodConfirmedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, itemId, scannedBy, method));
        return 1;
    }

    /// <summary>
    /// TASK_FINISHED arrived. Behaviour splits on the effective POD
    /// policy: if POD is required, leave DroppedOff items alone — they
    /// need /pod-scan to land at Delivered. Otherwise the existing
    /// MarkTripItemsDelivered runs (Pending/Picked/DroppedOff all jump
    /// to Delivered).
    /// </summary>
    public int MarkTripItemsDeliveredOrLeaveForPod(Guid tripId, bool templateRequiresPod)
    {
        var effective = RequiresPod ?? templateRequiresPod;
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
            AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
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

    public void MarkAsValidated(IReadOnlyDictionary<string, Guid> stationMap)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only submitted orders can be validated.");

        foreach (var item in _items)
        {
            if (!stationMap.TryGetValue(item.PickupLocationCode, out var pickupId))
                throw new InvalidOperationException($"Missing station mapping for pickup {item.PickupLocationCode}.");
            if (!stationMap.TryGetValue(item.DropLocationCode, out var dropId))
                throw new InvalidOperationException($"Missing station mapping for drop {item.DropLocationCode}.");
            item.SetStationIds(pickupId, dropId);
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
        var itemDtos = _items
            .Select(p => new ItemEventDto(
                p.ItemId, p.WeightKg ?? weightFallbackKg,
                p.PickupStationId!.Value, p.DropStationId!.Value,
                p.Hazmat is { } hz ? new ItemHazmatDto(hz.ClassCode, hz.PackingGroup?.ToString()) : null,
                p.Temperature is { } tr ? new ItemTemperatureDto(tr.MinC, tr.MaxC) : null,
                p.HandlingInstructions.Count > 0
                    ? p.HandlingInstructions.Select(h => h.ToString()).ToList()
                    : null))
            .ToList();

        return new DeliveryOrderConfirmedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Id, Priority.ToString(),
            ServiceWindow?.EarliestUtc, ServiceWindow?.LatestUtc,
            SubmittedAt, itemDtos, RequestedTransportMode?.ToString());
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
    /// Bring a Failed order back to Confirmed so an operator can retry
    /// the failed trip(s). Mirrors the Held → Release flow but is an
    /// explicit admin override: only Failed (terminal) orders qualify.
    /// Does NOT re-fire the confirmed integration event — the operator
    /// must trigger Trip-level retry separately so the audit trail
    /// distinguishes "who reopened" from "who retried".
    /// </summary>
    public void Reopen(string reason)
    {
        if (Status != OrderStatus.Failed)
            throw new InvalidOperationException(
                $"Only Failed orders can be reopened. Current status: {Status}.");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(new DeliveryOrderReopenedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
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
        AddDomainEvent(new DeliveryOrderCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
    }

    /// <summary>
    /// Mark every item of a (pickup, drop) station-pair group as Failed
    /// after the dispatcher couldn't place a vendor order for that group.
    /// Used by the Planning consumer when one group fails but others
    /// succeeded — keeps the failed group's items from blocking the
    /// order's eventual transition to terminal.
    /// </summary>
    public int MarkGroupItemsAsDispatchFailed(Guid pickupStationId, Guid dropStationId, string reason)
    {
        var changed = 0;
        foreach (var item in _items)
        {
            if (item.PickupStationId != pickupStationId || item.DropStationId != dropStationId)
                continue;
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
                $"Group ({pickupStationId}→{dropStationId}) dispatch failed: {reason}"));
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
        AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
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
            AddDomainEvent(new DeliveryOrderCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
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
