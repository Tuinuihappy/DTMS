using DTMS.Api.Infrastructure.Outbox;
using DTMS.SharedKernel.Diagnostics;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// P0 — SourceCallbackOutcome rows are written into the central `outbox`
// schema with a NULL PartitionKey. MultiPartitionOutboxProcessor only drains
// partitioned rows, and (before this fix) OutboxProcessorService only drained
// the 5 module schemas — so these rows stranded silently and the audit mirror
// went dark. These tests pin the central pass that closes that gap, and above
// all its double-delivery guard: a PARTITIONED row belongs to
// MultiPartitionOutboxProcessor (HTTP callback) and must never be published
// onto the bus by this pass.
public class CentralOutboxDrainTests
{
    private static OutboxDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("central-outbox-" + Guid.NewGuid()).Options);

    private static SourceCallbackOutcome Outcome() => new(
        EventId: Guid.NewGuid(),
        OccurredOn: DateTime.UtcNow,
        SystemKey: "oms",
        CallbackEventType: "shipment.started.v1",
        OrderId: Guid.NewGuid(),
        TripId: Guid.NewGuid(),
        Success: true,
        StatusCode: 200,
        Detail: null,
        CorrelationId: null);

    private static OutboxMessage PartitionedRow() => new(
        id: Guid.NewGuid(),
        type: "shipment.started.v1",
        content: "{\"shipmentId\":\"x\"}",
        occurredOnUtc: DateTime.UtcNow,
        partitionKey: "oms");

    private static OutboxProcessorService NewService()
    {
        var options = Substitute.For<IOptionsMonitor<OutboxOptions>>();
        options.CurrentValue.Returns(new OutboxOptions { UseSkipLocked = false });
        return new OutboxProcessorService(
            Substitute.For<IServiceScopeFactory>(),
            options,
            new WorkflowMetrics(),
            Substitute.For<IOutboxWakeSignal>(),
            NullLogger<OutboxProcessorService>.Instance);
    }

    [Fact]
    public async Task FetchCentral_TakesNullPartitionRows_AndNeverPartitionedOnes()
    {
        await using var db = NewDb();
        var nullRow = OutboxMessageFactory.FromIntegrationEvent(Outcome());
        db.OutboxMessages.Add(nullRow);
        db.OutboxMessages.Add(PartitionedRow());   // MultiPartition's property
        await db.SaveChangesAsync();

        var batch = await OutboxProcessorService.FetchCentralBatchAsync(db, 10, CancellationToken.None);

        batch.Should().ContainSingle().Which.Id.Should().Be(nullRow.Id);
    }

    [Fact]
    public async Task ProcessCentral_PublishesOutcome_MarksProcessed_AndSkipsPartitioned()
    {
        await using var db = NewDb();
        var nullRow = OutboxMessageFactory.FromIntegrationEvent(Outcome());
        var partitioned = PartitionedRow();
        db.OutboxMessages.AddRange(nullRow, partitioned);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IPublishEndpoint>();
        var dlq = Substitute.For<IDeadLetterStore>();
        var opts = new OutboxOptions { UseSkipLocked = false, BatchSize = 10, PublishConcurrency = 1 };

        var (pending, processed) = await NewService().ProcessCentralOutboxAsync(
            db, publisher, dlq, opts, CancellationToken.None);

        processed.Should().Be(1);
        pending.Should().Be(0);   // null-partition backlog fully drained
        await publisher.Received(1).Publish(
            Arg.Any<object>(), typeof(SourceCallbackOutcome), Arg.Any<CancellationToken>());

        (await db.OutboxMessages.SingleAsync(m => m.Id == nullRow.Id))
            .ProcessedOnUtc.Should().NotBeNull("the drained outcome row must not be re-fetched forever");
        (await db.OutboxMessages.SingleAsync(m => m.Id == partitioned.Id))
            .ProcessedOnUtc.Should().BeNull("partitioned rows belong to MultiPartitionOutboxProcessor — publishing them here would double-deliver");
    }

    [Fact]
    public async Task ProcessCentral_EmptyBacklog_IsAQuietNoOp()
    {
        await using var db = NewDb();
        db.OutboxMessages.Add(PartitionedRow());   // partitioned only — not ours
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IPublishEndpoint>();
        var (pending, processed) = await NewService().ProcessCentralOutboxAsync(
            db, publisher, Substitute.For<IDeadLetterStore>(),
            new OutboxOptions { UseSkipLocked = false, BatchSize = 10 }, CancellationToken.None);

        processed.Should().Be(0);
        pending.Should().Be(0);
        await publisher.DidNotReceiveWithAnyArgs().Publish(
            Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }
}
