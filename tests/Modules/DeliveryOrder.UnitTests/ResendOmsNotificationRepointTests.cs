using DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using System.Net;
using System.Net.Http;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase 4 — the started resend was repointed off the deleted legacy
// IOmsShipmentClient onto the federated ISourceCallbackDispatcher (sync). This
// pins that: a successful resend dispatches to the "oms" partition and writes
// the UpstreamOmsManuallyResent audit (the UI's "resent" signal).
//
// F1 — Phase 4 dropped the old UpstreamOms__Enabled gate without replacing it,
// so a manual resend used to punch through the subscription off-switch that the
// auto fan-out honours. These tests pin both sides of that gate.
public class ResendOmsNotificationRepointTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder OmsOrder(Guid tripId, out Guid orderId)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-R-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS");
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    private static ISubscriptionLookup NewLookup(bool subscribed)
    {
        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentStartedV1, Arg.Any<CancellationToken>())
            .Returns(subscribed
                ? new List<EventSubscriber> { new(WellKnownSourceSystems.Oms, "oms.shipment.started.v1") }
                : new List<EventSubscriber>());
        return lookup;
    }

    private sealed record Harness(
        ResendOmsNotificationCommandHandler Handler,
        ISourceCallbackDispatcher Dispatcher,
        IOrderAuditEventRepository Audit,
        Guid OrderId,
        Guid TripId);

    private static Harness NewHarness(bool subscribed)
    {
        var tripId = Guid.NewGuid();
        var order = OmsOrder(tripId, out var orderId);

        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");

        var orders = Substitute.For<IDeliveryOrderRepository>();
        orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        var trips = Substitute.For<ITripRepository>();
        trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        trips.GetRootTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(tripId);

        var formatter = Substitute.For<ICallbackPayloadFormatter>();
        formatter.FormatAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new CallbackPayload("application/json",
                System.Text.Encoding.UTF8.GetBytes("{\"shipmentId\":\"x\"}"),
                RelativePath: "/api/shipments"));
        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();

        var handler = new ResendOmsNotificationCommandHandler(
            formatter, dispatcher, NewLookup(subscribed), trips, orders, audit, activity,
            NullLogger<ResendOmsNotificationCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, orderId, tripId);
    }

    [Fact]
    public async Task Resend_Success_DispatchesToOms_AndWritesManuallyResentAudit()
    {
        var h = NewHarness(subscribed: true);

        var result = await h.Handler.Handle(
            new ResendOmsNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            WellKnownSourceSystems.Oms, Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e => e.EventType == "UpstreamOmsManuallyResent"),
            Arg.Any<CancellationToken>());
    }

    // The off-switch must close every path: with the oms subscription disabled
    // (or absent) the lookup returns empty, so the resend must refuse rather
    // than POST to OMS behind the operator's back.
    [Fact]
    public async Task Resend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribed: false);

        var result = await h.Handler.Handle(
            new ResendOmsNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.DidNotReceive().AddAsync(
            Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    // F4 — failure mapping. 4xx = OMS actively rejected (data problem at the
    // source; retrying without fixing it is pointless) vs 5xx/transport = OMS
    // unavailable (retrying is reasonable). The operator-facing message must
    // steer accordingly, and neither may write a ManuallyResent audit row.
    [Fact]
    public async Task Resend_OmsRejects4xx_SaysRejected_AndWritesNoAudit()
    {
        var h = NewHarness(subscribed: true);
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("lot not found", null, HttpStatusCode.NotFound));

        var result = await h.Handler.Handle(
            new ResendOmsNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("rejected");
        await h.Audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_OmsUnavailable5xx_SaysRequestFailed_AndWritesNoAudit()
    {
        var h = NewHarness(subscribed: true);
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));

        var result = await h.Handler.Handle(
            new ResendOmsNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("request failed");
        await h.Audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    // F2 — once DispatchAsync returns, OMS HAS the callback. An audit/activity
    // persistence failure after that point must not be reported as a resend
    // failure (the operator would re-click in confusion); it is logged instead.
    [Fact]
    public async Task Resend_AuditWriteFails_AfterDelivery_StillReturnsSuccess()
    {
        var h = NewHarness(subscribed: true);
        h.Audit.AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db hiccup"));

        var result = await h.Handler.Handle(
            new ResendOmsNotificationCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("OMS already received the callback — persistence trouble must not masquerade as a send failure");
        await h.Dispatcher.Received(1).DispatchAsync(
            WellKnownSourceSystems.Oms, Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }
}
