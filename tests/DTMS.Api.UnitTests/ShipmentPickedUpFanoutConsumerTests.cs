using System.Globalization;
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

// 2026-08 — shipment.pickedup.v1 fan-out: the first outbound callback on the
// pickup lifecycle. Pins the enqueued row's path/body (locationCode = the
// upstream's own Item.PickupLocationCode; occurredAt = Trip.VendorPickedUpAt)
// and the mode guards (self-managed skipped, Manual sent).
public class ShipmentPickedUpFanoutConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();
    private const string Root = "22222222-2222-2222-2222-222222222222";

    private static string OccurredAtWire(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed class Harness
    {
        public required OutboxDbContext Outbox { get; init; }
        public required ISubscriptionLookup Lookup { get; init; }
        public required ITripRepository Trips { get; init; }
        public required IDeliveryOrderRepository Orders { get; init; }

        public ShipmentPickedUpCallbackFanoutConsumer Build()
        {
            var sp = new ServiceCollection()
                .AddKeyedSingleton<ICallbackPayloadFormatter, OmsShipmentPickedUpFormatter>(
                    OmsShipmentPickedUpFormatter.FormatKey)
                .BuildServiceProvider();
            return new ShipmentPickedUpCallbackFanoutConsumer(
                Lookup, sp, Outbox, Trips, Orders,
                NullLogger<ShipmentPickedUpCallbackFanoutConsumer>.Instance);
        }
    }

    private static Harness NewHarness(bool subscribed)
    {
        var outbox = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("outbox-" + Guid.NewGuid()).Options);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync("shipment.pickedup.v1", Arg.Any<CancellationToken>())
            .Returns(subscribed
                ? new List<EventSubscriber> { new("oms", OmsShipmentPickedUpFormatter.FormatKey) }
                : new List<EventSubscriber>());

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

    private static DomainOrder OmsOrder(Guid? bindTripId,
        bool selfManaged = false, string pickupCode = "WH-A")
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-PU-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS",
            requestedBy: selfManaged ? "wms-operator-7" : null,
            requestedTransportMode: selfManaged ? TransportMode.Manual : TransportMode.Amr,
            selfManaged: selfManaged);
        order.AddItem(pickupCode, "DOCK-1", 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { [pickupCode] = Pickup, ["DOCK-1"] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindTripId is not null)
            order.AssignItemsToTrip(bindTripId.Value, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        return order;
    }

    // AMR trip that started then reported pickup (MOVE FINISHED at the dock).
    // Carries the denormalized codes like every real trip born after 2026-08.
    private static Trip PickedUpTrip(Guid orderId, DateTime? actedAt = null,
        string? pickupCode = "WH-A")
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop,
            pickupLocationCode: pickupCode, dropLocationCode: "DOCK-1");
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        trip.MarkVendorPickedUp(actedAt: actedAt);
        return trip;
    }

    private static ConsumeContext<TripPickupCompletedIntegrationEvent> Ctx(
        TripPickupCompletedIntegrationEvent evt)
    {
        var ctx = Substitute.For<ConsumeContext<TripPickupCompletedIntegrationEvent>>();
        ctx.Message.Returns(evt);
        ctx.MessageId.Returns(Guid.NewGuid());
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static ConsumeContext<TripPickupCompletedIntegrationEvent> Ctx(Guid tripId, Guid orderId) =>
        Ctx(new TripPickupCompletedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, tripId, orderId));

    [Fact]
    public async Task HappyPath_EnqueuesContractRow_WithLocationCodeAndVendorPickedUpAt()
    {
        var tripId = Guid.NewGuid();
        // The vendor's mission ChangeStateTime — deliberately not "now" so the
        // assertion proves we serialize VendorPickedUpAt, not OccurredOn.
        var actedAt = new DateTime(2026, 8, 1, 9, 24, 3, 512, DateTimeKind.Utc);
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);
        var trip = PickedUpTrip(order.Id, actedAt);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.PartitionKey.Should().Be("oms");
        row.Type.Should().Be("shipment.pickedup.v1");
        row.CallbackPath.Should().Be($"/integrations/tms/shipments/{Root}/pickup-arrived");
        row.RelatedOrderId.Should().Be(order.Id);
        row.RelatedTripId.Should().Be(tripId);
        row.Content.Should().Be(
            JsonSerializer.Serialize(new
            {
                orderRef = order.OrderRef,
                locationCode = "WH-A",
                occurredAt = OccurredAtWire(actedAt),
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    // Self-managed pickup auto-fires at dispatch — not a physical signal; the
    // source system executes the transport itself.
    [Fact]
    public async Task SelfManagedOrder_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId, selfManaged: true);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    // Manual pool pickup is geofence-verified loading — SENT, unlike the
    // arrived fan-out's legacy Manual skip.
    [Fact]
    public async Task ManualPoolTrip_Sends()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);
        var trip = Trip.CreateForEnvelope(order.Id, "upper-G1", "ORD-1", Pickup, Drop,
            pickupLocationCode: "WH-A", dropLocationCode: "DOCK-1");
        trip.MarkDispatched();
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        trip.MarkVendorPickedUp();   // operator tap → UtcNow
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(1);
    }

    // 2026-08 — the code frozen onto the Trip is the PRIMARY source: it wins
    // over the item scan and survives a cancel's unbinding (items here are
    // bound to a DIFFERENT trip, exactly the post-cancel shape).
    [Fact]
    public async Task TripCode_Primary_SurvivesUnboundItems()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: Guid.NewGuid());   // nothing bound to THIS trip
        var trip = Trip.CreateForEnvelope(order.Id, "upper-G1", "ORD-1", Pickup, Drop,
            pickupLocationCode: "SHELF1", dropLocationCode: "STF_09");
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        trip.MarkVendorPickedUp();
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.Content.Should().Contain("\"locationCode\":\"SHELF1\"");
    }

    // locationCode is REQUIRED by the endpoint and reads from the Trip alone
    // (item-scan fallback retired 2026-08-13) — a code-less trip skips even
    // though items are bound.
    [Fact]
    public async Task NoTripCode_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);   // items bound — irrelevant now
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(PickedUpTrip(order.Id, pickupCode: null));

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SourceNotSubscribed_EnqueuesNothing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: false);
        var order = OmsOrder(bindTripId: tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    // Trip row missing (theoretical) — without a trip there is no location
    // code, so the consumer skips loudly rather than guessing (the item-scan
    // fallback that used to cover this was retired 2026-08-13).
    [Fact]
    public async Task NullTrip_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        // Trips.GetByIdAsync returns null (substitute default).

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }
}
