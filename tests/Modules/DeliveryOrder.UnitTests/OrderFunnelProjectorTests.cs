using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Projections;
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

    // The status → counter-column mapping moved out of the entity when the
    // increment moved into SQL. It is still the thing worth pinning: the
    // column name is interpolated into the UPDATE, so only these literals
    // may ever come back from it.
    [Theory]
    [InlineData("Confirmed", "Confirmed")]
    [InlineData("Dispatched", "Dispatched")]
    [InlineData("InProgress", "InProgress")]
    [InlineData("Completed", "Completed")]
    [InlineData("PartiallyCompleted", "PartiallyCompleted")]
    [InlineData("Failed", "Failed")]
    [InlineData("Cancelled", "Cancelled")]
    [InlineData("Rejected", "Rejected")]
    [InlineData("Held", "Held")]
    [InlineData("Released", "Released")]
    public void CounterColumn_MapsKnownStatuses(string status, string expected)
        => OrderFunnelProjectionStore.CounterColumn(status).Should().Be(expected);

    [Theory]
    [InlineData("WhoKnows")]
    [InlineData("confirmed")]          // case-sensitive by design
    [InlineData("")]
    [InlineData("Confirmed\"; DROP TABLE x --")]
    public void CounterColumn_UnknownStatus_IsNull(string status)
        => OrderFunnelProjectionStore.CounterColumn(status).Should().BeNull();

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
