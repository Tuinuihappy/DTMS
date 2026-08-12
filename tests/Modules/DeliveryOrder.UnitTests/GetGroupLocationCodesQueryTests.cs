using DTMS.DeliveryOrder.Application.Queries.GetGroupLocationCodes;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// 2026-08 — resolves the source system's own location codes for one dispatch
// group, matched by resolved pair (stations for AMR, WMS for Manual).
// Deliberately soft: misses return (null, null), never a failure.
public class GetGroupLocationCodesQueryTests
{
    private static readonly Guid PickupStation = Guid.NewGuid();
    private static readonly Guid DropStation = Guid.NewGuid();

    private static DomainOrder AmrOrder(out Guid orderId)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-Q-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS");
        order.AddItem("SHELF1", "STF_09", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["SHELF1"] = PickupStation, ["STF_09"] = DropStation });
        order.Confirm(weightFallbackKg: 5.0);
        orderId = order.Id;
        return order;
    }

    private static GetGroupLocationCodesQueryHandler Handler(DomainOrder? order, Guid orderId)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        repo.GetByIdAsNoTrackingAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        return new GetGroupLocationCodesQueryHandler(repo);
    }

    [Fact]
    public async Task StationPairMatch_ReturnsTheItemsOwnCodes()
    {
        var order = AmrOrder(out var orderId);
        var handler = Handler(order, orderId);

        var result = await handler.Handle(
            new GetGroupLocationCodesQuery(orderId,
                PickupStationId: PickupStation, DropStationId: DropStation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PickupCode.Should().Be("SHELF1");
        result.Value.DropCode.Should().Be("STF_09");
    }

    [Fact]
    public async Task WmsPairMatch_ReturnsTheItemsOwnCodes()
    {
        var pickupWms = Guid.NewGuid();
        var dropWms = Guid.NewGuid();
        var order = DomainOrder.CreateFromUpstream(
            "OD-QW-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS",
            requestedTransportMode: TransportMode.Manual);
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(stationMap: null,
            wmsLocationMap: new Dictionary<string, Guid> { ["WH-A"] = pickupWms, ["DOCK-1"] = dropWms });
        order.Confirm(weightFallbackKg: 5.0);
        var handler = Handler(order, order.Id);

        var result = await handler.Handle(
            new GetGroupLocationCodesQuery(order.Id,
                PickupWmsLocationId: pickupWms, DropWmsLocationId: dropWms),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PickupCode.Should().Be("WH-A");
        result.Value.DropCode.Should().Be("DOCK-1");
    }

    [Fact]
    public async Task UnmatchedPair_ReturnsNulls_NotFailure()
    {
        var order = AmrOrder(out var orderId);
        var handler = Handler(order, orderId);

        var result = await handler.Handle(
            new GetGroupLocationCodesQuery(orderId,
                PickupStationId: Guid.NewGuid(), DropStationId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PickupCode.Should().BeNull();
        result.Value.DropCode.Should().BeNull();
    }

    [Fact]
    public async Task OrderMissing_ReturnsNulls_NotFailure()
    {
        var orderId = Guid.NewGuid();
        var handler = Handler(order: null, orderId);

        var result = await handler.Handle(
            new GetGroupLocationCodesQuery(orderId, PickupStationId: PickupStation, DropStationId: DropStation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PickupCode.Should().BeNull();
    }
}
