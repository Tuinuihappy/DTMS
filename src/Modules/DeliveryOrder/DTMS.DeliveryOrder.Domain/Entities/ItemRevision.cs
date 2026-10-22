using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;

namespace DTMS.DeliveryOrder.Domain.Entities;

/// <summary>
/// One line of a Failed-order revision (<see cref="DeliveryOrder.ReviseFailed"/>).
/// <see cref="ExistingItemId"/> names the item being edited in place; null
/// means a new item. Items of the order not named by any revision line are
/// removed.
/// </summary>
public sealed record ItemRevision(
    Guid? ExistingItemId,
    string PickupLocationCode,
    string DropLocationCode,
    string ItemId,
    string? Description,
    string? LoadUnitProfileCode,
    Dimensions? Dimensions,
    double? WeightKg,
    Quantity Quantity,
    HazmatInfo? Hazmat = null,
    TemperatureRange? Temperature = null,
    IReadOnlyList<HandlingInstruction>? HandlingInstructions = null);

/// <summary>What <see cref="DeliveryOrder.ReviseFailed"/> changed in the item
/// collection — the repository needs the added and removed rows explicitly
/// (client-generated Guid keys would otherwise be tracked as Modified).</summary>
public sealed record ItemRevisionOutcome(
    IReadOnlyList<Item> Added,
    IReadOnlyList<Item> Removed);
