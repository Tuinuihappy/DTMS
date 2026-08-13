using DTMS.DeliveryOrder.Application.Commands.ResendShipmentPickedUp;
using DTMS.DeliveryOrder.Application.Consumers;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Iam.Application.Callbacks;
using System.Net;
using System.Net.Http;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// 2026-08 — the pickedup resend, source-agnostic like its siblings. Pins the
// happy path (subscription-routed dispatch + PickedUpManuallyResent audit +
// the ShipmentPickedUpContext fields), the off-switch, and its own gates:
// self-managed orders and never-picked-up trips are refused.
public class ResendShipmentPickedUpTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder SourceOrder(
        Guid tripId, out Guid orderId, string source = "oms",
        bool selfManaged = false, bool bindItems = true)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-RP-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: source, sourceSystemDisplayName: source.ToUpperInvariant(),
            requestedBy: selfManaged ? "wms-operator-7" : null,
            requestedTransportMode: selfManaged ? TransportMode.Manual : TransportMode.Amr,
            selfManaged: selfManaged);
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindItems)
            order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    // Default carries a code like every real trip born after 2026-08.
    private static Trip PickedUpTrip(Guid orderId, DateTime? actedAt = null, string? pickupCode = "WH-A")
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G3", "ORD-3", Pickup, Drop,
            pickupLocationCode: pickupCode);
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        trip.MarkVendorPickedUp(actedAt: actedAt);
        return trip;
    }

    // 2026-08 — the original bug this feature fixes: a cancel unbinds the
    // items, but the trip's own frozen code keeps the resend working.
    [Fact]
    public async Task Resend_AfterCancelUnbind_UsesTripCode_Succeeds()
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, bindItems: false);   // post-cancel shape
        var trip = PickedUpTrip(orderId,
            actedAt: new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc), pickupCode: "SHELF1");

        var orders = Substitute.For<IDeliveryOrderRepository>();
        orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        var trips = Substitute.For<ITripRepository>();
        trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        trips.GetRootTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(tripId);
        var formatter = Substitute.For<ICallbackPayloadFormatter>();
        formatter.FormatAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new CallbackPayload("application/json",
                System.Text.Encoding.UTF8.GetBytes("{}"), RelativePath: "/x"));
        var resolver = Substitute.For<ICallbackFormatterResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(formatter);
        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentPickedUpV1, Arg.Any<CancellationToken>())
            .Returns(new List<EventSubscriber> { new("oms", "oms.shipment.pickedup.v1") });
        var handler = new ResendShipmentPickedUpCommandHandler(
            resolver, Substitute.For<ISourceCallbackDispatcher>(), lookup, trips, orders,
            Substitute.For<IOrderAuditEventRepository>(), Substitute.For<IOrderActivityProjectionStore>(),
            Substitute.For<ISourceCallbackOutboxSuperseder>(),
            NullLogger<ResendShipmentPickedUpCommandHandler>.Instance);

        var result = await handler.Handle(
            new ResendShipmentPickedUpCommand(orderId, tripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocationCode.Should().Be("SHELF1");
        await formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentPickedUpContext>(c => c.LocationCode == "SHELF1"),
            Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        ResendShipmentPickedUpCommandHandler Handler,
        ISourceCallbackDispatcher Dispatcher,
        IOrderAuditEventRepository Audit,
        ISourceCallbackOutboxSuperseder Superseder,
        ICallbackPayloadFormatter Formatter,
        DomainOrder Order,
        Trip Trip,
        Guid OrderId,
        Guid TripId);

    private static Harness NewHarness(
        string orderSource = "oms", string? subscribedSystem = "oms",
        bool selfManaged = false, bool bindItems = true, bool pickedUp = true,
        string? tripPickupCode = "WH-A")
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, orderSource, selfManaged, bindItems);

        Trip trip;
        if (pickedUp)
        {
            trip = PickedUpTrip(orderId, new DateTime(2026, 8, 1, 9, 24, 3, 512, DateTimeKind.Utc),
                pickupCode: tripPickupCode);
        }
        else
        {
            trip = Trip.CreateForEnvelope(orderId, "upper-G3", "ORD-3", Pickup, Drop);
            trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        }

        var orders = Substitute.For<IDeliveryOrderRepository>();
        orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);
        var trips = Substitute.For<ITripRepository>();
        trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        trips.GetRootTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(tripId);

        var formatter = Substitute.For<ICallbackPayloadFormatter>();
        formatter.FormatAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new CallbackPayload("application/json",
                System.Text.Encoding.UTF8.GetBytes("{}"),
                RelativePath: "/integrations/tms/shipments/x/pickup-arrived"));
        var resolver = Substitute.For<ICallbackFormatterResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(formatter);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentPickedUpV1, Arg.Any<CancellationToken>())
            .Returns(subscribedSystem is null
                ? new List<EventSubscriber>()
                : new List<EventSubscriber> { new(subscribedSystem, $"{subscribedSystem}.shipment.pickedup.v1") });

        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var superseder = Substitute.For<ISourceCallbackOutboxSuperseder>();

        var handler = new ResendShipmentPickedUpCommandHandler(
            resolver, dispatcher, lookup, trips, orders, audit, activity, superseder,
            NullLogger<ResendShipmentPickedUpCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, superseder, formatter,
            order, trip, orderId, tripId);
    }

    [Fact]
    public async Task Resend_Success_DispatchesToSource_AndWritesPickedUpManuallyResentAudit()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocationCode.Should().Be("WH-A");
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.PickedUpManuallyResent && e.SystemKey == "oms"),
            Arg.Any<CancellationToken>());
    }

    // The context handed to the formatter must carry the upstream's own
    // pickup code and the trip's actual pickup time — not the click time.
    [Fact]
    public async Task Resend_PassesLocationCodeAndVendorPickedUpAt_ToFormatter()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentPickedUpContext>(c =>
                c.OrderRef == h.Order.OrderRef &&
                c.LocationCode == "WH-A" &&
                c.OccurredAt == h.Trip.VendorPickedUpAt!.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_SelfManagedOrder_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(selfManaged: true);

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Self-managed");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_TripNeverPickedUp_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(pickedUp: false);

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not reported pickup");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // Reads from the Trip alone (item-scan fallback retired 2026-08-13) — a
    // code-less trip refuses even with items still bound.
    [Fact]
    public async Task Resend_NoTripCode_ReturnsFailure()
    {
        var h = NewHarness(tripPickupCode: null);

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locationCode is required");
    }

    [Fact]
    public async Task Resend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribedSystem: null);

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_Success_SupersedesPendingOutboxRows_ForThisOrderAndSystem()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Superseder.Received(1).SupersedePendingAsync(
            "oms", CallbackEventTypes.ShipmentPickedUpV1, h.OrderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_DispatchFails_DoesNotSupersede()
    {
        var h = NewHarness();
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await h.Superseder.DidNotReceive().SupersedePendingAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_SupersedeFails_AfterDelivery_StillReturnsSuccess()
    {
        var h = NewHarness();
        h.Superseder.SupersedePendingAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("outbox db hiccup"));

        var result = await h.Handler.Handle(
            new ResendShipmentPickedUpCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
