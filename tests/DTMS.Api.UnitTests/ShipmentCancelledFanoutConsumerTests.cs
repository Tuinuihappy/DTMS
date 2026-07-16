using System.Text.Json;
using DTMS.Api.Infrastructure.Callbacks;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DTMS.Api.UnitTests;

// The cancel fan-out: tells the source system a started shipment died. Sibling of
// ShipmentStartedFanoutConsumerTests — same InMemory-outbox harness, and the same
// root-trip-id contract, since OMS keys shipments on the root trip id that
// shipment.started.v1 carried.
public class ShipmentCancelledFanoutConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();
    private const string Root = "11111111-1111-1111-1111-111111111111";
    private const string Reason = "vendor cancelled";
    private const string Actor = "86347852";
    private static readonly DateTime OccurredAt = new(2026, 7, 15, 9, 7, 9, DateTimeKind.Utc);

    private sealed class Harness
    {
        public required OutboxDbContext Outbox { get; init; }
        public required ISubscriptionLookup Lookup { get; init; }
        public required ITripRepository Trips { get; init; }
        public required IDeliveryOrderRepository Orders { get; init; }

        public ShipmentCancelledCallbackFanoutConsumer Build()
        {
            var sp = new ServiceCollection()
                .AddKeyedSingleton<ICallbackPayloadFormatter, OmsShipmentCancelledFormatter>(
                    OmsShipmentCancelledFormatter.FormatKey)
                .BuildServiceProvider();
            return new ShipmentCancelledCallbackFanoutConsumer(
                Lookup, sp, Outbox, Trips, Orders,
                NullLogger<ShipmentCancelledCallbackFanoutConsumer>.Instance);
        }
    }

    private static Harness NewHarness(bool subscribed, int subscriberCount = 1)
    {
        var outbox = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("outbox-" + Guid.NewGuid()).Options);

        var subs = new List<EventSubscriber>();
        if (subscribed)
        {
            subs.Add(new("oms", OmsShipmentCancelledFormatter.FormatKey));
            // A second system on the same source key would be unusual; this
            // stands in for "two subscribers routed to the same source".
            for (var i = 1; i < subscriberCount; i++)
                subs.Add(new("OMS", OmsShipmentCancelledFormatter.FormatKey));
        }

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync("shipment.cancelled.v1", Arg.Any<CancellationToken>())
            .Returns(subs);

        var trips = Substitute.For<ITripRepository>();
        trips.GetRootTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.Parse(Root));

        return new Harness
        {
            Outbox = outbox,
            Lookup = lookup,
            Trips = trips,
            Orders = Substitute.For<IDeliveryOrderRepository>(),
        };
    }

    private static DomainOrder OmsOrder(Guid tripId, bool bindItems = true, string sourceKey = "oms")
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-TEST-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: sourceKey, sourceSystemDisplayName: "OMS");
        order.AddItem("WH-A", "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindItems)
            order.AssignItemsToTrip(tripId, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        return order;
    }

    private static Trip StartedTrip(Guid orderId, bool pooled = false)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        if (pooled) trip.MarkDispatched();
        trip.MarkVendorStarted(
            vendorVehicleKey: "device-FAN1",
            vendorVehicleName: "FAN1_STANDARD_NO1");   // → InProgress, sets StartedAt
        return trip;
    }

    // Never started: Created status, StartedAt null — what the order-cancelled
    // cascade kills when it sweeps a trip that hadn't left yet.
    private static Trip CreatedTrip(Guid orderId) =>
        Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);

    private static ConsumeContext<TripCancelledIntegrationEvent> Ctx(
        Guid tripId, Guid orderId, bool nullMessageId = false)
    {
        var evt = new TripCancelledIntegrationEvent(
            Guid.NewGuid(), OccurredAt, tripId, Guid.NewGuid(), orderId, Reason, "upper-G1",
            TriggeredBy: Actor);
        var ctx = Substitute.For<ConsumeContext<TripCancelledIntegrationEvent>>();
        ctx.Message.Returns(evt);
        ctx.MessageId.Returns(nullMessageId ? null : Guid.NewGuid());
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task HappyPath_EnqueuesCancelRow_KeyedOnRootTripId()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(StartedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.PartitionKey.Should().Be("oms");
        row.Type.Should().Be("shipment.cancelled.v1");
        row.RelatedOrderId.Should().Be(order.Id);
        row.RelatedTripId.Should().Be(tripId);
        // Actor and timestamp come off the event, not from DateTime.UtcNow here —
        // a retried callback must not re-stamp the moment of cancellation.
        row.Content.Should().Be(
            JsonSerializer.Serialize(
                new { cancelReason = Reason, cancelledBy = Actor, occurredAt = OccurredAt },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    // The whole point of the trip-level design: OMS knows the shipment by the
    // ROOT trip id it got from shipment.started.v1, not by the id of whichever
    // retry attempt happened to die.
    [Fact]
    public async Task Path_UsesRootTripId_NotTheCancelledAttemptId()
    {
        var tripId = Guid.NewGuid();   // != Root
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(StartedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.CallbackPath.Should().Be($"/api/shipments/{Root}/cancelled");
        row.CallbackPath.Should().NotContain(tripId.ToString());
    }

    // REGRESSION LOCK. TripCancelledConsumer unbinds this trip's items while
    // handling the same TripCancelledIntegrationEvent on its own queue, so by the
    // time we run the items may already be gone. Mirroring ShipmentStarted's
    // `lots.Count == 0 → skip` guard would drop the cancel whenever that consumer
    // commits first — green in tests, intermittent in prod. The cancel must not
    // look at items at all.
    [Fact]
    public async Task ItemsAlreadyUnbound_StillEnqueues()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId, bindItems: false);   // TripCancelledConsumer won the race
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(StartedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SourceNotSubscribed_EnqueuesNothing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: false);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    // No started was ever sent for a pool trip (TripStartedOmsNotifyConsumer is
    // gone and nothing replaced it), so cancelling one would name a shipment the
    // subscriber has never seen.
    [Fact]
    public async Task PoolTrip_NeverStartedUpstream_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(StartedTrip(order.Id, pooled: true));

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TripNeverStarted_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(CreatedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OrderNotFound_SkipsWithoutThrowing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DomainOrder?)null);

        await h.Build().Consume(Ctx(tripId, Guid.NewGuid()));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    // Fail closed: without the trip we cannot resolve the root id, and guessing
    // would cancel the wrong shipment.
    [Fact]
    public async Task TripNotFound_SkipsWithoutThrowing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns((Trip?)null);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EmptyDeliveryOrderId_SkipsWithoutLookup()
    {
        var h = NewHarness(subscribed: true);

        await h.Build().Consume(Ctx(Guid.NewGuid(), Guid.Empty));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
        await h.Orders.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NullMessageId_StillEnqueuesOneRow()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(StartedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id, nullMessageId: true));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.CorrelationId.Should().NotBeNull();
    }

    // Source routing is case-insensitive, matching the started/arrived fan-outs.
    [Fact]
    public async Task TwoSubscribersOnSameSource_EachGetsARow_SameCorrelationId()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true, subscriberCount: 2);
        var order = OmsOrder(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(StartedTrip(order.Id));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var rows = await h.Outbox.OutboxMessages.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.CorrelationId).Distinct().Should().HaveCount(1);
    }
}
