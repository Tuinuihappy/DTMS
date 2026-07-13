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
using DTMS.OmsAdapter.Abstractions.Exceptions;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DTMS.Api.UnitTests;

// Phase S.5 (B2) — the shipment-started fan-out: source-routed enqueue of a
// byte-identical OMS callback row, with the legacy skips preserved. Uses a real
// InMemory OutboxDbContext so we can assert the enqueued row's shape.
public class ShipmentStartedFanoutConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();
    private const string Root = "11111111-1111-1111-1111-111111111111";

    private sealed class Harness
    {
        public required OutboxDbContext Outbox { get; init; }
        public required ISubscriptionLookup Lookup { get; init; }
        public required ITripRepository Trips { get; init; }
        public required IDeliveryOrderRepository Orders { get; init; }
        public required ShipmentCallbackOptions Options { get; init; }

        public ShipmentStartedCallbackFanoutConsumer Build()
        {
            var sp = new ServiceCollection()
                .AddKeyedSingleton<ICallbackPayloadFormatter, OmsShipmentStartedFormatter>(
                    OmsShipmentStartedFormatter.FormatKey)
                .BuildServiceProvider();
            return new ShipmentStartedCallbackFanoutConsumer(
                Lookup, sp, Outbox, Trips, Orders,
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLogger<ShipmentStartedCallbackFanoutConsumer>.Instance);
        }
    }

    private static Harness NewHarness(bool enabled, bool subscribed)
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
            Options = new ShipmentCallbackOptions { ShipmentEventsEnabled = enabled },
        };
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

    private static Trip AmrTrip(Guid orderId, string? vehicleName, bool pooled = false)
    {
        var trip = Trip.CreateForEnvelope(orderId, "upper-G1", "ORD-1", Pickup, Drop);
        if (pooled) trip.MarkDispatched();   // sets DispatchedAt (pool path)
        // Key + name arrive together from RIOT3; pass both so VendorVehicleName
        // populates (null name = the fast-cap case).
        trip.MarkVendorStarted(
            vendorVehicleKey: vehicleName is null ? null : "device-" + vehicleName,
            vendorVehicleName: vehicleName);   // → InProgress
        return trip;
    }

    private static ConsumeContext<TripStartedIntegrationEvent> Ctx(Guid tripId, Guid orderId)
    {
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, Guid.NewGuid(), Guid.NewGuid(), orderId);
        var ctx = Substitute.For<ConsumeContext<TripStartedIntegrationEvent>>();
        ctx.Message.Returns(evt);
        ctx.MessageId.Returns(Guid.NewGuid());
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task HappyPath_EnqueuesByteIdenticalRow_RoutedToOms()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(enabled: true, subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(AmrTrip(order.Id, "FAN1_STANDARD_NO3"));

        await h.Build().Consume(Ctx(tripId, order.Id));

        var row = await h.Outbox.OutboxMessages.SingleAsync();
        row.PartitionKey.Should().Be("oms");
        row.CallbackPath.Should().Be("/api/shipments");
        row.Type.Should().Be("shipment.started.v1");
        row.RelatedOrderId.Should().Be(order.Id);
        row.RelatedTripId.Should().Be(tripId);
        // Body byte-identical to the legacy OmsShipmentNotification.
        row.Content.Should().Be(
            JsonSerializer.Serialize(new
            {
                shipmentId = Root,
                deliveryBy = "FAN1_STANDARD_NO3",
                lots = new[] { new { lotNo = "LOT-A" } },
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public async Task FlagOff_EnqueuesNothing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(enabled: false, subscribed: true);

        await h.Build().Consume(Ctx(tripId, Guid.NewGuid()));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
        await h.Orders.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SourceNotSubscribed_EnqueuesNothing()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(enabled: true, subscribed: false);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PoolTripAlreadyNotifiedAtDispatch_Skips()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(enabled: true, subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(AmrTrip(order.Id, "FAN1", pooled: true));

        await h.Build().Consume(Ctx(tripId, order.Id));

        (await h.Outbox.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MissingVehicleName_ThrowsFastCap()
    {
        var tripId = Guid.NewGuid();
        var h = NewHarness(enabled: true, subscribed: true);
        var order = OmsOrderWithBoundItem(tripId);
        h.Orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(order);
        h.Trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(AmrTrip(order.Id, vehicleName: null));

        var act = async () => await h.Build().Consume(Ctx(tripId, order.Id));

        await act.Should().ThrowAsync<VendorVehicleUnavailableException>();
    }
}
