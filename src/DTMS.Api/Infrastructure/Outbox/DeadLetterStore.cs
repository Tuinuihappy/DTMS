using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase O3 — DLQ store implementation on the central
/// <see cref="OutboxDbContext"/>. See
/// <see cref="IDeadLetterStore"/> for the contract.
/// </summary>
public sealed class DeadLetterStore : IDeadLetterStore
{
    private readonly OutboxDbContext _db;
    private readonly IDeadLetterReplayRouter _router;
    private readonly ILogger<DeadLetterStore> _log;

    public DeadLetterStore(
        OutboxDbContext db,
        IDeadLetterReplayRouter router,
        ILogger<DeadLetterStore> log)
    {
        _db = db;
        _router = router;
        _log = log;
    }

    public async Task<bool> MoveAsync(
        OutboxMessage original,
        string source,
        DateTime firstFailedOnUtc,
        DateTime lastFailedOnUtc,
        CancellationToken ct = default)
    {
        // Fast idempotency short-circuit — if a prior partial success
        // already inserted the DLQ row (delete-side failed), skip the
        // insert entirely. Unique index would also catch it via
        // DbUpdateException; this just avoids the throw.
        var alreadyMoved = await _db.DeadLetterMessages
            .AsNoTracking()
            .AnyAsync(m => m.OriginalOutboxId == original.Id, ct);
        if (alreadyMoved)
        {
            _log.LogDebug("DLQ row for original {OriginalId} already present — no-op", original.Id);
            return false;
        }

        var dlq = new DeadLetterMessage(
            id: Guid.NewGuid(),
            originalOutboxId: original.Id,
            source: source,
            type: original.Type,
            content: original.Content,
            occurredOnUtc: original.OccurredOnUtc,
            firstFailedOnUtc: firstFailedOnUtc,
            lastFailedOnUtc: lastFailedOnUtc,
            retryCount: original.RetryCount,
            lastError: original.Error,
            traceParent: original.TraceParent);

        _db.DeadLetterMessages.Add(dlq);
        try
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation(
                "DLQ moved {OriginalId} from {Source} — type={Type}, retries={Retry}",
                original.Id, source, original.Type, original.RetryCount);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race with a concurrent processor tick — treat as no-op.
            _db.Entry(dlq).State = EntityState.Detached;
            _log.LogDebug("DLQ move raced (unique violation) on {OriginalId} — already there", original.Id);
            return false;
        }
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        int take, int skip, string? sourceFilter = null, CancellationToken ct = default)
    {
        var q = _db.DeadLetterMessages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sourceFilter))
            q = q.Where(m => m.Source == sourceFilter);

        return await q.OrderByDescending(m => m.LastFailedOnUtc)
                      .Skip(Math.Max(0, skip))
                      .Take(Math.Clamp(take, 1, 500))
                      .ToListAsync(ct);
    }

    public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken ct = default)
        => _db.DeadLetterMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<long> CountAsync(CancellationToken ct = default)
        => _db.DeadLetterMessages.LongCountAsync(ct);

    public async Task<bool> ReplayAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.DeadLetterMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return false;

        // Fresh OutboxMessage — new id (so a subsequent terminal failure
        // during replay lands as a new DLQ row rather than colliding on
        // the unique index against this DLQ entry, which stays until we
        // delete it after re-emit succeeds).
        var replay = new OutboxMessage(
            id: Guid.NewGuid(),
            type: row.Type,
            content: row.Content,
            occurredOnUtc: row.OccurredOnUtc,
            traceParent: row.TraceParent);

        // Insert into the origin module's OutboxMessages (LISTEN/NOTIFY
        // trigger fires there → drain immediately). Router throws if the
        // Source slug is unknown; that surfaces as a 500 to the admin
        // caller with a clear message.
        await _router.ReinsertAsync(row.Source, replay, ct);

        // Delete DLQ row only after the re-insert commits — if the
        // delete fails (e.g. server bounce mid-tx), admin re-runs the
        // endpoint and it's idempotent because the router insert will
        // race with a fresh Guid Id and the OutboxMessages table has no
        // uniqueness constraint we'd hit (the deduping unique index is
        // on the DLQ side, on OriginalOutboxId, which points to the
        // pre-replay row that no longer exists).
        _db.DeadLetterMessages.Remove(row);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("DLQ replayed {DlqId} → new outbox {NewId} in {Source}",
            id, replay.Id, row.Source);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.DeadLetterMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return false;

        _db.DeadLetterMessages.Remove(row);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("DLQ deleted {DlqId} (permanent)", id);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql exposes the SQLSTATE via PostgresException.SqlState.
        // 23505 = unique_violation. Match on that specifically rather
        // than string-sniffing the message.
        return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
    }
}
