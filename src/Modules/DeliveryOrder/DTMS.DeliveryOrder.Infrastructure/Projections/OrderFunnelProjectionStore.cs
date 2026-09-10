using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DTMS.DeliveryOrder.Infrastructure.Projections;

public class OrderFunnelProjectionStore : IOrderFunnelProjectionStore
{
    private const string Table = $"{DeliveryOrderDbContext.Schema}.\"OrderFunnelHourly\"";

    // Creates the hour's row if this is the first event to land in it.
    // DO NOTHING because a concurrent writer creating the same bucket is
    // expected, not exceptional — whoever loses simply proceeds to the
    // UPDATE below and finds the winner's row.
    private const string EnsureBucketSql = $"""
        INSERT INTO {Table}
            ("Id", "BucketHour", "Confirmed", "Dispatched", "InProgress",
             "Completed", "PartiallyCompleted", "Failed", "Cancelled",
             "Rejected", "Held", "Released")
        VALUES (@id, @bucket, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        ON CONFLICT ("BucketHour") DO NOTHING;
        """;

    // The only column names that may reach SQL. Everything below is built
    // from this array at type-init, so no caller string is ever interpolated
    // into a statement — which is also why ExecuteSqlRawAsync below receives
    // a prepared string rather than an interpolation (EF1002).
    private static readonly string[] Counters =
    [
        "Confirmed", "Dispatched", "InProgress", "Completed", "PartiallyCompleted",
        "Failed", "Cancelled", "Rejected", "Held", "Released",
    ];

    private static readonly Dictionary<string, string> IncrementSqlByStatus =
        Counters.ToDictionary(
            c => c,
            c => $"""
                  UPDATE {Table}
                     SET "{c}" = "{c}" + 1
                   WHERE "BucketHour" = @bucket;
                  """,
            StringComparer.Ordinal);

    private readonly DeliveryOrderDbContext _db;

    public OrderFunnelProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    /// <summary>
    /// Add one to the counter for <paramref name="status"/> in the hour
    /// bucket <paramref name="occurredOn"/> falls into.
    ///
    /// <para>The increment happens inside the database — <c>SET x = x + 1</c>,
    /// never a value this process read a moment ago. That matters because
    /// api and outbox-worker both consume these events, so two writers can
    /// touch the same bucket at once. The previous read-then-write version
    /// let them both read 5 and both write 6, losing a count with no
    /// exception and nothing in the log to notice.</para>
    ///
    /// <para>Postgres locks the row for the duration of the UPDATE, so
    /// concurrent increments serialize and both land. No retry, no
    /// concurrency token, no conflict to detect — the window they were
    /// racing through no longer exists.</para>
    /// </summary>
    public async Task IncrementAsync(
        string projectorName, Guid eventId, DateTime occurredOn, string status,
        CancellationToken cancellationToken = default)
    {
        // Align bucket to start-of-hour UTC. Truncating to hour precision
        // collapses sub-hour duplicates into the same row so the projection
        // stays compact even under burst traffic.
        var bucketHour = new DateTime(
            occurredOn.Year, occurredOn.Month, occurredOn.Day,
            occurredOn.Hour, 0, 0, DateTimeKind.Utc);

        var incrementSql = IncrementSqlByStatus.GetValueOrDefault(status);

        // Track the inbox row once, OUTSIDE the retry lambda. The execution
        // strategy re-runs the lambda after a rollback, and a second Add()
        // would queue a duplicate insert; a single tracked entity survives
        // the failed SaveChanges and is re-sent as-is.
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));

        // The DbContext has EnableRetryOnFailure, so an explicit transaction
        // must run through the execution strategy or EF refuses it outright.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Unknown status → no counter to touch (the projector warns at
            // its own boundary). Still mark the event processed so it isn't
            // reconsidered forever, and skip creating an all-zero row.
            if (incrementSql is not null)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    EnsureBucketSql,
                    [IdParam(), BucketParam(bucketHour)],
                    cancellationToken);

                await _db.Database.ExecuteSqlRawAsync(
                    incrementSql, [BucketParam(bucketHour)], cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }

    // Fresh parameter instances per attempt — Npgsql forbids reusing one
    // across commands, and the strategy may run the lambda more than once.
    private static NpgsqlParameter IdParam() =>
        new("id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() };

    private static NpgsqlParameter BucketParam(DateTime bucketHour) =>
        new("bucket", NpgsqlDbType.TimestampTz) { Value = bucketHour };

    /// <summary>
    /// The counter column a status writes to, or null when the status isn't
    /// one this projection counts (unknown statuses are ignored — the
    /// projector warns at its own boundary). Exposed so a test can pin the
    /// whitelist: only these names are ever built into a statement.
    /// </summary>
    internal static string? CounterColumn(string status) =>
        IncrementSqlByStatus.ContainsKey(status) ? status : null;
}
