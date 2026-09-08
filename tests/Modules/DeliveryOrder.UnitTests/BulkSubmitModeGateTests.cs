using DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;
using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Application.Services;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Confirm-time mode gate on the bulk path: an unregistered mode becomes a
// per-order BulkSubmitFailure (never a thrown exception — that would abort
// the batch and break the 200/207/400 envelope). Message text is owned by
// TransportModeNotEnabledException — assert the OrderRef, not the string.
public class BulkSubmitModeGateTests
{
    [Fact]
    public async Task MixedModes_CollectsFailureForUnregistered_PersistsRegistered()
    {
        var (handler, repo, stations) = Build(manualRegistered: false);
        StationsSucceed(stations);
        var cmd = new BulkSubmitDeliveryOrdersCommand([
            Order("BULK-AMR", TransportMode.Amr),
            Order("BULK-MAN", TransportMode.Manual),
        ]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().HaveCount(1);
        result.Value.Succeeded.Single().Order.OrderRef.Should().Be("BULK-AMR");
        result.Value.Failures.Should().HaveCount(1);
        result.Value.Failures.Single().OrderRef.Should().Be("BULK-MAN");
        await repo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<DomainOrder>>(orders => orders.Count() == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllUnregistered_NoPersistence()
    {
        var (handler, repo, _) = Build(manualRegistered: false);
        var cmd = new BulkSubmitDeliveryOrdersCommand([Order("BULK-MAN", TransportMode.Manual)]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeEmpty();
        result.Value.Failures.Should().HaveCount(1);
        await repo.DidNotReceive().AddRangeAsync(
            Arg.Any<IEnumerable<DomainOrder>>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static (BulkSubmitDeliveryOrdersCommandHandler handler,
        IDeliveryOrderRepository repo,
        IStationValidationService stations) Build(bool manualRegistered)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var stations = Substitute.For<IStationValidationService>();
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetInternalAsync(Arg.Any<CancellationToken>())
            .Returns(new OrderOrigin("manual", "Manual"));
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(TransportMode.Amr).Returns(true);
        registry.IsRegistered(TransportMode.Manual).Returns(manualRegistered);

        var handler = new BulkSubmitDeliveryOrdersCommandHandler(
            repo, stations, Substitute.For<ICurrentUserAccessor>(),
            origins, registry, Options.Create(new DeliveryOrderOptions()));
        return (handler, repo, stations);
    }

    private static CreateDraftDeliveryOrderCommand Order(string orderRef, TransportMode mode) => new(
        orderRef,
        new ServiceWindowDto(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
        [
            new ItemDto(
                ItemId: "SKU-1", Description: null,
                PickupLocationCode: "WH-A", DropLocationCode: "DOCK-1",
                LoadUnitProfileCode: null, Dimensions: null, WeightKg: 1.0,
                Quantity: new QuantityDto(1, "EA")),
        ],
        RequestedTransportMode: mode);

    private static void StationsSucceed(IStationValidationService stations)
        => stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(
                new Dictionary<string, Guid> { ["WH-A"] = Guid.NewGuid(), ["DOCK-1"] = Guid.NewGuid() }));
}
