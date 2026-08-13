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

// 2026-08 — shipment.droppedoff.v1 fan-out (renamed from arrived; the old
// consumer never had tests — this closes that gap too). Pins the enqueued
// row's path/body (locationCode = the upstream's own Item.DropLocationCode;
// occurredAt = Trip.VendorDroppedAt) and the mode guards: self-managed
// skipped (drop is source-reported), Manual pool SENT (the legacy blanket
// Manual skip is gone).
public class ShipmentDroppedOffFanoutConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();
    private const string Root = "33333333-3333-3333-3333-333333333333";

    private static string OccurredAtWire(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed class Harness
    {
        public required OutboxDbContext Outbox { get; init; }
        public required ISubscriptionLookup Lookup { get; init; }
        public required ITripRepository Trips { get; init; }
        public required IDeliveryOrderRepository Orders { get; init; }

        public ShipmentDroppedOffCallbackFanoutConsumer Build()
        {
            var sp = new ServiceCollection()
                .AddKeyedSingleton<ICallbackPayloadFormatter, OmsShipmentDroppedOffFormatter>(
                    OmsShipmentDroppedOffFormatter.FormatKey)
                .BuildServiceProvider();
            return new ShipmentDroppedOffCallbackFanoutConsumer(
                Lookup, sp, Outbox, Trips, Orders,
                NullLogger<ShipmentDroppedOffCallbackFanoutConsumer>.Instance);
        }
    }

    private static Harness NewHarness(bool subscribed)
    {
        var outbox = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("outbox-" + Guid.NewGuid()).Options);

        var lookup = Substitute.For<ISubscriptionLookup>();
        lookup.GetSubscribersAsync("shipment.droppedoff.v1", Arg.Any<CancellationToken>())
            .Returns(subscribed
                ? new List<EventSubscriber> { new("oms", OmsShipmentDroppedOffFormatter.FormatKey) }
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
        bool selfManaged = false, string dropCode = "STF_09")
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-DO-" + Guid.NewGuid().ToString("N")[..6], Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS",
            requestedBy: selfManaged ? "wms-operator-7" : null,
            requestedTransportMode: selfManaged ? TransportMode.Manual : TransportMode.Amr,
            selfManaged: selfManaged);
        order.AddItem("WH-A", dropCode, 1, "LOT-A", null, null, null, 5.0,
            Quantity.Create(1, UnitOfMeasure.EA));
        order.MarkAsValidated(new Dictionary<string, Guid> { ["WH-A"] = Pickup, [dropCode] = Drop });
        order.Confirm(weightFallbackKg: 5.0);
        if (bindTripId is not null)
            order.AssignItemsToTrip(bindTripId.Value, attemptNumber: 1, pickupStationId: Pickup, dropStationId: Drop);
        return order;
    }

    // AMR trip that started, picked up, then reported drop. Carries the
    // denormalized codes like every real trip born after 2026-08.
    private static Trip DroppedTrip(Guid orderId, DateTime? actedAt = null,
        string? dropCode = "STF_09")
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop,
            pickupLocationCode: "WH-A", dropLocationCode: dropCode);
        trip.MarkVendorStarted(vendorVehicleKey: "device-1", vendorVehicleName: "FAN1_NO3");
        trip.MarkVendorPickedUp();
        trip.MarkVendorDropCompleted(actedAt: actedAt);
        return trip;
    }

    private static ConsumeContext<TripDropCompletedIntegrationEvent> Ctx(
        TripDropCompletedIntegrationEvent evt)
    {
        var ctx = Substitute.For<ConsumeContext<TripDropCompletedIntegrationEvent>>();
        ctx.Message.Returns(evt);
        ctx.MessageId.Returns(Guid.NewGuid());
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static ConsumeContext<TripDropCompletedIntegrationEvent> Ctx(Guid tripId, Guid orderId) =>
        Ctx(new TripDropCompletedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, tripId, orderId));

    [Fact]
    public async Task HappyPath_EnqueuesContractRow_WithDropCodeAndVendorDroppedAt()
    {
        var tripId = Guid.NewGuid();
        // The vendor's mission ChangeStateTime — deliberately not "now" so the
        // assertion proves we serialize VendorDroppedAt, not OccurredOn.
        var actedAt = new DateTime(2026, 8, 1, 16, 42, 11, 208, DateTimeKind.Utc);
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);
        var trip = DroppedTrip(order.Id, actedAt);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.PartitionKey.Should().Be("oms");
        row.Type.Should().Be("shipment.droppedoff.v1");
        row.CallbackPath.Should().Be($"/integrations/tms/shipments/{Root}/dropoff-arrived");
        row.RelatedOrderId.Should().Be(order.Id);
        row.RelatedTripId.Should().Be(tripId);
        row.Content.Should().Be(
            JsonSerializer.Serialize(new
            {
                orderRef = order.OrderRef,
                locationCode = "STF_09",
                occurredAt = OccurredAtWire(actedAt),
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    // Self-managed drop is reported INTO DTMS by the source system — sending
    // it back would echo their own report.
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

    // Manual pool drop is geofence-verified — SENT, the old consumer's
    // blanket TransportMode.Manual skip is gone.
    [Fact]
    public async Task ManualPoolTrip_Sends()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(subscribed: true);
        var order = OmsOrder(bindTripId: tripId);
        var trip = Trip.CreateForEnvelope(order.Id, "upper-G1", "ORD-1", Pickup, Drop,
            pickupLocationCode: "WH-A", dropLocationCode: "STF_09");
        trip.MarkDispatched();
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        trip.MarkVendorPickedUp();
        trip.MarkVendorDropCompleted();   // operator tap → UtcNow
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(1);
    }

    // 2026-08 — the code frozen onto the Trip is the PRIMARY source: it wins
    // over the item scan and survives a cancel's unbinding.
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
        trip.MarkVendorDropCompleted();
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.Content.Should().Contain("\"locationCode\":\"STF_09\"");
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
            .Returns(DroppedTrip(order.Id, dropCode: null));

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
