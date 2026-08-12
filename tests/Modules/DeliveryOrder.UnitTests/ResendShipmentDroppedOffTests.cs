using DTMS.DeliveryOrder.Application.Commands.ResendShipmentDroppedOff;
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

// 2026-08 — the droppedoff resend (renamed from arrived). Pins the happy path
// (subscription-routed dispatch + ArrivedManuallyResent audit — the Arrived*
// audit family serves the new wire name), the off-switch, its gates
// (self-managed refused, never-dropped refused, locationCode required), and
// the supersede of pending fan-out rows.
public class ResendShipmentDroppedOffTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder SourceOrder(
        Guid tripId, out Guid orderId, string source = "oms",
        bool selfManaged = false, bool bindItems = true)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-RD-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: source, sourceSystemDisplayName: source.ToUpperInvariant(),
            requestedBy: selfManaged ? "wms-operator-7" : null,
            requestedTransportMode: selfManaged ? TransportMode.Manual : TransportMode.Amr,
            selfManaged: selfManaged);
        order.AddItem("WH-A", "STF_09", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["STF_09"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindItems)
            order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    private static Trip DroppedTrip(Guid orderId, DateTime? actedAt = null, string? dropCode = null)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G4", "ORD-4", Pickup, Drop,
            dropLocationCode: dropCode);
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        trip.MarkVendorPickedUp();
        trip.MarkVendorDropCompleted(actedAt: actedAt);
        return trip;
    }

    // 2026-08 — the original bug this feature fixes: a cancel unbinds the
    // items, but the trip's own frozen code keeps the resend working.
    [Fact]
    public async Task Resend_AfterCancelUnbind_UsesTripCode_Succeeds()
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, bindItems: false);   // post-cancel shape
        var trip = DroppedTrip(orderId,
            actedAt: new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc), dropCode: "STF_09");

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
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentDroppedOffV1, Arg.Any<CancellationToken>())
            .Returns(new List<EventSubscriber> { new("oms", "oms.shipment.droppedoff.v1") });
        var handler = new ResendShipmentDroppedOffCommandHandler(
            resolver, Substitute.For<ISourceCallbackDispatcher>(), lookup, trips, orders,
            Substitute.For<IOrderAuditEventRepository>(), Substitute.For<IOrderActivityProjectionStore>(),
            Substitute.For<ISourceCallbackOutboxSuperseder>(),
            NullLogger<ResendShipmentDroppedOffCommandHandler>.Instance);

        var result = await handler.Handle(
            new ResendShipmentDroppedOffCommand(orderId, tripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocationCode.Should().Be("STF_09");
        await formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentDroppedOffContext>(c => c.LocationCode == "STF_09"),
            Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        ResendShipmentDroppedOffCommandHandler Handler,
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
        bool selfManaged = false, bool bindItems = true, bool dropped = true)
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, orderSource, selfManaged, bindItems);

        Trip trip;
        if (dropped)
        {
            trip = DroppedTrip(orderId, new DateTime(2026, 8, 1, 16, 42, 11, 208, DateTimeKind.Utc));
        }
        else
        {
            trip = Trip.CreateForEnvelope(orderId, "upper-G4", "ORD-4", Pickup, Drop);
            trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
            trip.MarkVendorPickedUp();
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
                RelativePath: "/integrations/tms/shipments/x/dropoff-arrived"));
        var resolver = Substitute.For<ICallbackFormatterResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(formatter);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentDroppedOffV1, Arg.Any<CancellationToken>())
            .Returns(subscribedSystem is null
                ? new List<EventSubscriber>()
                : new List<EventSubscriber> { new(subscribedSystem, $"{subscribedSystem}.shipment.droppedoff.v1") });

        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var superseder = Substitute.For<ISourceCallbackOutboxSuperseder>();

        var handler = new ResendShipmentDroppedOffCommandHandler(
            resolver, dispatcher, lookup, trips, orders, audit, activity, superseder,
            NullLogger<ResendShipmentDroppedOffCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, superseder, formatter,
            order, trip, orderId, tripId);
    }

    [Fact]
    public async Task Resend_Success_DispatchesToSource_AndWritesArrivedManuallyResentAudit()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocationCode.Should().Be("STF_09");
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.ArrivedManuallyResent && e.SystemKey == "oms"),
            Arg.Any<CancellationToken>());
    }

    // The context handed to the formatter must carry the upstream's own drop
    // code and the trip's actual drop time — not the click time.
    [Fact]
    public async Task Resend_PassesDropCodeAndVendorDroppedAt_ToFormatter()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentDroppedOffContext>(c =>
                c.OrderRef == h.Order.OrderRef &&
                c.LocationCode == "STF_09" &&
                c.OccurredAt == h.Trip.VendorDroppedAt!.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_SelfManagedOrder_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(selfManaged: true);

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Self-managed");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_TripNeverDropped_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(dropped: false);

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not reported drop-off");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_NoBoundItems_ReturnsFailure()
    {
        var h = NewHarness(bindItems: false);

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locationCode is required");
    }

    [Fact]
    public async Task Resend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribedSystem: null);

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

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
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Superseder.Received(1).SupersedePendingAsync(
            "oms", CallbackEventTypes.ShipmentDroppedOffV1, h.OrderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_DispatchFails_DoesNotSupersede()
    {
        var h = NewHarness();
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));

        var result = await h.Handler.Handle(
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

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
            new ResendShipmentDroppedOffCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
