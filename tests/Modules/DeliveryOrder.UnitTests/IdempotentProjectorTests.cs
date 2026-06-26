using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

// File-scoped types — NSubstitute's Castle proxy requires non-private
// types for generic substitution (ConsumeContext<ProjectorTestEvent>).
public record ProjectorTestEvent(Guid EventId, DateTime OccurredOn, Guid OrderId) : IIntegrationEvent;

// Phase P0.1 — IdempotentProjector base behavior. These tests live in
// DeliveryOrder.UnitTests for convenience (NSubstitute + FluentAssertions
// already wired); migrate to a dedicated SharedKernel.UnitTests project
// when projection-related tests proliferate.
public class IdempotentProjectorTests
{
    private sealed class TestProjector : IdempotentProjector<ProjectorTestEvent>
    {
        public List<ProjectorTestEvent> Projected { get; } = new();
        public int SaveCalls { get; private set; }
        public Func<ProjectorTestEvent, Task>? OnProject { get; set; }

        public TestProjector(IProjectionInboxRepository inbox, ProjectionMetrics metrics)
            : base(inbox, metrics, NullLogger<TestProjector>.Instance) { }

        protected override async Task ProjectAsync(ProjectorTestEvent evt, CancellationToken ct)
        {
            Projected.Add(evt);
            if (OnProject is { } cb) await cb(evt);
        }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NewEvent_IsProjectedAndInboxRecorded()
    {
        var (projector, inbox, _) = BuildProjector();
        var evt = new ProjectorTestEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        await projector.Consume(ContextFor(evt));

        projector.Projected.Should().ContainSingle().Which.EventId.Should().Be(evt.EventId);
        projector.SaveCalls.Should().Be(1, "inbox + read-model must commit together");
        await inbox.Received(1).RecordAsync(
            Arg.Is<InboxMessage>(m => m.EventId == evt.EventId && m.ProjectorName == nameof(TestProjector)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkippedWithoutProjection()
    {
        var (projector, inbox, _) = BuildProjector();
        var evt = new ProjectorTestEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        inbox.HasProcessedAsync(nameof(TestProjector), evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(ContextFor(evt));

        projector.Projected.Should().BeEmpty("inbox hit ⇒ short-circuit, no projection");
        projector.SaveCalls.Should().Be(0);
        await inbox.DidNotReceive().RecordAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed_NotThrown()
    {
        var (projector, _, _) = BuildProjector();
        var evt = new ProjectorTestEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        projector.OnProject = _ => throw new InvalidOperationException("schema mismatch");

        var act = async () => await projector.Consume(ContextFor(evt));

        await act.Should().NotThrowAsync("permanent failures must not block the queue");
        projector.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task TransientFailure_IsRethrown_ForMassTransitRetry()
    {
        var (projector, _, _) = BuildProjector();
        var evt = new ProjectorTestEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        projector.OnProject = _ => throw new TimeoutException("db lock");

        var act = async () => await projector.Consume(ContextFor(evt));

        await act.Should().ThrowAsync<TimeoutException>(
            "transient failures bubble to MassTransit so the outbox retry policy kicks in");
    }

    private static (TestProjector projector, IProjectionInboxRepository inbox, ProjectionMetrics metrics)
        BuildProjector()
    {
        var inbox = Substitute.For<IProjectionInboxRepository>();
        inbox.HasProcessedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);  // default — new event
        var metrics = new ProjectionMetrics();
        return (new TestProjector(inbox, metrics), inbox, metrics);
    }

    private static ConsumeContext<T> ContextFor<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
