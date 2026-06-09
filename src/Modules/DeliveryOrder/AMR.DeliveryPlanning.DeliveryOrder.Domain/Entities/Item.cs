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
    // Populated when the operator submits /pod-scan against a DroppedOff
    // item. PodScannedAt is the audit-grade timestamp; DroppedOffAt is
    // the SLA clock anchor.
    public DateTime? DroppedOffAt { get; private set; }
    public DateTime? PodScannedAt { get; private set; }
    public string? PodScannedBy { get; private set; }
    public string? PodMethod { get; private set; }    // "Barcode" / "Manual" / "Signature" / "Confirm"
    public string? PodReference { get; private set; } // scanned code / signature hash / null

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

    /// <summary>Operator scanned POD — item is now Delivered with audit.
    /// Accepts from both Picked and DroppedOff (DroppedOff is the common
    /// path; Picked allows the rare race where TASK_FINISHED + POD scan
    /// arrive before the drop-station SUB_TASK_FINISHED).</summary>
    internal void ConfirmPodAndDeliver(string scannedBy, string method, string? reference)
    {
        if (Status is ItemStatus.Delivered) return;
        if (Status is not (ItemStatus.Picked or ItemStatus.DroppedOff))
            throw new InvalidOperationException(
                $"Cannot confirm POD from {Status}.");
        Status = ItemStatus.Delivered;
        PodScannedAt = DateTime.UtcNow;
        PodScannedBy = scannedBy;
        PodMethod = method;
        PodReference = reference;
    }
}
