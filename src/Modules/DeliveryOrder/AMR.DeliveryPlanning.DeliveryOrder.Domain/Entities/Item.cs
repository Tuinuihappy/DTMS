using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class Item : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string PickupLocationCode { get; private set; } = string.Empty;
    public string DropLocationCode { get; private set; } = string.Empty;
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    // Phase 2.5 — per ADR-002, every order references a Warehouse (building/site);
    // AMR additionally references a specific Station (dock within the warehouse).
    // Nullable now (Phase 2.5 just adds the slot); Phase 2.6 wires IWarehouseLookup
    // into the validation pipeline so PickupWarehouseId / DropWarehouseId actually
    // get populated alongside PickupStationId / DropStationId.
    public Guid? PickupWarehouseId { get; private set; }
    public Guid? DropWarehouseId { get; private set; }
    public int ItemSeq { get; private set; }
    // Upstream-supplied identifier for this item instance (e.g. SAP/ERP item id,
    // scanner barcode). Unique within an order — used by POD scan matching.
    public string ItemId { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? LoadUnitProfileCode { get; private set; }
    public Dimensions? Dimensions { get; private set; }
    public double? WeightKg { get; private set; }
    public Quantity Quantity { get; private set; } = null!;
    public HazmatInfo? Hazmat { get; private set; }
    public TemperatureRange? Temperature { get; private set; }
    public IReadOnlyList<HandlingInstruction> HandlingInstructions { get; private set; }
        = Array.Empty<HandlingInstruction>();
    public ItemStatus Status { get; private set; }

    /// <summary>The Trip currently dispatching this item, or null when the
    /// item is awaiting first dispatch / has been unbound after a retryable
    /// cancellation. Rebound at retry time so the latest attempt is the
    /// authoritative owner.</summary>
    public Guid? TripId { get; private set; }

    /// <summary>Attempt number of the Trip this item is bound to (1 = first
    /// dispatch). Mirrors Trip.AttemptNumber so per-item audit queries
    /// don't have to join.</summary>
    public int? AttemptNumber { get; private set; }

    // ── POD (Proof of Delivery) evidence ───────────────────────────────
    // DroppedOffAt is the SLA clock anchor stamped when the vendor drop
    // sub-task completes. Per-checkpoint POD scans live on PodEvents —
    // at most one event per (ItemId, ScanType) pair (Pickup, Drop). UI
    // surfaces them as two chips per item; consumers project them into
    // pickupPod / dropPod DTOs.
    public DateTime? DroppedOffAt { get; private set; }

    private readonly List<ItemPodEvent> _podEvents = new();
    public IReadOnlyCollection<ItemPodEvent> PodEvents => _podEvents.AsReadOnly();

    /// <summary>Operator scan recorded at the pickup station, or null if
    /// pickup POD wasn't required / hasn't been scanned yet.</summary>
    public ItemPodEvent? PickupPod => _podEvents.FirstOrDefault(e => e.ScanType == PodScanType.Pickup);

    /// <summary>Operator scan recorded at the drop dock, or null if drop
    /// POD wasn't required / hasn't been scanned yet.</summary>
    public ItemPodEvent? DropPod => _podEvents.FirstOrDefault(e => e.ScanType == PodScanType.Drop);

    private Item() { }

    internal Item(Guid deliveryOrderId, string pickupLocationCode, string dropLocationCode,
        int itemSeq, string itemId, string? description,
        string? loadUnitProfileCode,
        Dimensions? dimensions, double? weightKg, Quantity quantity,
        HazmatInfo? hazmat = null,
        TemperatureRange? temperature = null,
        IReadOnlyList<HandlingInstruction>? handlingInstructions = null)
    {
        if (string.IsNullOrWhiteSpace(pickupLocationCode))
            throw new ArgumentException("PickupLocationCode must not be empty.", nameof(pickupLocationCode));
        if (string.IsNullOrWhiteSpace(dropLocationCode))
            throw new ArgumentException("DropLocationCode must not be empty.", nameof(dropLocationCode));
        ArgumentNullException.ThrowIfNull(quantity);

        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        PickupLocationCode = pickupLocationCode.Trim();
        DropLocationCode = dropLocationCode.Trim();
        ItemSeq = itemSeq;
        ItemId = itemId;
        Description = description;
        LoadUnitProfileCode = loadUnitProfileCode;
        Dimensions = dimensions;
        WeightKg = weightKg;
        Quantity = quantity;
        Hazmat = hazmat;
        Temperature = temperature;
        // Dedupe while preserving order: ["Fragile","Fragile","ThisSideUp"] → ["Fragile","ThisSideUp"].
        // Caller intent is "this item has these handling traits", not a sequence.
        HandlingInstructions = handlingInstructions is null
            ? Array.Empty<HandlingInstruction>()
            : handlingInstructions.Distinct().ToArray();
        Status = ItemStatus.Pending;
    }

    internal void SetStationIds(Guid pickupStationId, Guid dropStationId)
    {
        PickupStationId = pickupStationId;
        DropStationId = dropStationId;
    }

    /// <summary>
    /// Set the warehouse Ids resolved from PickupLocationCode / DropLocationCode.
    /// Phase 2.5 introduces the slot; Phase 2.6 wires IWarehouseLookup to call
    /// this alongside the existing SetStationIds. Internal because warehouse
    /// resolution belongs to the order validation pipeline (Application layer),
    /// not arbitrary callers.
    /// </summary>
    internal void SetWarehouseIds(Guid pickupWarehouseId, Guid dropWarehouseId)
    {
        if (pickupWarehouseId == Guid.Empty)
            throw new ArgumentException("PickupWarehouseId must not be empty.", nameof(pickupWarehouseId));
        if (dropWarehouseId == Guid.Empty)
            throw new ArgumentException("DropWarehouseId must not be empty.", nameof(dropWarehouseId));

        PickupWarehouseId = pickupWarehouseId;
        DropWarehouseId = dropWarehouseId;
    }

    internal void UpdateStatus(ItemStatus status) => Status = status;

    /// <summary>Bind the item to a Trip. A re-bind (different TripId or
    /// higher AttemptNumber) resets a Failed/Returned status back to
    /// Pending so the new trip can drive it terminal again.</summary>
    internal void AssignToTrip(Guid tripId, int attemptNumber)
    {
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "AttemptNumber must be >= 1.");

        var isRebind = TripId != tripId || (AttemptNumber ?? 0) < attemptNumber;
        TripId = tripId;
        AttemptNumber = attemptNumber;

        if (isRebind && Status is ItemStatus.Failed or ItemStatus.Returned)
            Status = ItemStatus.Pending;
    }

    /// <summary>Remove the trip binding so the item is discoverable as
    /// "awaiting dispatch" again. Used when an envelope is cancelled and
    /// the order is still live (operator may retry). If the item had
    /// already been Picked, reset it to Pending — the cancel cascade
    /// means the item never actually reached the drop station, so the
    /// in-transit status no longer reflects reality.</summary>
    internal void UnassignFromTrip()
    {
        TripId = null;
        AttemptNumber = null;
        // Cancel cascade after pickup OR drop — the trip is gone, the
        // item is no longer "in transit" or "at the dock". Reset to
        // Pending so retry can rebind cleanly. Delivered items are
        // never touched (terminal).
        if (Status is ItemStatus.Picked or ItemStatus.DroppedOff)
        {
            Status = ItemStatus.Pending;
            DroppedOffAt = null;
        }
    }

    /// <summary>Follow the order to a Cancelled terminal state. Used when
    /// the order itself is admin-cancelled (operator hit "Cancel order"
    /// in the UI) — the cascade unbinds items from in-flight trips, but
    /// without this they'd sit at Pending forever because the order is
    /// admin-locked at Cancelled and won't dispatch again. Delivered
    /// items are never touched.</summary>
    internal void MarkCancelled()
    {
        if (Status is ItemStatus.Delivered or ItemStatus.Cancelled) return;
        Status = ItemStatus.Cancelled;
    }

    /// <summary>True when the item has reached one of the irreversible
    /// states the consumer flow stops touching.</summary>
    internal bool IsTerminal =>
        Status is ItemStatus.Delivered
               or ItemStatus.Failed
               or ItemStatus.Cancelled
               or ItemStatus.Returned;

    /// <summary>Transition Pending → Picked when the vendor reports the
    /// robot finished its pickup action at the trip's pickup station.
    /// Idempotent against duplicate webhooks (Picked → Picked = no-op).
    /// Strictly forward-only: refuses to back-step from Delivered or
    /// any terminal failure state.</summary>
    internal void MarkPicked()
    {
        if (Status is ItemStatus.Picked) return;
        if (Status is not ItemStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot mark item Picked from {Status}.");
        Status = ItemStatus.Picked;
    }

    /// <summary>Transition Picked → DroppedOff when the vendor reports the
    /// robot finished its drop action at the trip's drop station. The
    /// item is physically at the dock but not yet POD-confirmed. Records
    /// DroppedOffAt as the SLA anchor for the POD-overdue chip.</summary>
    internal void MarkDroppedOff()
    {
        if (Status is ItemStatus.DroppedOff or ItemStatus.Delivered) return;
        if (Status is not ItemStatus.Picked)
            throw new InvalidOperationException(
                $"Cannot mark item DroppedOff from {Status}.");
        Status = ItemStatus.DroppedOff;
        DroppedOffAt = DateTime.UtcNow;
    }

    /// <summary>Audit-only pickup POD scan. Does NOT flip Item.Status —
    /// vendor's SUB_TASK_FINISHED at the pickup station already drove
    /// Pending → Picked. Idempotent: a second pickup scan on an item
    /// that already has one is a no-op (returns false).</summary>
    internal bool RecordPickupPod(string scannedBy, string method, string? reference)
    {
        if (PickupPod is not null) return false;   // idempotent
        _podEvents.Add(new ItemPodEvent(Id, PodScanType.Pickup, scannedBy, method, reference));
        return true;
    }

    /// <summary>Operator scanned the drop POD. Records the audit row and,
    /// if the item is in a valid pre-Delivered state, transitions to
    /// Delivered. Accepts from Picked or DroppedOff (DroppedOff is the
    /// common path; Picked covers the race where TASK_FINISHED + POD scan
    /// arrive before the drop SUB_TASK_FINISHED). Idempotent: a second
    /// drop scan returns false without mutating the event row.</summary>
    internal bool RecordDropPod(string scannedBy, string method, string? reference)
    {
        if (DropPod is not null) return false;   // idempotent
        if (Status is not (ItemStatus.Picked or ItemStatus.DroppedOff or ItemStatus.Delivered))
            throw new InvalidOperationException(
                $"Cannot record drop POD from {Status}.");
        _podEvents.Add(new ItemPodEvent(Id, PodScanType.Drop, scannedBy, method, reference));
        if (Status is ItemStatus.Picked or ItemStatus.DroppedOff)
            Status = ItemStatus.Delivered;
        return true;
    }
}
