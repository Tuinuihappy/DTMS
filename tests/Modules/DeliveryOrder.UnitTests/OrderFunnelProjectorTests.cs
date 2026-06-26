using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

public class OrderFunnelProjectorTests
{
    [Fact]
    public async Task Confirmed_IncrementsConfirmedColumnAtEventHour()
    {
        var (projector, store) = Build();
        var occurredAt = new DateTime(2026, 6, 13, 14, 22, 11, DateTimeKind.Utc);
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), occurredAt, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).IncrementAsync(
            OrderFunnelProjector.Name,
            evt.EventId,
            occurredAt,
            "Confirmed",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_IncrementsFailedColumn()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "vendor rejected");

        await projector.Consume(Ctx(evt));

        await store.Received(1).IncrementAsync(
            OrderFunnelProjector.Name, evt.EventId, evt.OccurredOn,
            "Failed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.HasProcessedEventAsync(
                OrderFunnelProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().IncrementAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.When(s => s.IncrementAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.When(s => s.IncrementAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public void IncrementStatus_MapsKnownStatuses()
    {
        var row = new OrderFunnelHourlyRow(
            new DateTime(2026, 6, 13, 14, 0, 0, DateTimeKind.Utc));

        row.IncrementStatus("Confirmed");
        row.IncrementStatus("Confirmed");
        row.IncrementStatus("Failed");
        row.IncrementStatus("PartiallyCompleted");

        row.Confirmed.Should().Be(2);
        row.Failed.Should().Be(1);
        row.PartiallyCompleted.Should().Be(1);
        row.Cancelled.Should().Be(0);
    }

    [Fact]
    public void IncrementStatus_UnknownStatus_IsNoOp()
    {
        var row = new OrderFunnelHourlyRow(
            new DateTime(2026, 6, 13, 14, 0, 0, DateTimeKind.Utc));

        row.IncrementStatus("WhoKnows");

        row.Confirmed.Should().Be(0);
        row.Failed.Should().Be(0);
    }

    private static (OrderFunnelProjector projector, IOrderFunnelProjectionStore store) Build()
    {
        var store = Substitute.For<IOrderFunnelProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new OrderFunnelProjector(
            store, metrics, new NoopDashboardRealtimePublisher(), NullLogger<OrderFunnelProjector>.Instance);
        return (projector, store);
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var c = Substitute.For<ConsumeContext<T>>();
        c.Message.Returns(message);
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }
}
