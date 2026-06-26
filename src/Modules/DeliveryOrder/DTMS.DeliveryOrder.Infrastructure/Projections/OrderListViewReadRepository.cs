using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P4 — read-side query against the OrderListView denormalized
/// table. Replaces the runtime <c>SearchAsync</c> implementation in
/// DeliveryOrderRepository — the API contract is identical (filters +
/// sort + page) so callers don't notice the swap.
///
/// Full-text search prefers Postgres GIN tsvector when the term has any
/// length; degenerate empty terms fall through to "no search filter"
/// for parity with the legacy ILIKE path.
/// </summary>
public class OrderListViewReadRepository : IOrderListViewReadRepository
{
    private readonly DeliveryOrderDbContext _db;

    public OrderListViewReadRepository(DeliveryOrderDbContext db) => _db = db;

    public async Task<(IReadOnlyList<OrderListViewEntry> Items, int TotalCount)> SearchAsync(
        OrderListViewFilters filters,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OrderListView.AsNoTracking().AsQueryable();

        if (filters.Status.HasValue)
            query = query.Where(r => r.Status == filters.Status.Value.ToString());

        if (filters.Bucket.HasValue)
        {
            var bucketStatuses = OrderStatusBuckets.For(filters.Bucket.Value)
                .Select(s => s.ToString())
                .ToArray();
            query = query.Where(r => bucketStatuses.Contains(r.Status));
        }

        if (filters.Priority.HasValue)
            query = query.Where(r => r.Priority == filters.Priority.Value.ToString());

        if (filters.TransportMode.HasValue)
            query = query.Where(r => r.TransportMode == filters.TransportMode.Value.ToString());

        if (filters.HasFailedTrip == true)
            query = query.Where(r => r.HasFailedTrip);

        if (filters.HasActiveJob == true)
            query = query.Where(r => r.HasActiveJob);

        if (filters.CreatedFromUtc.HasValue)
            query = query.Where(r => r.CreatedAt >= filters.CreatedFromUtc.Value);
        if (filters.CreatedToUtc.HasValue)
            query = query.Where(r => r.CreatedAt <= filters.CreatedToUtc.Value);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            // Postgres full-text via raw SQL: each whitespace-separated
            // token becomes a prefix-match term joined with AND. Defends
            // against operator characters that to_tsquery would parse
            // as ANDed terms or worse.
            var sanitized = SanitizeQuery(filters.Search);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                // Functional WHERE that the GIN index on SearchVector
                // backs. Npgsql can translate EF.Functions.ToTsQuery /
                // .Matches in newer providers, but raw SQL keeps this
                // independent of the provider version.
                query = query.Where(r =>
                    EF.Functions.ToTsVector("simple", r.SearchText)
                        .Matches(EF.Functions.ToTsQuery("simple", sanitized)));
            }
        }

        query = ApplySort(query, filters.SortBy, filters.SortDescending);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var entries = rows.Select(Map).ToList();
        return (entries, total);
    }

    private static IQueryable<OrderListViewRow> ApplySort(
        IQueryable<OrderListViewRow> query, string? sortBy, bool descending)
    {
        return (sortBy?.ToLowerInvariant(), descending) switch
        {
            ("orderref", false)       => query.OrderBy(r => r.OrderRef),
            ("orderref", true)        => query.OrderByDescending(r => r.OrderRef),
            ("priority", false)       => query.OrderBy(r => r.Priority),
            ("priority", true)        => query.OrderByDescending(r => r.Priority),
            ("status", false)         => query.OrderBy(r => r.Status),
            ("status", true)          => query.OrderByDescending(r => r.Status),
            ("totalweightkg", false)  => query.OrderBy(r => r.TotalWeightKg),
            ("totalweightkg", true)   => query.OrderByDescending(r => r.TotalWeightKg),
            ("updatedat", false)      => query.OrderBy(r => r.UpdatedAt),
            ("updatedat", true)       => query.OrderByDescending(r => r.UpdatedAt),
            (_, false)                => query.OrderBy(r => r.CreatedAt),
            _                         => query.OrderByDescending(r => r.CreatedAt),
        };
    }

    /// <summary>
    /// Make raw user input safe for to_tsquery. Strips characters that
    /// have special meaning in the tsquery grammar (&amp;, |, !, parens)
    /// and turns whitespace-separated tokens into prefix-AND search
    /// (<c>foo &amp; bar:*</c>).
    /// </summary>
    private static string SanitizeQuery(string raw)
    {
        var cleaned = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch == '-').ToArray());
        var tokens = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;
        return string.Join(" & ", tokens.Select(t => $"{t}:*"));
    }

    public async Task<DeliveryOrderStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        // Same shape as the write-side GetStatsAsync (DeliveryOrderRepository.cs:117)
        // but sourced from the projection so chip counts and table rows
        // see exactly the same universe of orders.
        var byStatusRaw = await _db.OrderListView
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = new Dictionary<OrderStatus, int>();
        foreach (var row in byStatusRaw)
        {
            if (Enum.TryParse<OrderStatus>(row.Status, out var s))
                byStatus[s] = row.Count;
        }
        var total = byStatus.Values.Sum();

        var totalWeight = total == 0
            ? 0d
            : await _db.OrderListView.AsNoTracking().SumAsync(r => r.TotalWeightKg, cancellationToken);

        return new DeliveryOrderStats(total, byStatus, totalWeight);
    }

    private static OrderListViewEntry Map(OrderListViewRow r) => new(
        r.OrderId, r.OrderRef,
        r.SourceSystem, r.Priority, r.Status,
        r.SubmittedAt, r.CreatedBy, r.RequestedBy, r.Notes,
        r.CreatedAt, r.UpdatedAt,
        r.TotalWeightKg, r.TotalQuantity, r.TotalItems,
        r.TransportMode,
        r.RequiresDropPod, r.RequiresPickupPod,
        r.ServiceWindowEarliestUtc, r.ServiceWindowLatestUtc,
        r.HasFailedTrip, r.HasActiveJob, r.LatestJobStatus);
}
