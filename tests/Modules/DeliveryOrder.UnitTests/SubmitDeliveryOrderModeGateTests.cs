using DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Application.Services;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Confirm-time mode gate: submitting an order whose TransportMode has no
// registered IDispatchStrategy must throw TransportModeNotEnabledException
// (mapped to 422 by ExceptionHandlingMiddleware) BEFORE any location
// resolution or persistence. Message text is owned by the exception —
// assert type + Mode only, never the string.
public class SubmitDeliveryOrderModeGateTests
{
    [Fact]
    public async Task UnregisteredMode_ThrowsTransportModeNotEnabled_NothingPersisted()
    {
        var (handler, repo, stations, _) = Build(manualRegistered: false);
        var order = DraftOrder(TransportMode.Manual);
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => handler.Handle(new SubmitDeliveryOrderCommand(order.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<TransportModeNotEnabledException>())
            .Which.Mode.Should().Be(TransportMode.Manual);
        await stations.DidNotReceive().BuildStationMapAsync(
            Arg.Any<IEnumerable<DTMS.DeliveryOrder.Domain.Entities.Item>>(), Arg.Any<CancellationToken>());
        await stations.DidNotReceive().BuildWmsLocationMapAsync(
            Arg.Any<IEnumerable<DTMS.DeliveryOrder.Domain.Entities.Item>>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisteredMode_PassesGate_ConfirmsOrder()
    {
        var (handler, repo, stations, _) = Build(manualRegistered: true);
        var order = DraftOrder(TransportMode.Amr);
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        stations.BuildStationMapAsync(
                Arg.Any<IEnumerable<DTMS.DeliveryOrder.Domain.Entities.Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(
                new Dictionary<string, Guid> { ["WH-A"] = Guid.NewGuid(), ["DOCK-1"] = Guid.NewGuid() }));

        var result = await handler.Handle(new SubmitDeliveryOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static (SubmitDeliveryOrderCommandHandler handler,
        IDeliveryOrderRepository repo,
        IStationValidationService stations,
        IDispatchStrategyRegistry registry) Build(bool manualRegistered)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var stations = Substitute.For<IStationValidationService>();
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(TransportMode.Amr).Returns(true);
        registry.IsRegistered(TransportMode.Manual).Returns(manualRegistered);

        var handler = new SubmitDeliveryOrderCommandHandler(
            repo, Substitute.For<IOrderAuditEventRepository>(), stations, registry,
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<SubmitDeliveryOrderCommandHandler>.Instance);
        return (handler, repo, stations, registry);
    }

    private static DomainOrder DraftOrder(TransportMode mode)
    {
        var order = DomainOrder.Create(
            "GATE-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null, requestedTransportMode: mode);
        order.AddItem(
            "WH-A", "DOCK-1", 1, "SKU-1",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 1.0, quantity: Quantity.Create(1, UnitOfMeasure.EA));
        return order;
    }
}
