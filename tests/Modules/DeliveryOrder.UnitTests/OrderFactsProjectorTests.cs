using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

public class OrderFactsProjectorTests
{
    [Fact]
    public async Task Confirmed_UpsertsRowWithDimensionsAndMeasures()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var item1 = new ItemSummaryDto("SKU-001", 5.0, Guid.NewGuid(), Guid.NewGuid());
        var item2 = new ItemSummaryDto("SKU-002", 3.0, Guid.NewGuid(), Guid.NewGuid());
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId,
            "High", null, null, null, new[] { item1, item2 }, "Amr");

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpsertOnConfirmAsync(
            orderId,
            evt.OccurredOn,
            "High",
            "Amr",
            totalItems: 2,
            totalWeightKg: 8.0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatched_SetsDispatchedAt()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderDispatchedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetDispatchedAtAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Completed_SetsCompletedAt()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderCompletedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCompletedAtAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_SetsFailedAtAndReason()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "vendor rejected");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetFailedAtAsync(
            orderId, evt.OccurredOn, "vendor rejected", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelled_SetsCancelledAtAndReason()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderCancelledIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "operator");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCancelledAtAsync(
            orderId, evt.OccurredOn, "operator", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Held_SetsHeldAtAndReason()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderHeldIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "investigation");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetHeldAtAsync(
            orderId, evt.OccurredOn, "investigation", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Released_SetsReleasedAt()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderReleasedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetReleasedAtAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.HasProcessedEventAsync(
                OrderFactsProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().SetFailedAtAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.When(s => s.SetFailedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.When(s => s.SetFailedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (OrderFactsProjector projector, IOrderFactsProjectionStore store) Build()
    {
        var store = Substitute.For<IOrderFactsProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new OrderFactsProjector(
            store, metrics, NullLogger<OrderFactsProjector>.Instance);
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
