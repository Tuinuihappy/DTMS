using DTMS.DeliveryOrder.Application.Commands.ResendShipmentArrived;
using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase C — the arrived resend, source-agnostic like the started one. Pins
// the happy path (subscription-routed dispatch + ArrivedManuallyResent audit
// with SystemKey), the off-switch, and its own extra gate: manual transport
// never reports arrival upstream.
public class ResendShipmentArrivedTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder SourceOrder(
        Guid tripId, out Guid orderId, string source = "oms", TransportMode? transportMode = null)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-RA-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: source, sourceSystemDisplayName: source.ToUpperInvariant(),
            requestedTransportMode: transportMode);
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    private sealed record Harness(
        ResendShipmentArrivedCommandHandler Handler,
        ISourceCallbackDispatcher Dispatcher,
        IOrderAuditEventRepository Audit,
        Guid OrderId,
        Guid TripId);

    private static Harness NewHarness(
        string orderSource = "oms", string? subscribedSystem = "oms", TransportMode? transportMode = null)
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, orderSource, transportMode);

        var trip = Trip.CreateForEnvelope(orderId, "upper-G2", "ORD-2", Pickup, Drop);

        var orders = Substitute.For<IDeliveryOrderRepository>();
        orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        var trips = Substitute.For<ITripRepository>();
        trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        trips.GetRootTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(tripId);

        var formatter = Substitute.For<ICallbackPayloadFormatter>();
        formatter.FormatAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new CallbackPayload("application/json",
                System.Text.Encoding.UTF8.GetBytes("{\"lots\":[]}"),
                RelativePath: "/api/shipments/x/arrived"));
        var resolver = Substitute.For<ICallbackFormatterResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(formatter);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentArrivedV1, Arg.Any<CancellationToken>())
            .Returns(subscribedSystem is null
                ? new List<EventSubscriber>()
                : new List<EventSubscriber> { new(subscribedSystem, $"{subscribedSystem}.shipment.arrived.v1") });

        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();

        var handler = new ResendShipmentArrivedCommandHandler(
            resolver, dispatcher, lookup, trips, orders, audit, activity,
            NullLogger<ResendShipmentArrivedCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, orderId, tripId);
    }

    [Fact]
    public async Task ArrivedResend_Success_DispatchesToSource_AndWritesArrivedManuallyResentAudit()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentArrivedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.ArrivedManuallyResent && e.SystemKey == "oms"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArrivedResend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribedSystem: null);

        var result = await h.Handler.Handle(
            new ResendShipmentArrivedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // Manual transport never reports arrival upstream (the source system owns
    // the arrival signal for operator-pool deliveries) — refuse, don't send.
    [Fact]
    public async Task ArrivedResend_ManualTransport_IsRefused()
    {
        var h = NewHarness(transportMode: TransportMode.Manual);

        var result = await h.Handler.Handle(
            new ResendShipmentArrivedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Manual transport");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }
}
