using DTMS.Api.Infrastructure.Outbox;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTMS.Api.UnitTests;

// Supersede-on-resend (plan A). When a manual resend has already delivered a
// callback, the still-queued fan-out row for the same order+system+event must
// be retired so its retry can't re-POST a duplicate. These tests pin the
// scope (order-wide, covering retry-chain siblings) and the idempotency guard
// (rows the processor already terminated are left untouched).
public class SourceCallbackOutboxSupersederTests
{
    private const string Oms = "oms";
    private const string Started = "shipment.started.v1";

    private static OutboxDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase("supersede-outbox-" + Guid.NewGuid()).Options);

    private static SourceCallbackOutboxSuperseder NewSut(OutboxDbContext db) =>
        new(db, NullLogger<SourceCallbackOutboxSuperseder>.Instance);

    private static OutboxMessage Row(
        string system, string type, Guid orderId, Guid tripId) => new(
        id: Guid.NewGuid(),
        type: type,
        content: "{\"shipmentId\":\"x\"}",
        occurredOnUtc: DateTime.UtcNow,
        partitionKey: system,
        relatedOrderId: orderId,
        relatedTripId: tripId);

    [Fact]
    public async Task Supersedes_AllPendingRowsForOrder_AcrossRetryChainSiblings()
    {
        await using var db = NewDb();
        var orderId = Guid.NewGuid();
        // Two pending started rows for the same order — different trips (two
        // attempts in the retry chain, same shipmentId at OMS).
        var attempt1 = Row(Oms, Started, orderId, Guid.NewGuid());
        var attempt2 = Row(Oms, Started, orderId, Guid.NewGuid());
        db.OutboxMessages.AddRange(attempt1, attempt2);
        await db.SaveChangesAsync();

        var retired = await NewSut(db).SupersedePendingAsync(Oms, Started, orderId, CancellationToken.None);

        retired.Should().Be(2);
        var rows = await db.OutboxMessages.ToListAsync();
        rows.Should().OnlyContain(r => r.ProcessedOnUtc != null && r.NextRetryAtUtc == null);
        rows.Should().OnlyContain(r => r.RetryCount == 0);   // superseding is not a failed attempt
        rows.Should().OnlyContain(r => r.Error == "[superseded] manual resend delivered");
    }

    [Fact]
    public async Task LeavesAlone_OtherOrders_OtherSystems_OtherEventTypes()
    {
        await using var db = NewDb();
        var orderId = Guid.NewGuid();
        var target = Row(Oms, Started, orderId, Guid.NewGuid());
        var otherOrder = Row(Oms, Started, Guid.NewGuid(), Guid.NewGuid());
        var otherSystem = Row("erp", Started, orderId, Guid.NewGuid());
        var otherEvent = Row(Oms, "shipment.arrived.v1", orderId, Guid.NewGuid());
        db.OutboxMessages.AddRange(target, otherOrder, otherSystem, otherEvent);
        await db.SaveChangesAsync();

        var retired = await NewSut(db).SupersedePendingAsync(Oms, Started, orderId, CancellationToken.None);

        retired.Should().Be(1);
        (await db.OutboxMessages.FindAsync(target.Id))!.ProcessedOnUtc.Should().NotBeNull();
        (await db.OutboxMessages.FindAsync(otherOrder.Id))!.ProcessedOnUtc.Should().BeNull();
        (await db.OutboxMessages.FindAsync(otherSystem.Id))!.ProcessedOnUtc.Should().BeNull();
        (await db.OutboxMessages.FindAsync(otherEvent.Id))!.ProcessedOnUtc.Should().BeNull();
    }

    [Fact]
    public async Task Idempotent_SkipsRowsTheProcessorAlreadyTerminated()
    {
        await using var db = NewDb();
        var orderId = Guid.NewGuid();
        // The processor already hit OMS's 400 and marked this row terminal —
        // its red-card outcome is already emitted. Superseding must NOT touch
        // it (can't un-write the audit; would only clobber the Error).
        var alreadyFailed = Row(Oms, Started, orderId, Guid.NewGuid());
        alreadyFailed.MarkAsPermanentlyFailed(DateTime.UtcNow, "[permanent 400] duplicate");
        db.OutboxMessages.Add(alreadyFailed);
        await db.SaveChangesAsync();

        var retired = await NewSut(db).SupersedePendingAsync(Oms, Started, orderId, CancellationToken.None);

        retired.Should().Be(0);
        (await db.OutboxMessages.FindAsync(alreadyFailed.Id))!
            .Error.Should().Be("[permanent 400] duplicate", "processor-terminated rows are left as-is");
    }

    [Fact]
    public async Task NoPendingRows_ReturnsZero()
    {
        await using var db = NewDb();

        var retired = await NewSut(db).SupersedePendingAsync(
            Oms, Started, Guid.NewGuid(), CancellationToken.None);

        retired.Should().Be(0);
    }
}
