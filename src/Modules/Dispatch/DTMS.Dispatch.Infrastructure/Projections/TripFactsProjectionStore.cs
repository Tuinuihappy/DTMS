using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DTMS.Dispatch.Infrastructure.Projections;

public class TripFactsProjectionStore : ITripFactsProjectionStore
{
    private const string Table = $"{DispatchDbContext.BiSchema}.\"TripFacts\"";

    private const string PauseSql = $"""
        UPDATE {Table}
           SET "PauseCount"    = "PauseCount" + 1,
               "FirstPausedAt" = COALESCE("FirstPausedAt", @at),
               "FinalStatus"   = @status,
               "UpdatedAt"     = @at
         WHERE "TripId" = @tripId;
        """;

    private const string ReflavourSql = $"""
        UPDATE {Table}
           SET "FinalStatus" = @status,
               "UpdatedAt"   = @at
         WHERE "TripId" = @tripId;
        """;

    private readonly DispatchDbContext _db;

    public TripFactsProjectionStore(DispatchDbContext db) => _db = db;

    private static NpgsqlParameter TripIdParam(Guid tripId) =>
        new("tripId", NpgsqlDbType.Uuid) { Value = tripId };

    private static NpgsqlParameter AtParam(DateTime at) =>
        new("at", NpgsqlDbType.TimestampTz) { Value = at };

    private static NpgsqlParameter StatusParam(string status) =>
        new("status", NpgsqlDbType.Varchar) { Value = status };

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, ct);

    public async Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        // DispatchDbContext has EnableRetryOnFailure, and EF refuses an
        // explicit transaction outside the execution strategy.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // The strategy re-runs this whole lambda after a rollback. Entities
            // the failed attempt tracked are still queued, so without clearing
            // them the retry would insert each one twice.
            _db.ChangeTracker.Clear();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await work();
            await tx.CommitAsync(ct);
        });
    }

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureRowAsync(
        Guid tripId, DateTime occurredAt,
        Guid? deliveryOrderId, Guid? jobId, CancellationToken ct)
    {
        var row = await _db.TripFacts.FirstOrDefaultAsync(r => r.TripId == tripId, ct);
        if (row is null)
        {
            // No TripCreated event exists — first event we see is the row's
            // birthday. Backfill SQL overrides CreatedAt for pre-P5.2 trips.
            _db.TripFacts.Add(TripFactsRow.Create(
                tripId, occurredAt, deliveryOrderId, jobId, finalStatus: "Created"));
        }
    }

    public async Task SetStartedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, Guid? vehicleId,
        string? vendorVehicleKey, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetStartedAt(at, deliveryOrderId, jobId, vehicleId, vendorVehicleKey);
    }

    public Task RecordPausedAsync(Guid tripId, DateTime at, CancellationToken ct)
        => RecordPausedAsync(tripId, at, "Paused", reflavour: false, ct);

    /// <summary>
    /// PauseCount is the one accumulating column in this table, so it is the
    /// one that cannot be computed in memory: api and outbox-worker both
    /// consume pause events, and `PauseCount += 1` on a loaded row let two of
    /// them read the same count and both write count+1, losing one silently.
    /// Postgres adds to the stored value under the row lock it holds for the
    /// statement, so concurrent pauses serialize and both land.
    ///
    /// <para>A re-flavour (Hang↔Held drift on an already-paused trip) moves
    /// the status label only — hence the second statement, which leaves the
    /// counter and FirstPausedAt alone.</para>
    ///
    /// <para>No row match is a no-op, exactly as the previous
    /// <c>Find(...)?.RecordPaused(...)</c> was: pause events arrive after the
    /// row exists, and a missing one means the trip was never projected.</para>
    /// </summary>
    public Task RecordPausedAsync(
        Guid tripId, DateTime at, string finalStatus, bool reflavour, CancellationToken ct)
        => _db.Database.ExecuteSqlRawAsync(
            reflavour ? ReflavourSql : PauseSql,
            [TripIdParam(tripId), AtParam(at), StatusParam(finalStatus)],
            ct);

    public async Task RecordResumedAsync(Guid tripId, DateTime at, CancellationToken ct)
        => (await Find(tripId, ct))?.RecordResumed(at);

    public async Task SetCompletedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, string? vendorUpperKey, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetCompletedAt(at, deliveryOrderId, jobId, vendorUpperKey);
    }

    public async Task SetVendorVehicleKeyAsync(
        Guid tripId, DateTime at, string vendorVehicleKey, CancellationToken ct)
    {
        // Row already exists by the time a backfill fires (it follows the
        // terminal event), but EnsureRow keeps us safe against bus reordering.
        await EnsureRowAsync(tripId, at, null, null, ct);
        var row = await Find(tripId, ct);
        row?.BackfillVendorVehicleKey(vendorVehicleKey, at);
    }

    public async Task SetFailedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetFailedAt(at, deliveryOrderId, jobId, vendorUpperKey, reason);
    }

    public async Task SetRejectedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetRejectedAt(at, deliveryOrderId, jobId, vendorUpperKey, reason);
    }

    public async Task SetCancelledAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetCancelledAt(at, deliveryOrderId, jobId, vendorUpperKey, reason);
    }

    // Check the change tracker BEFORE the database. EnsureRowAsync Adds a new
    // row for a trip whose first event we see (e.g. TripStarted), but that add
    // isn't persisted until MarkProcessedAsync's SaveChanges — so a LINQ query
    // to the DB wouldn't find it yet and Set* would silently no-op against a
    // null row (the bug that left VendorVehicleKey / StartedAt null on every
    // trip whose first projected event created the row). Local first fixes all
    // Set* callers at the source.
    private async Task<TripFactsRow?> Find(Guid tripId, CancellationToken ct)
        => _db.TripFacts.Local.FirstOrDefault(r => r.TripId == tripId)
           ?? await _db.TripFacts.FirstOrDefaultAsync(r => r.TripId == tripId, ct);
}
