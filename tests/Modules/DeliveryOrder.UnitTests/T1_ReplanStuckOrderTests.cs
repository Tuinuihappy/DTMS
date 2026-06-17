using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// T1.7 — ReplanStuckOrderCommand is the single replay implementation shared
// by the admin /replan endpoint and the T1.4 watchdog. Failing safely is more
// important than recovering aggressively: every guard exists because an
// over-eager replay could double-dispatch a vendor order, orphan items, or
// publish a malformed integration event.
public class T1_ReplanStuckOrderTests
{
    private const double WeightFallbackKg = 5.0;
    private static readonly Guid PickupStationId = Guid.NewGuid();
    private static readonly Guid DropStationId = Guid.NewGuid();

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsFailure()
    {
        var (handler, _, _, _, publisher) = HandlerWith(order: null, trips: new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(Guid.NewGuid(), "ops", "manual replay"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
        await publisher.DidNotReceiveWithAnyArgs()
            .Publish<DeliveryOrderConfirmedIntegrationEventV1>(default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_EmptyTriggeredBy_ReturnsFailure(string? triggeredBy)
    {
        var order = BuildPlannedOrder();
        var (handler, _, _, _, _) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, triggeredBy!, "reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("TriggeredBy");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_EmptyReason_ReturnsFailure(string? reason)
    {
        var order = BuildPlannedOrder();
        var (handler, _, _, _, _) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, "ops", reason!),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Reason");
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Validated)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public async Task Handle_NonReplayableStatus_ReturnsFailure(OrderStatus status)
    {
        var order = BuildOrderAtStatus(status);
        var (handler, _, _, _, publisher) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, "ops", "reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("only Confirmed/Planning/Planned/Dispatched");
        await publisher.DidNotReceiveWithAnyArgs()
            .Publish<DeliveryOrderConfirmedIntegrationEventV1>(default!);
    }

    [Fact]
    public async Task Handle_RequireStuckPlannedFlagButOrderIsConfirmed_Skips()
    {
        // Watchdog path: a different pod may have advanced this order between
        // scan and command-send. RequireStuckPlanned=true tells the handler
        // to give up gracefully rather than double-fire.
        var order = BuildConfirmedOrder();
        var (handler, _, _, _, publisher) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, "watchdog", "auto", RequireStuckPlanned: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no longer stuck-Planned");
        await publisher.DidNotReceiveWithAnyArgs()
            .Publish<DeliveryOrderConfirmedIntegrationEventV1>(default!);
    }

    [Fact]
    public async Task Handle_ActiveTrip_ReturnsFailure()
    {
        // Replan with a Trip already in flight would queue a duplicate vendor
        // order — operators should /trips/{id}/retry on the specific Trip
        // instead.
        var order = BuildPlannedOrder();
        var activeTrip = Trip.CreateForEnvelope(order.Id, "UK-1", "VK-1");
        activeTrip.MarkVendorStarted();
        var (handler, _, _, _, publisher) = HandlerWith(order, new List<Trip> { activeTrip });

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, "ops", "manual"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("still active");
        await publisher.DidNotReceiveWithAnyArgs()
            .Publish<DeliveryOrderConfirmedIntegrationEventV1>(default!);
    }

    [Fact]
    public async Task Handle_ValidPlannedOrder_PublishesEventAndAudits()
    {
        // Happy path. The published event must carry the order's items with
        // resolved station IDs so the Planning consumer can group + dispatch
        // exactly as it did the first time.
        var order = BuildPlannedOrder();
        var (handler, orderRepo, auditRepo, _, publisher) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new ReplanStuckOrderCommand(order.Id, "PlanningWatchdog", "auto-replay"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(order.Id);
        result.Value.PreviousStatus.Should().Be("Planned");
        result.Value.ItemCount.Should().Be(2);

        await publisher.Received(1).Publish(
            Arg.Is<DeliveryOrderConfirmedIntegrationEventV1>(e =>
                e.DeliveryOrderId == order.Id
                && e.Items.Count == 2
                && e.Items.All(i => i.PickupStationId == PickupStationId && i.DropStationId == DropStationId)),
            Arg.Any<CancellationToken>());

        await auditRepo.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(a =>
                a.EventType == "OrderReplanned"
                && a.Details!.Contains("PlanningWatchdog")
                && a.Details.Contains("Planned")),
            Arg.Any<CancellationToken>());

        await orderRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static (
        ReplanStuckOrderCommandHandler handler,
        IDeliveryOrderRepository orderRepo,
        IOrderAuditEventRepository auditRepo,
        ITripRepository tripRepo,
        IPublishEndpoint publisher)
        HandlerWith(DomainOrder? order, List<Trip> trips)
    {
        var orderRepo = Substitute.For<IDeliveryOrderRepository>();
        if (order is not null)
            orderRepo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var auditRepo = Substitute.For<IOrderAuditEventRepository>();

        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(trips);

        var publisher = Substitute.For<IPublishEndpoint>();
        var options = Options.Create(new DeliveryOrderOptions { WeightFallbackKg = WeightFallbackKg });

        var handler = new ReplanStuckOrderCommandHandler(
            orderRepo, auditRepo, tripRepo, publisher, options,
            NullLogger<ReplanStuckOrderCommandHandler>.Instance);

        return (handler, orderRepo, auditRepo, tripRepo, publisher);
    }

    private static DomainOrder BuildOrderWithItems()
    {
        var order = DomainOrder.Create(
            "T1-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, WeightFallbackKg,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.AddItem("WH-A", "DOCK-1", 2, "SKU-2", null, null, null, WeightFallbackKg,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = PickupStationId,
            ["DOCK-1"] = DropStationId,
        });
        return order;
    }

    private static DomainOrder BuildConfirmedOrder()
    {
        var order = BuildOrderWithItems();
        order.Confirm(WeightFallbackKg);
        return order;
    }

    private static DomainOrder BuildPlannedOrder()
    {
        var order = BuildConfirmedOrder();
        order.MarkPlanning();
        order.MarkPlanned();
        return order;
    }

    private static DomainOrder BuildOrderAtStatus(OrderStatus status) => status switch
    {
        OrderStatus.Draft     => DomainOrder.Create(
            "D-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null),
        OrderStatus.Submitted => BuildAndAdvance(stopBeforeValidate: true),
        OrderStatus.Validated => BuildOrderWithItems(),
        OrderStatus.Cancelled => CancelOf(BuildConfirmedOrder()),
        OrderStatus.Rejected  => RejectOf(BuildOrderWithItems()),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "test helper does not cover this status")
    };

    private static DomainOrder BuildAndAdvance(bool stopBeforeValidate)
    {
        var sub = DomainOrder.Create(
            "S-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null);
        sub.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null,
            WeightFallbackKg, Quantity.Create(1, UnitOfMeasure.EA));
        sub.Submit();
        return sub;
    }

    private static DomainOrder CancelOf(DomainOrder o) { o.Cancel("test"); return o; }
    private static DomainOrder RejectOf(DomainOrder o) { o.Reject("test"); return o; }
}
