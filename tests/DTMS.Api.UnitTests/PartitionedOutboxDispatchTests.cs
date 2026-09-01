using DTMS.Api.Infrastructure.Outbox;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace DTMS.Api.UnitTests;

// Commit 1 / F2 — dispatch used to run INSIDE the claim transaction, so a
// transient Npgsql error on commit made EF's execution strategy replay the
// whole batch and re-POST every callback that had already succeeded. Dispatch
// now lives in its own pass with no transaction around it; these tests pin the
// properties that pass must hold, since the duplicate-POST bug was really a
// question of "how many times does a claimed row reach the dispatcher".
public class PartitionedOutboxDispatchTests
{
    private const string SystemKey = "oms";

    private static MultiPartitionOutboxProcessor NewProcessor() =>
        new(Substitute.For<IServiceProvider>(),
            Substitute.For<IConnectionMultiplexer>(),
            NullLogger<MultiPartitionOutboxProcessor>.Instance);

    private static OutboxMessage Row() => new(
        id: Guid.NewGuid(),
        type: "shipment.started.v1",
        content: "{\"shipmentId\":\"x\"}",
        occurredOnUtc: DateTime.UtcNow,
        partitionKey: SystemKey);

    [Fact]
    public async Task EveryClaimedRow_ReachesTheDispatcher_ExactlyOnce()
    {
        var rows = new List<OutboxMessage> { Row(), Row(), Row() };
        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();

        var outcomes = await NewProcessor().DispatchClaimedAsync(
            dispatcher, SystemKey, rows, CancellationToken.None);

        outcomes.Should().HaveCount(3);
        outcomes.Values.Should().AllSatisfy(e => e.Should().BeNull());
        foreach (var row in rows)
        {
            await dispatcher.Received(1).DispatchAsync(
                SystemKey, row, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task AFailedRow_IsRecorded_AndDoesNotStopTheRowsBehindIt()
    {
        var rows = new List<OutboxMessage> { Row(), Row(), Row() };
        var boom = new HttpRequestException("receiver said no");
        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();
        dispatcher
            .DispatchAsync(SystemKey, rows[1], Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw boom);

        var outcomes = await NewProcessor().DispatchClaimedAsync(
            dispatcher, SystemKey, rows, CancellationToken.None);

        outcomes[rows[0].Id].Should().BeNull();
        outcomes[rows[1].Id].Should().BeSameAs(boom);
        outcomes[rows[2].Id].Should().BeNull("one bad receiver response must not strand the rest of the batch");
    }

    [Fact]
    public async Task Cancellation_LeavesRemainingRowsWithoutAnOutcome()
    {
        // Rows with no outcome are the ones PersistOutcomesAsync releases the
        // lease on — they must NOT be conflated with "dispatched successfully",
        // or a shutdown mid-batch would silently drop callbacks.
        var rows = new List<OutboxMessage> { Row(), Row(), Row() };
        using var cts = new CancellationTokenSource();
        var dispatcher = Substitute.For<ISourceCallbackDispatcher>();
        dispatcher
            .When(d => d.DispatchAsync(SystemKey, rows[0], Arg.Any<CancellationToken>()))
            .Do(_ => cts.Cancel());

        var outcomes = await NewProcessor().DispatchClaimedAsync(
            dispatcher, SystemKey, rows, cts.Token);

        outcomes.Should().ContainKey(rows[0].Id).WhoseValue.Should().BeNull();
        outcomes.Should().NotContainKey(rows[1].Id);
        outcomes.Should().NotContainKey(rows[2].Id);
        await dispatcher.DidNotReceive().DispatchAsync(
            SystemKey, rows[2], Arg.Any<CancellationToken>());
    }
}
