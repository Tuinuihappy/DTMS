using AMR.DeliveryPlanning.Fleet.Application.Projections;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Fleet.UnitTests;

public class VehicleStateHistoryProjectorTests
{
    [Fact]
    public async Task FirstEvent_AppendsRowWithNullFromState()
    {
        var (projector, store) = Build();
        var vehicleId = Guid.NewGuid();
        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, vehicleId, "Idle", 0.85, null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            VehicleStateHistoryProjector.Name,
            evt.EventId, vehicleId,
            fromState: null,
            toState: "Idle",
            batteryLevel: 0.85,
            currentNodeId: (Guid?)null,
            occurredAt: evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondEvent_DerivesFromState()
    {
        var (projector, store) = Build();
        var vehicleId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        store.GetLatestForVehicleAsync(vehicleId, Arg.Any<CancellationToken>())
            .Returns(("Idle", t0));

        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, vehicleId, "Moving", 0.75, Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            VehicleStateHistoryProjector.Name,
            evt.EventId, vehicleId,
            fromState: "Idle",
            toState: "Moving",
            batteryLevel: 0.75,
            currentNodeId: evt.CurrentNodeId,
            occurredAt: evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Idle", 0.9, null);

        store.HasProcessedEventAsync(
                VehicleStateHistoryProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<double>(), Arg.Any<Guid?>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfOrderEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var vehicleId = Guid.NewGuid();
        var latestTime = DateTime.UtcNow;
        store.GetLatestForVehicleAsync(vehicleId, Arg.Any<CancellationToken>())
            .Returns(("Moving", latestTime));

        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), latestTime.AddMinutes(-1), vehicleId, "Idle", 0.9, null);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<double>(), Arg.Any<Guid?>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Idle", 0.9, null);

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<double>(), Arg.Any<Guid?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new VehicleStateChangedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Idle", 0.9, null);

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<double>(), Arg.Any<Guid?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (VehicleStateHistoryProjector projector, IVehicleStateHistoryProjectionStore store) Build()
    {
        var store = Substitute.For<IVehicleStateHistoryProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.GetLatestForVehicleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(((string, DateTime)?)null);
        var metrics = new ProjectionMetrics();
        var projector = new VehicleStateHistoryProjector(
            store, metrics, NullLogger<VehicleStateHistoryProjector>.Instance);
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
