using System.Globalization;
using System.Text;
using System.Text.Json;
using DTMS.Api.Infrastructure.Callbacks;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Domain;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using DTMS.SharedKernel.Exceptions;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DTMS.Api.UnitTests;

// Phase S.5 (B2) — the shipment-started fan-out: source-routed enqueue of the
// 2026-08 TMS-integration callback row {shipmentId, orderRef, deliveryBy,
// occurredAt}. Uses a real InMemory OutboxDbContext so we can assert the
// enqueued row's shape.
public class ShipmentStartedFanoutConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();
    private const string Root = "11111111-1111-1111-1111-111111111111";

    // Mirrors OmsShipmentStartedFormatter's pinned wire format.
    private static string OccurredAtWire(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed class Harness
    {
        public required OutboxDbContext Outbox { get; init; }
        public required ISubscriptionLookup Lookup { get; init; }
        public required ITripRepository Trips { get; init; }
        public required IDeliveryOrderRepository Orders { get; init; }
        public required IOperatorRepository Operators { get; init; }

        public ShipmentStartedCallbackFanoutConsumer Build()
        {
            var sp = new ServiceCollection()
                .AddKeyedSingleton<ICallbackPayloadFormatter, OmsShipmentStartedFormatter>(
                    OmsShipmentStartedFormatter.FormatKey)
                .BuildServiceProvider();
            return new ShipmentStartedCallbackFanoutConsumer(
                Lookup, sp, Outbox, Trips, Orders, Operators,
                NullLogger<ShipmentStartedCallbackFanoutConsumer>.Instance);
        }
    }

    private static Harness NewHarness(bool subscribed)
    {
        var outbox = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("outbox-" + Guid.NewGuid()).Options);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync("shipment.started.v1", Arg.Any<CancellationToken>())
            .Returns(subscribed
                ? new List<EventSubscriber> { new("oms", OmsShipmentStartedFormatter.FormatKey) }
                : new List<EventSubscriber>());

        var trips = Substitute.For<ITripRepository>();
        trips.GetRootTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.Parse(Root));

        return new Harness
        {
            Outbox = outbox,
            Lookup = lookup,
            Trips = trips,
            Orders = Substitute.For<IDeliveryOrderRepository>(),
            Operators = Substitute.For<IOperatorRepository>(),
        };
    }

    // Pool trip: dispatched into the pool, then started at claim time — the
    // manual claim path calls MarkVendorStarted with all vehicle fields null
    // (AcknowledgeTripCommandHandler). ClaimedByOperatorId is written by a raw
    // SQL CAS in prod, so tests set it via reflection (same idiom as
    // AcknowledgeTripHandlerTests.ClaimedTrip).
    private static Trip PoolTrip(Guid orderId, Guid? claimedBy)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        trip.MarkDispatched();
        if (claimedBy is not null)
            typeof(Trip).GetProperty(nameof(Trip.ClaimedByOperatorId))!.SetValue(trip, claimedBy);
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        return trip;
    }

    // Order (source=oms) with one item bound to the trip.
    private static DomainOrder OmsOrderWithBoundItem(Guid tripId)
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-TEST-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS");
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        // CreateFromUpstream already lands the order as Submitted — no Submit().
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        return order;
    }

    private static Trip AmrTrip(Guid orderId, string? vehicleName)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        // Key + name arrive together from RIOT3; pass both so VendorVehicleName
        // populates (null name = the fast-cap case).
        trip.MarkVendorStarted(
            vendorVehicleKey: vehicleName is null ? null : "device-" + vehicleName,
            vendorVehicleName: vehicleName);   // → InProgress
        return trip;
    }

    private static ConsumeContext<TripStartedIntegrationEvent> Ctx(Guid tripId, Guid orderId) =>
        Ctx(new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, Guid.NewGuid(), Guid.NewGuid(), orderId));

    private static ConsumeContext<TripStartedIntegrationEvent> Ctx(TripStartedIntegrationEvent evt)
    {
        var ctx = Substitute.For<ConsumeContext<TripStartedIntegrationEvent>>();
        ctx.Message.Returns(evt);
        ctx.MessageId.Returns(Guid.NewGuid());
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task HappyPath_EnqueuesContractRow_RoutedToOms()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        var trip = AmrTrip(order.Id, "FAN1_STANDARD_NO3");
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.PartitionKey.Should().Be("oms");
        row.CallbackPath.Should().Be("/integrations/tms/shipments/started");
        row.Type.Should().Be("shipment.started.v1");
        row.RelatedOrderId.Should().Be(order.Id);
        row.RelatedTripId.Should().Be(tripId);
        // occurredAt = the trip's own StartedAt (business event time), not
        // the enqueue time — serialized with the formatter's pinned pattern.
        row.Content.Should().Be(
            JsonSerializer.Serialize(new
            {
                shipmentId = Root,
                orderRef = order.OrderRef,
                deliveryBy = "FAN1_STANDARD_NO3",
                occurredAt = OccurredAtWire(trip.StartedAt!.Value),
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    // The wire payload no longer carries lots, so a trip that starts before
    // any item is bound must still notify — the old "pre-binding row, skip"
    // guard silently dropped the callback forever (nothing re-fires it).
    [Fact]
    public async Task NoBoundItems_StillEnqueues()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(Guid.NewGuid());   // items bound to a DIFFERENT trip
        var trip = AmrTrip(order.Id, "FAN1_STANDARD_NO3");
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(1);
    }

    // Self-managed orders are the only reachable null-trip path (the vendor
    // branch throws first) — occurredAt falls back to the event's OccurredOn,
    // stamped microseconds after StartedAt in the same domain block.
    [Fact]
    public async Task SelfManaged_NullTrip_UsesEventOccurredOn()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = DomainOrder.CreateFromUpstream(
            "OD-SELF-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS",
            requestedBy: "wms-operator-7",
            requestedTransportMode: DTMS.DeliveryOrder.Domain.Enums.TransportMode.Manual,
            selfManaged: true);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        // Trips.GetByIdAsync returns null (substitute default) — trip not found.

        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), new DateTime(2026, 8, 1, 2, 17, 41, 263, DateTimeKind.Utc),
            tripId, Guid.NewGuid(), Guid.NewGuid(), order.Id);
        await h.Build().Consume(Ctx(evt));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.Content.Should().Contain("\"deliveryBy\":\"wms-operator-7\"");
        row.Content.Should().Contain("\"occurredAt\":\"2026-08-01T02:17:41.263Z\"");
    }

    [Fact]
    public async Task SourceNotSubscribed_EnqueuesNothing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: false);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    // Pool trips notify at claim time (2026-08 — the legacy dispatch-time
    // notifier is long gone, so the old DispatchedAt skip silently dropped
    // every pool shipment.started forever). deliveryBy = the claiming
    // operator as "DisplayName (EmployeeCode)".
    [Fact]
    public async Task PoolTripClaimed_EnqueuesWithOperatorName()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(PoolTrip(order.Id, claimedBy: operatorId));
        h.Operators.GetByIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(Operator.CreateFromJwtClaims("EMP0142", "สมชาย ใจดี", OperatorRole.Operator));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.CallbackPath.Should().Be("/integrations/tms/shipments/started");
        // STJ escapes non-ASCII on the wire (ส...) — assert the decoded
        // value, not the raw bytes.
        using var doc = JsonDocument.Parse(row.Content);
        doc.RootElement.GetProperty("deliveryBy").GetString()
            .Should().Be("สมชาย ใจดี (EMP0142)");
    }

    // Admin force-start before any claim: the shipment did start and the
    // upstream is create-once, so send with a null carrier instead of
    // dropping the callback forever.
    [Fact]
    public async Task PoolTripUnclaimed_EnqueuesWithNullDeliveryBy()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(PoolTrip(order.Id, claimedBy: null));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.Content.Should().Contain("\"deliveryBy\":null");
    }

    // Operator row vanished between claim and consume (or was never synced) —
    // the name is display data, not worth blocking the callback over.
    [Fact]
    public async Task PoolTripClaimed_OperatorRowMissing_EnqueuesWithNullDeliveryBy()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(PoolTrip(order.Id, claimedBy: Guid.NewGuid()));
        // Operators.GetByIdAsync returns null (substitute default).

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.Content.Should().Contain("\"deliveryBy\":null");
    }

    [Fact]
    public async Task MissingVehicleName_ThrowsFastCap()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(AmrTrip(order.Id, vehicleName: null));

        var act = async () => await h.Build().Consume(Ctx(tripId, order.Id));

        await act.Should().ThrowAsync<VendorVehicleUnavailableException>();
    }
}
