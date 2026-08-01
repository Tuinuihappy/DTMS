using System.Net;
using System.Net.Http;
using DTMS.DeliveryOrder.Application.Commands.ResendShipmentStarted;
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
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase C — the started resend is source-AGNOSTIC: the target system comes
// from the order, the formatter + partition come from that system's
// subscription row, and the audit labels are the system-neutral
// UpstreamCallbackAudit vocabulary with SystemKey carried separately. These
// pin: the sync dispatch contract, the subscription off-switch (F1), the
// 4xx/5xx operator-message mapping (F4), delivered-but-audit-failed=Success
// (F2), and — the point of the phase — that a SAP order dispatches to SAP.
public class ResendShipmentStartedTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private static DomainOrder SourceOrder(Guid tripId, out Guid orderId, string source = "oms",
        bool bindItems = true)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-R-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: source, sourceSystemDisplayName: source.ToUpperInvariant());
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindItems)
            order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        orderId = order.Id;
        return order;
    }

    private sealed record Harness(
        ResendShipmentStartedCommandHandler Handler,
        ISourceCallbackDispatcher Dispatcher,
        IOrderAuditEventRepository Audit,
        ICallbackFormatterResolver Resolver,
        ISourceCallbackOutboxSuperseder Superseder,
        ICallbackPayloadFormatter Formatter,
        IOperatorRepository Operators,
        DomainOrder Order,
        Trip Trip,
        Guid OrderId,
        Guid TripId);

    // subscribedSystem = which system holds an ENABLED shipment.started
    // subscription (null = nobody, the off-switch case). tripFactory
    // overrides the default AMR trip (e.g. a claimed pool trip).
    private static Harness NewHarness(string orderSource = "oms", string? subscribedSystem = "oms",
        bool bindItems = true, Func<Guid, Trip>? tripFactory = null)
    {
        var tripId = Guid.NewGuid();
        var order = SourceOrder(tripId, out var orderId, orderSource, bindItems);

        Trip trip;
        if (tripFactory is not null)
        {
            trip = tripFactory(orderId);
        }
        else
        {
            trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
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
                System.Text.Encoding.UTF8.GetBytes("{\"shipmentId\":\"x\"}"),
                RelativePath: "/integrations/tms/shipments/started"));
        var resolver = Substitute.For<ICallbackFormatterResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(formatter);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync(CallbackEventTypes.ShipmentStartedV1, Arg.Any<CancellationToken>())
            .Returns(subscribedSystem is null
                ? new List<EventSubscriber>()
                : new List<EventSubscriber> { new(subscribedSystem, $"{subscribedSystem}.shipment.started.v1") });

        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();   // no throw = 2xx
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var superseder = Substitute.For<ISourceCallbackOutboxSuperseder>();
        var operators = Substitute.For<IOperatorRepository>();

        var handler = new ResendShipmentStartedCommandHandler(
            resolver, dispatcher, lookup, trips, orders, operators, audit, activity, superseder,
            NullLogger<ResendShipmentStartedCommandHandler>.Instance);

        return new Harness(handler, dispatcher, audit, resolver, superseder, formatter,
            operators, order, trip, orderId, tripId);
    }

    // 2026-08 contract — the handler must hand the formatter the full context:
    // the order's own OrderRef and the trip's StartedAt (business event time,
    // NOT the resend click time — a resend hours later reports the original).
    [Fact]
    public async Task Resend_PassesOrderRefAndTripStartedAt_ToFormatter()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentStartedContext>(c =>
                c.OrderRef == h.Order.OrderRef &&
                c.DeliveryBy == "FAN1_NO3" &&
                c.OccurredAt == h.Trip.StartedAt!.Value),
            Arg.Any<CancellationToken>());
    }

    // The wire payload no longer carries lots — a trip with no bound items is
    // resendable (the old guard returned Failure "No items are bound...").
    [Fact]
    public async Task Resend_NoBoundItems_StillSucceeds()
    {
        var h = NewHarness(bindItems: false);

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // Claimed pool trip: dispatched, claimed (reflection — prod writes this
    // via a raw SQL CAS), started at claim with no vendor vehicle.
    private static Trip ClaimedPoolTrip(Guid orderId, Guid operatorId)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        trip.MarkDispatched();
        typeof(Trip).GetProperty(nameof(Trip.ClaimedByOperatorId))!.SetValue(trip, operatorId);
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        return trip;
    }

    // Manual pool trip resend — deliveryBy resolves the claiming operator
    // ("DisplayName (EmployeeCode)"), same ladder as the auto fan-out. The
    // old handler failed these with "no VendorVehicleName".
    [Fact]
    public async Task Resend_ClaimedPoolTrip_UsesOperatorName()
    {
        var operatorId = Guid.NewGuid();
        var h = NewHarness(tripFactory: orderId => ClaimedPoolTrip(orderId, operatorId));
        h.Operators.GetByIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(Operator.CreateFromJwtClaims("EMP0142", "สมชาย ใจดี", OperatorRole.Operator));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Formatter.Received(1).FormatAsync(
            Arg.Is<ShipmentStartedContext>(c => c.DeliveryBy == "สมชาย ใจดี (EMP0142)"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_OmsOrder_DispatchesToOms_AndWritesManuallyResentAudit()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.ManuallyResent && e.SystemKey == "oms"),
            Arg.Any<CancellationToken>());
    }

    // The point of Phase C: a SAP order resolves SAP's subscription, SAP's
    // formatter key, and dispatches to the SAP partition — no OMS anywhere.
    [Fact]
    public async Task Resend_SapOrder_DispatchesToSap_WithSapFormatter()
    {
        var h = NewHarness(orderSource: "sap", subscribedSystem: "sap");

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        h.Resolver.Received(1).Resolve("sap.shipment.started.v1");
        await h.Dispatcher.Received(1).DispatchAsync(
            "sap", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.Received(1).AddAsync(
            Arg.Is<OrderAuditEvent>(e =>
                e.EventType == UpstreamCallbackAudit.ManuallyResent && e.SystemKey == "sap"),
            Arg.Any<CancellationToken>());
    }

    // A source without an enabled subscription must be refused — never
    // dispatched to somebody else's system.
    [Fact]
    public async Task Resend_SourceNotSubscribed_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(orderSource: "sap", subscribedSystem: "oms");   // sap order, only oms subscribed

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // The off-switch must close every path: subscription disabled (or absent)
    // → the resend refuses rather than POST behind the operator's back.
    [Fact]
    public async Task Resend_SubscriptionDisabled_ReturnsFailure_AndDoesNotDispatch()
    {
        var h = NewHarness(subscribedSystem: null);

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        await h.Dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await h.Audit.DidNotReceive().AddAsync(
            Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    // F4 — failure mapping: 4xx = upstream actively rejected (fix the data)
    // vs 5xx/transport = unavailable (retry later). Neither writes audit.
    [Fact]
    public async Task Resend_UpstreamRejects4xx_SaysRejected_AndWritesNoAudit()
    {
        var h = NewHarness();
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("lot not found", null, HttpStatusCode.NotFound));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("rejected");
        await h.Audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_Upstream5xx_SaysCallbackFailed_AndWritesNoAudit()
    {
        var h = NewHarness();
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("failed");
        await h.Audit.DidNotReceive().AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>());
    }

    // F2 — once DispatchAsync returns, the upstream HAS the callback; a
    // persistence failure after that must not masquerade as a send failure.
    [Fact]
    public async Task Resend_AuditWriteFails_AfterDelivery_StillReturnsSuccess()
    {
        var h = NewHarness();
        h.Audit.AddAsync(Arg.Any<OrderAuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db hiccup"));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).DispatchAsync(
            "oms", Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    // A successful resend retires any pending fan-out row for this order+system
    // so its queued retry can't re-POST a duplicate and clobber the success.
    [Fact]
    public async Task Resend_Success_SupersedesPendingOutboxRows_ForThisOrderAndSystem()
    {
        var h = NewHarness();

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Superseder.Received(1).SupersedePendingAsync(
            "oms", CallbackEventTypes.ShipmentStartedV1, h.OrderId, Arg.Any<CancellationToken>());
    }

    // Casing guard — the fan-out wrote PartitionKey = sub.SystemKey ("oms"),
    // but the order's SourceSystemKey may differ in case ("OMS"). Supersede
    // must key off the SUBSCRIPTION's SystemKey so the case-sensitive SQL
    // match hits the row; keying off order.SourceSystemKey would miss it.
    [Fact]
    public async Task Resend_Success_SupersedesUsingSubscriptionKey_NotOrderCasing()
    {
        var h = NewHarness(orderSource: "OMS", subscribedSystem: "oms");

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Superseder.Received(1).SupersedePendingAsync(
            "oms", CallbackEventTypes.ShipmentStartedV1, h.OrderId, Arg.Any<CancellationToken>());
        await h.Superseder.DidNotReceive().SupersedePendingAsync(
            "OMS", Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // The dispatch never happened, so there is nothing to supersede — a failed
    // resend must not retire the row that could still recover on its own.
    [Fact]
    public async Task Resend_DispatchFails_DoesNotSupersede()
    {
        var h = NewHarness();
        h.Dispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await h.Superseder.DidNotReceive().SupersedePendingAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Supersede is best-effort — a failure retiring the pending rows must not
    // turn a delivered resend into a reported failure.
    [Fact]
    public async Task Resend_SupersedeFails_AfterDelivery_StillReturnsSuccess()
    {
        var h = NewHarness();
        h.Superseder.SupersedePendingAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("outbox db hiccup"));

        var result = await h.Handler.Handle(
            new ResendShipmentStartedCommand(h.OrderId, h.TripId, "ops@dtms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
