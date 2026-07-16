using DTMS.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain;
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

// F4 — the arrived resend handler had no unit test at all. Pins the same
// contract as the started handler: subscription-gated, dispatches sync to the
// oms partition, writes the UpstreamOmsArrivedManuallyResent audit — plus its
// own extra gate: manual transport never reports arrival to OMS.
public class ResendOmsArrivedNotificationTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder OmsOrder(Guid tripId, out Guid orderId, TransportMode? transportMode = null)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-RA-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS",
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
        ResendOmsArrivedNotificationCommandHandler Handler,
        ISourceCallbackDispatcher Dispatcher,
        IOrderAuditEventRepository Audit,
        Guid OrderId,
        Guid TripId);

    private static Harness NewHarness(bool subscribed, TransportMode? transportMode = null)
    {
        var tripId = Guid.NewGuid();
        var order = OmsOrder(tripId, out var orderId, transportMode);

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

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentArrivedV1, Arg.Any<CancellationToken>())
            .Returns(subscribed
                ? new List<EventSubscriber> { new(WellKnownSourceSystems.Oms, "oms.shipment.arrived.v1") }
                : new List<EventSubscriber>());

        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();

        var handler = new ResendOmsArrivedNotificationCommandHandler(
            formatter, dispatcher, lookup, trips, orders, audit, activity,
            NullLogger<ResendOmsArrivedNotificationCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, orderId, tripId);
    }

    [Fact]
    public async Task ArrivedResend_Success_DispatchesToOms_AndWritesArrivedManuallyResentAudit()
    {
        var h = NewHarness(subscribed: true);

        var result = await h.Handler.Handle(
            new ResendOmsArrivedNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            WellKnownSourceSystems.Oms, Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == "UpstreamOmsArrivedManuallyResent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArrivedResend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribed: false);

        var result = await h.Handler.Handle(
            new ResendOmsArrivedNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // Manual transport never reports arrival to OMS (OMS owns the arrival
    // signal for operator-pool deliveries) — the resend must refuse, not send.
    [Fact]
    public async Task ArrivedResend_ManualTransport_IsRefused()
    {
        var h = NewHarness(subscribed: true, transportMode: TransportMode.Manual);

        var result = await h.Handler.Handle(
            new ResendOmsArrivedNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Manual transport");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }
}
