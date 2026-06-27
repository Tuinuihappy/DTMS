using DomainItem = DTMS.DeliveryOrder.Domain.Entities.Item;

namespace DTMS.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

// Strongly-typed amendment snapshot. Decoupled from the read/write command
// DTOs on purpose — the snapshot must remain interpretable even if those
// DTOs evolve. Bump the `Version` constant + the OrderAmendment.AmendmentVersion
// column together when the shape changes.

public record ServiceWindowSnapshotV1(DateTime? EarliestUtc, DateTime? LatestUtc);

public record DimensionsSnapshotV1(double LengthMm, double WidthMm, double HeightMm);

public record QuantitySnapshotV1(double Value, string Uom);

public record HazmatSnapshotV1(string ClassCode, string? PackingGroup);

public record TemperatureSnapshotV1(double? MinC, double? MaxC);

public record ItemSnapshotV1(
    int ItemSeq,
    string ItemId,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    string? LoadUnitProfileCode,
    DimensionsSnapshotV1? Dimensions,
    double? WeightKg,
    QuantitySnapshotV1 Quantity,
    HazmatSnapshotV1? Hazmat,
    TemperatureSnapshotV1? Temperature,
    IReadOnlyList<string> HandlingInstructions);

public record OrderSnapshotV1(
    string Version,
    string OrderRef,
    string Priority,
    string OrderStatus,
    DateTime? SubmittedAt,
    string? RequestedBy,
    string? Notes,
    ServiceWindowSnapshotV1? ServiceWindow,
    IReadOnlyList<ItemSnapshotV1> Items)
{
    /// <summary>Snapshot shape revision. Bump together with the column.</summary>
    public const string SchemaVersion = "1.0";

    public static OrderSnapshotV1 From(Domain.Entities.DeliveryOrder order) =>
        new(
            Version: SchemaVersion,
            OrderRef: order.OrderRef,
            Priority: order.Priority.ToString(),
            OrderStatus: order.Status.ToString(),
            SubmittedAt: order.SubmittedAt,
            RequestedBy: order.RequestedBy,
            Notes: order.Notes,
            ServiceWindow: order.ServiceWindow is { } sw
                ? new ServiceWindowSnapshotV1(sw.EarliestUtc, sw.LatestUtc)
                : null,
            Items: order.Items.Select(ToItemSnapshot).ToList());

    private static ItemSnapshotV1 ToItemSnapshot(DomainItem p) =>
        new(
            ItemSeq: p.ItemSeq,
            ItemId: p.ItemId,
            Description: p.Description,
            PickupLocationCode: p.PickupLocationCode,
            DropLocationCode: p.DropLocationCode,
            LoadUnitProfileCode: p.LoadUnitProfileCode,
            Dimensions: p.Dimensions is { } d
                ? new DimensionsSnapshotV1(d.LengthMm, d.WidthMm, d.HeightMm)
                : null,
            WeightKg: p.WeightKg,
            Quantity: new QuantitySnapshotV1(p.Quantity.Value, p.Quantity.Uom.ToString()),
            Hazmat: p.Hazmat is { } hz
                ? new HazmatSnapshotV1(hz.ClassCode, hz.PackingGroup?.ToString())
                : null,
            Temperature: p.Temperature is { } tr
                ? new TemperatureSnapshotV1(tr.MinC, tr.MaxC)
                : null,
            HandlingInstructions: p.HandlingInstructions.Select(h => h.ToString()).ToList());
}
