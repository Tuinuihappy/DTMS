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
    /// the order is still live (operator may retry).</summary>
    internal void UnassignFromTrip()
    {
        TripId = null;
        AttemptNumber = null;
    }
}
