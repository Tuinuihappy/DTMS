using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using DomainItem = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.Item;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

// Strongly-typed amendment snapshot. Decoupled from the read/write command
// DTOs on purpose — the snapshot must remain interpretable even if those
// DTOs evolve. Bump the `Version` constant + the OrderAmendment.AmendmentVersion
// column together when the shape changes (e.g. P2 adds OrderTemplate ref).

public record ServiceWindowSnapshotV1(DateTime? Earliest, DateTime? Latest);

public record DimensionsSnapshotV1(double LengthMm, double WidthMm, double HeightMm);

public record QuantitySnapshotV1(double Value, string Uom);

public record HazmatSnapshotV1(string ClassCode, string? PackingGroup);

public record TemperatureSnapshotV1(double? MinC, double? MaxC);

public record CargoSpecificSnapshotV1(
    string? PartNo, string? Wo, string? Line, string? Vendor,
    string? DateCode, string? TradingCode, string? InventoryNo,
    string? Po, string? TraceId, string? LotNo);

public record ItemSnapshotV1(
    int ItemSeq,
    string Sku,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    string? LoadUnitProfileCode,
    DimensionsSnapshotV1? Dimensions,
    double? WeightKg,
    QuantitySnapshotV1 Quantity,
    string? CargoType,
    CargoSpecificSnapshotV1? CargoSpecific,
    HazmatSnapshotV1? Hazmat,
    TemperatureSnapshotV1? Temperature,
    IReadOnlyList<string> HandlingInstructions);

public record OrderSnapshotV1(
    string Version,
    string OrderRef,
    string Priority,
    string SlaTier,
    string OrderStatus,
    DateTime? SubmittedAt,
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
            SlaTier: order.SlaTier.ToString(),
            OrderStatus: order.Status.ToString(),
            SubmittedAt: order.SubmittedAt,
            ServiceWindow: order.ServiceWindow is { } sw
                ? new ServiceWindowSnapshotV1(sw.Earliest, sw.Latest)
                : null,
            Items: order.Items.Select(ToItemSnapshot).ToList());

    private static ItemSnapshotV1 ToItemSnapshot(DomainItem p) =>
        new(
            ItemSeq: p.ItemSeq,
            Sku: p.Sku,
            Description: p.Description,
            PickupLocationCode: p.PickupLocationCode,
            DropLocationCode: p.DropLocationCode,
            LoadUnitProfileCode: p.LoadUnitProfileCode,
            Dimensions: p.Dimensions is { } d
                ? new DimensionsSnapshotV1(d.LengthMm, d.WidthMm, d.HeightMm)
                : null,
            WeightKg: p.WeightKg,
            Quantity: new QuantitySnapshotV1(p.Quantity.Value, p.Quantity.Uom.ToString()),
            CargoType: p.CargoType?.ToString(),
            CargoSpecific: p.CargoSpecific is { } cs
                ? new CargoSpecificSnapshotV1(cs.PartNo, cs.Wo, cs.Line, cs.Vendor,
                    cs.DateCode, cs.TradingCode, cs.InventoryNo, cs.Po, cs.TraceId, cs.LotNo)
                : null,
            Hazmat: p.Hazmat is { } hz
                ? new HazmatSnapshotV1(hz.ClassCode, hz.PackingGroup?.ToString())
                : null,
            Temperature: p.Temperature is { } tr
                ? new TemperatureSnapshotV1(tr.MinC, tr.MaxC)
                : null,
            HandlingInstructions: p.HandlingInstructions.Select(h => h.ToString()).ToList());
}
