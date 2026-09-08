using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Application.Services;
using DTMS.SharedKernel.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Confirm-time mode gate on the federated source path: this handler
// creates + confirms in one call, so an unregistered mode must be rejected
// (422 via middleware) before anything persists. The gate sits AFTER the
// idempotency lookup — replays of an order accepted while its mode was
// enabled must still return the original ack.
public class UpstreamModeGateTests
{
    [Fact]
    public async Task UnregisteredMode_Throws_NothingPersisted()
    {
        var (handler, repo, activity) = Build(manualRegistered: false);

        var act = () => handler.Handle(
            Command(mode: TransportMode.Manual), CancellationToken.None);

        (await act.Should().ThrowAsync<TransportModeNotEnabledException>())
            .Which.Mode.Should().Be(TransportMode.Manual);
        await repo.DidNotReceive().AddAsync(Arg.Any<DomainOrder>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await activity.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default!, default!, default, default,
            default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task DuplicateOrderRef_ReportsTheConflict_NotTheDisabledMode()
    {
        // Gate placement: the ref lookup runs first, so a caller reusing a ref
        // is told that — not that a mode they didn't choose to disable is off.
        // Both conditions hold here; only the one the caller can act on is
        // reported.
        var (handler, repo, _) = Build(manualRegistered: false);
        var existing = DomainOrder.CreateFromUpstream(
            "OD-GATE-01", Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
            "oms", "OMS", "OMS", null, null, TransportMode.Manual, false);
        repo.GetByRefAsync("oms", "OD-GATE-01", Arg.Any<CancellationToken>()).Returns(existing);
        repo.GetByIdAsNoTrackingAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var act = () => handler.Handle(
            Command(orderRef: "OD-GATE-01", mode: TransportMode.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateValueException>();
    }

    private static (CreateUpstreamDeliveryOrderCommandHandler handler,
        IDeliveryOrderRepository repo,
        IOrderActivityProjectionStore activity) Build(bool manualRegistered)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetByKeyAsync("oms", Arg.Any<CancellationToken>())
            .Returns(new OrderOrigin("oms", "OMS"));
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(TransportMode.Amr).Returns(true);
        registry.IsRegistered(TransportMode.Manual).Returns(manualRegistered);

        var handler = new CreateUpstreamDeliveryOrderCommandHandler(
            repo, Substitute.For<IOrderAuditEventRepository>(), activity,
            Substitute.For<IStationValidationService>(),
            Substitute.For<ICurrentUserAccessor>(), origins, registry,
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<CreateUpstreamDeliveryOrderCommandHandler>.Instance);
        return (handler, repo, activity);
    }

    private static CreateUpstreamDeliveryOrderCommand Command(
        string orderRef = "OD-GATE-01", TransportMode mode = TransportMode.Manual) => new(
        orderRef,
        new ServiceWindowDto(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
        [
            new ItemDto(
                ItemId: "SKU-1", Description: null,
                PickupLocationCode: "LOC-A", DropLocationCode: "LOC-B",
                LoadUnitProfileCode: null, Dimensions: null, WeightKg: 1.0,
                Quantity: new QuantityDto(1, "EA")),
        ],
        SourceSystemKey: "oms",
        RequestedTransportMode: mode);
}
