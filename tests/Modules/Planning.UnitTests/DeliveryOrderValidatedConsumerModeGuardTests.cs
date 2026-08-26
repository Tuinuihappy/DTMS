using DTMS.DeliveryOrder.Application.Commands.MarkAllItemsAsDispatchFailed;
using DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using DTMS.DeliveryOrder.Application.Commands.RecomputeOrderStatus;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Services;
using DTMS.Planning.Application.Consumers;
using DTMS.SharedKernel.Diagnostics;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

// Unregistered-mode guard: an order whose TransportMode has no registered
// IDispatchStrategy must be driven to Failed (fail all pending items +
// recompute), never parked at Planning — the pre-2026-08-20 branch sent
// MarkOrderPlanning + Recompute, which stalled forever because pending
// items count as in-flight.
public class DeliveryOrderValidatedConsumerModeGuardTests
{
    [Fact]
    public async Task UnregisteredMode_FailsAllItemsAndRecomputes_WithoutPlanning()
    {
        var (consumer, registry, sender) = Build();
        registry.IsRegistered(TransportMode.Manual).Returns(false);
        var orderId = Guid.NewGuid();

        await consumer.Consume(Ctx(Event(orderId, "Manual")));

        await sender.Received(1).Send(
            Arg.Is<MarkAllItemsAsDispatchFailedCommand>(c =>
                c.OrderId == orderId && !string.IsNullOrWhiteSpace(c.Reason)),
            Arg.Any<CancellationToken>());
        await sender.Received(1).Send(
            Arg.Is<RecomputeOrderStatusCommand>(c => c.OrderId == orderId),
            Arg.Any<CancellationToken>());
        await sender.DidNotReceive().Send(
            Arg.Any<MarkOrderPlanningCommand>(), Arg.Any<CancellationToken>());
        registry.DidNotReceive().Get(Arg.Any<TransportMode>());
    }

    [Fact]
    public async Task RegisteredMode_SkipsGuard_ProceedsToPlanning()
    {
        var (consumer, registry, sender) = Build();
        var strategy = Substitute.For<IDispatchStrategy>();
        strategy.Mode.Returns(TransportMode.Amr);
        strategy.GroupItems(Arg.Any<IReadOnlyList<DispatchGroupItem>>())
            .Returns(new List<DispatchGroup>());
        registry.IsRegistered(TransportMode.Amr).Returns(true);
        registry.Get(TransportMode.Amr).Returns(strategy);
        var orderId = Guid.NewGuid();

        await consumer.Consume(Ctx(Event(orderId, "Amr")));

        await sender.Received(1).Send(
            Arg.Is<MarkOrderPlanningCommand>(c => c.OrderId == orderId),
            Arg.Any<CancellationToken>());
        await sender.DidNotReceive().Send(
            Arg.Any<MarkAllItemsAsDispatchFailedCommand>(), Arg.Any<CancellationToken>());
    }

    private static (DeliveryOrderValidatedConsumer consumer,
        IDispatchStrategyRegistry registry,
        ISender sender) Build()
    {
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        var sender = Substitute.For<ISender>();
        var consumer = new DeliveryOrderValidatedConsumer(
            registry,
            Substitute.For<ISelfManagedDispatchService>(),
            sender,
            new WorkflowMetrics(),
            NullLogger<DeliveryOrderValidatedConsumer>.Instance);
        return (consumer, registry, sender);
    }

    private static DeliveryOrderConfirmedIntegrationEventV1 Event(Guid orderId, string mode) => new(
        Guid.NewGuid(), DateTime.UtcNow, orderId, "Normal",
        EarliestUtc: null, LatestUtc: null, SubmittedAt: DateTime.UtcNow,
        Items:
        [
            new ItemSummaryDto("SKU-1", 1.0, Guid.NewGuid(), Guid.NewGuid()),
        ],
        RequestedTransportMode: mode);

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var c = Substitute.For<ConsumeContext<T>>();
        c.Message.Returns(message);
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }
}
