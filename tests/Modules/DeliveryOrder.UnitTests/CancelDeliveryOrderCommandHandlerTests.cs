using DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Cancelling an order has to take its items with it. Items bound to a trip go
// the long way round — the cascade cancels the trip, TripCancelledConsumer
// releases and sweeps them — but unbound items have no trip to follow, and the
// order will never dispatch again, so the handler has to sweep those itself.
//
// That gap only shows on a pre-dispatch cancel, which is exactly the window the
// source-system order-cancel endpoint exists to reach.
public class CancelDeliveryOrderCommandHandlerTests
{
    [Fact]
    public async Task PreDispatchCancel_CancelsUnboundItems()
    {
        // Confirmed, never dispatched: no trip exists, so nothing else in the
        // system will ever touch these items again.
        var order = ConfirmedOrder();
        var (handler, repo) = HandlerFor(order);

        var result = await handler.Handle(new CancelDeliveryOrderCommand(order.Id, "ops"), default);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.Items.Should().OnlyContain(i => i.Status == ItemStatus.Cancelled);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripBoundItems_AreLeftForTheTripCascade()
    {
        // The trip is still carrying these. Cancelling them here would race the
        // cascade and strip the trip of the items it is about to release.
        var order = ConfirmedOrder();
        var bound = order.Items.First();
        order.AssignItemsToTrip(
            Guid.NewGuid(), 1, bound.PickupStationId!.Value, bound.DropStationId!.Value);
        var (handler, _) = HandlerFor(order);

        await handler.Handle(new CancelDeliveryOrderCommand(order.Id, "ops"), default);

        order.Items.Should().OnlyContain(i => i.Status != ItemStatus.Cancelled,
            "every item on this order is bound to the trip");
    }

    [Fact]
    public async Task TerminalOrder_Fails()
    {
        var order = ConfirmedOrder();
        var tripId = Guid.NewGuid();
        var first = order.Items.First();
        order.AssignItemsToTrip(tripId, 1, first.PickupStationId!.Value, first.DropStationId!.Value);
        order.MarkTripItemsDelivered(tripId);
        order.RecomputeStatusFromItems();
        order.Status.Should().Be(OrderStatus.Completed);
        var (handler, repo) = HandlerFor(order);

        var result = await handler.Handle(new CancelDeliveryOrderCommand(order.Id, "too late"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Completed");
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (CancelDeliveryOrderCommandHandler handler, IDeliveryOrderRepository repo)
        HandlerFor(DomainOrder order)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return (new CancelDeliveryOrderCommandHandler(
            repo, NullLogger<CancelDeliveryOrderCommandHandler>.Instance), repo);
    }

    private static DomainOrder ConfirmedOrder()
    {
        var order = DomainOrder.Create(
            "CAN-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, 5.0, Quantity.Create(1, "EA"));
        order.AddItem("WH-A", "DOCK-1", 2, "SKU-2", null, null, null, 5.0, Quantity.Create(1, "EA"));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5.0);
        return order;
    }
}
