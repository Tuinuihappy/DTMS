using DTMS.Iam.Application.Callbacks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// <see cref="ISourceCallbackOutboxSuperseder"/> over <see cref="OutboxDbContext"/>.
/// Lives in DTMS.Api because that is the only assembly that owns the outbox
/// DbContext (same reason the fan-out consumers do).
///
/// <para>Loads the matching pending rows and marks each via
/// <c>OutboxMessage.MarkAsSuperseded</c> rather than issuing a bulk
/// <c>ExecuteUpdate</c>: it keeps the row-state invariant (mutations only
/// through the entity's Mark* methods) and stays testable on the EF InMemory
/// provider. The <c>ProcessedOnUtc == null</c> filter is the idempotency /
/// race guard — a row the processor already terminated (delivered, or failed
/// and audited) is left exactly as the processor left it.</para>
/// </summary>
public sealed class SourceCallbackOutboxSuperseder : ISourceCallbackOutboxSuperseder
{
    private readonly OutboxDbContext _db;
    private readonly ILogger<SourceCallbackOutboxSuperseder> _log;

    public SourceCallbackOutboxSuperseder(
        OutboxDbContext db, ILogger<SourceCallbackOutboxSuperseder> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<int> SupersedePendingAsync(
        string systemKey, string eventType, Guid orderId, CancellationToken ct)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.PartitionKey == systemKey
                     && m.Type == eventType
                     && m.RelatedOrderId == orderId
                     && m.ProcessedOnUtc == null)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var row in pending)
            row.MarkAsSuperseded(now, "[superseded] manual resend delivered");

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "[OutboxSupersede] Retired {Count} pending {EventType} row(s) for order {OrderId} → {System} after manual resend delivered.",
            pending.Count, eventType, orderId, systemKey);

        return pending.Count;
    }
}
