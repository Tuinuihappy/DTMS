using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AbandonStuckDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase b11 — Order cascade & operator abandon. The cascade lives in
// TripCancelledConsumer; the operator escape hatch in
// AbandonStuckDeliveryOrderCommandHandler. Both share the same
// "in-flight order + zero active sibling trips" precondition.
public class B11CascadeAndAbandonTests
{
    // ── TripCancelledConsumer cascade ────────────────────────────────────

    [Fact]
    public async Task TripCancelled_LastActiveTrip_CascadesOrderToCancelled()
    {
        var order = BuildOrder();
        var tripId = Guid.NewGuid();
        order.MarkDispatched();

        // Build a Trip in Cancelled status for the trip currently being processed.
        var cancelledTrip = Trip.CreateForEnvelope(order.Id, "UK-1", "VK-1");
        SetTripId(cancelledTrip, tripId);
        cancelledTrip.MarkVendorStarted();
        cancelledTrip.Cancel("vendor rejected");

        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Trip> { cancelledTrip });

        var orderRepo = Substitute.For<IDeliveryOrderRepository>();
        orderRepo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var consumer = new TripCancelledConsumer(orderRepo, tripRepo, NullLogger<TripCancelledConsumer>.Instance);
        var ctx = ContextFor(new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, Guid.NewGuid(), order.Id,
            "vendor rejected", "UK-1"));

        await consumer.Consume(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled, "no active siblings → cascade fires");
    }

    [Fact]
    public async Task TripCancelled_StillHasActiveSibling_DoesNotCascade()
    {
        var order = BuildOrder();
        var cancelledTripId = Guid.NewGuid();
        order.MarkDispatched();

        // The trip currently being cancelled
        var cancelledTrip = Trip.CreateForEnvelope(order.Id, "UK-1", "VK-1");
        SetTripId(cancelledTrip, cancelledTripId);
        cancelledTrip.MarkVendorStarted();
        cancelledTrip.Cancel("operator");

        // An active sibling — should block cascade
        var activeSibling = Trip.CreateForEnvelope(order.Id, "UK-2", "VK-2");
        activeSibling.MarkVendorStarted();   // Status InProgress

        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Trip> { cancelledTrip, activeSibling });

        var orderRepo = Substitute.For<IDeliveryOrderRepository>();
        orderRepo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var consumer = new TripCancelledConsumer(orderRepo, tripRepo, NullLogger<TripCancelledConsumer>.Instance);
        var ctx = ContextFor(new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, cancelledTripId, Guid.NewGuid(), order.Id,
            "operator", "UK-1"));

        await consumer.Consume(ctx);

        order.Status.Should().Be(OrderStatus.Dispatched, "active sibling still in flight → keep order");
    }

    [Fact]
    public async Task TripCancelled_OrderAlreadyCancelled_NoOpOnCascade()
    {
        var order = BuildOrder();
        order.Cancel("admin override");
        var tripId = Guid.NewGuid();

        // No need to mock trip list — the cascade branch is gated on order status
        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Trip>());

        var orderRepo = Substitute.For<IDeliveryOrderRepository>();
        orderRepo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var consumer = new TripCancelledConsumer(orderRepo, tripRepo, NullLogger<TripCancelledConsumer>.Instance);
        var ctx = ContextFor(new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, Guid.NewGuid(), order.Id,
            "vendor", "UK-X"));

        await consumer.Consume(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        // Repo MUST NOT be polled for trips when order is already terminal —
        // skipping the query is a perf optimization that matters for orders
        // that fan out to many trips.
        await tripRepo.DidNotReceive().GetByDeliveryOrderIdAsync(order.Id, Arg.Any<CancellationToken>());
    }

    // ── AbandonStuckDeliveryOrderCommandHandler ───────────────────────────

    [Fact]
    public async Task Abandon_RejectsWhenOrderNotInFlight()
    {
        var order = BuildOrder();
        order.Cancel("already terminal");
        var (handler, _, _) = HandlerWith(order, new List<Trip>());

        var result = await handler.Handle(
            new AbandonStuckDeliveryOrderCommand(order.Id, "ops-01", "cleanup"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task Abandon_RejectsWhenActiveTripsRemain()
    {
        var order = BuildOrder();
        order.MarkDispatched();
        var activeTrip = Trip.CreateForEnvelope(order.Id, "UK-Z", "VK-Z");
        activeTrip.MarkVendorStarted();

        var (handler, _, _) = HandlerWith(order, new List<Trip> { activeTrip });

        var result = await handler.Handle(
            new AbandonStuckDeliveryOrderCommand(order.Id, "ops-01", "cleanup"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("active trip");
    }

    [Fact]
    public async Task Abandon_HappyPath_CancelsOrderAndItems()
    {
        var order = BuildOrder();
        order.MarkDispatched();
        var deadTrip = Trip.CreateForEnvelope(order.Id, "UK-D", "VK-D");
        deadTrip.MarkVendorStarted();
        deadTrip.Cancel("dead");

        var (handler, orderRepo, auditRepo) = HandlerWith(order, new List<Trip> { deadTrip });

        var result = await handler.Handle(
            new AbandonStuckDeliveryOrderCommand(order.Id, "ops-01", "cleanup b11"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        // Unbound items must follow the order to terminal — otherwise the
        // stranded-items bug from the roadmap recurs.
        order.Items.Should().AllSatisfy(i =>
            i.Status.Should().BeOneOf(ItemStatus.Cancelled, ItemStatus.Delivered, ItemStatus.Failed, ItemStatus.Returned));
        await orderRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditRepo.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == "OrderAbandoned"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abandon_RejectsEmptyActor()
    {
        var (handler, _, _) = HandlerWith(BuildOrder(), new List<Trip>());

        var result = await handler.Handle(
            new AbandonStuckDeliveryOrderCommand(Guid.NewGuid(), "", "reason"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("AbandonedBy");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static DomainOrder BuildOrder()
    {
        var order = DomainOrder.Create(
            "B11-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.AddItem("WH-A", "DOCK-1", 2, "SKU-2", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5.0);
        return order;
    }

    private static (AbandonStuckDeliveryOrderCommandHandler handler,
                    IDeliveryOrderRepository orderRepo,
                    IOrderAuditEventRepository auditRepo)
        HandlerWith(DomainOrder order, List<Trip> trips)
    {
        var orderRepo = Substitute.For<IDeliveryOrderRepository>();
        orderRepo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var tripRepo = Substitute.For<ITripRepository>();
        tripRepo.GetByDeliveryOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(trips);
        var auditRepo = Substitute.For<IOrderAuditEventRepository>();
        var handler = new AbandonStuckDeliveryOrderCommandHandler(
            orderRepo, tripRepo, auditRepo,
            NullLogger<AbandonStuckDeliveryOrderCommandHandler>.Instance);
        return (handler, orderRepo, auditRepo);
    }

    private static ConsumeContext<T> ContextFor<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    // Trip.Id is set inside CreateForEnvelope to Guid.NewGuid(). When a test
    // wires a specific TripId into both the consumer message and the trip
    // returned from the repo, we need to override it. The Id setter is
    // private — reflection is the least-bad path for unit tests.
    private static void SetTripId(Trip trip, Guid id)
    {
        typeof(DTMS.SharedKernel.Domain.AggregateRoot<Guid>)
            .GetProperty(nameof(Trip.Id))!
            .SetValue(trip, id);
    }
}
