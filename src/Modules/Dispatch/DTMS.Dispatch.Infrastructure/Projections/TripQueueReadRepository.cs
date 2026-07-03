using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Projections;

/// <summary>
/// EF-backed implementation of the operator Trips list. Joins
/// dispatch.Trips against dispatch.TripItems (left, one row per trip)
/// to surface a human-readable OrderRef without N+1 round trips. Sorts
/// + paginates server-side; total count is returned alongside the page
/// so the UI can render "page X of N" without a second request.
/// </summary>
public class TripQueueReadRepository : ITripQueueReadRepository
{
    private readonly DispatchDbContext _db;

    public TripQueueReadRepository(DispatchDbContext db) => _db = db;

    public async Task<TripQueuePage> SearchAsync(TripQueueFilter filter, CancellationToken cancellationToken = default)
    {
        // Phase 3b — eagerly load AmrExtension so the row mapper below
        // can read VendorOrderKey / VendorVehicleKey / VendorVehicleName.
        // Manual / Fleet trips materialise with AmrExtension = null,
        // which the delegating properties on Trip turn into null DTO fields.
        var query = _db.Trips.AsNoTracking().Include(t => t.AmrExtension).AsQueryable();

        if (filter.Statuses.Count > 0)
        {
            var statuses = filter.Statuses.ToHashSet();
            query = query.Where(t => statuses.Contains(t.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.VehicleKey))
        {
            var key = filter.VehicleKey;
            // Vendor vehicle key lives on the AMR extension (Phase 3b).
            // Filtering by it is implicitly AMR-only — Manual / Fleet
            // trips with no extension never match.
            query = query.Where(t => t.AmrExtension != null && t.AmrExtension.VendorVehicleKey == key);
        }

        if (filter.FromUtc.HasValue)
        {
            var from = filter.FromUtc.Value;
            query = query.Where(t => t.CreatedAt >= from);
        }
        if (filter.ToUtc.HasValue)
        {
            var to = filter.ToUtc.Value;
            query = query.Where(t => t.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search;
            // Match UpperKey, VendorOrderKey, or any TripItems.OrderRef on
            // this trip. OrderRef matches use EXISTS so the projection
            // table is hit by its existing (TripId) index.
            query = query.Where(t =>
                EF.Functions.ILike(t.UpperKey, $"%{s}%")
                || (t.AmrExtension != null && t.AmrExtension.VendorOrderKey != null
                    && EF.Functions.ILike(t.AmrExtension.VendorOrderKey, $"%{s}%"))
                || _db.TripItems.Any(i => i.TripId == t.Id && EF.Functions.ILike(i.OrderRef, $"%{s}%")));
        }

        // Total before paging — drives pagination UI.
        var total = await query.CountAsync(cancellationToken);

        query = ApplySort(query, filter.SortBy, filter.SortDescending);

        var skip = (filter.Page - 1) * filter.PageSize;

        // LEFT JOIN — TripItems may not be populated yet for very fresh
        // trips (the projector lags TripStartedIntegrationEvent by a few
        // hundred ms). Show OrderRef when available; null otherwise.
        var rows = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .Select(t => new
            {
                Trip = t,
                OrderRef = _db.TripItems
                    .Where(i => i.TripId == t.Id)
                    .Select(i => i.OrderRef)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new TripQueueItem(
                r.Trip.Id,
                r.Trip.DeliveryOrderId,
                r.OrderRef,
                r.Trip.JobId,
                r.Trip.VehicleId,
                r.Trip.VendorVehicleKey,
                r.Trip.VendorVehicleName,
                r.Trip.ClaimedByOperatorId,
                r.Trip.Status,
                r.Trip.AttemptNumber,
                r.Trip.PreviousAttemptId,
                r.Trip.UpperKey,
                r.Trip.VendorOrderKey,
                r.Trip.TemplateNameAtDispatch,
                r.Trip.PriorityAtDispatch,
                r.Trip.CreatedAt,
                r.Trip.StartedAt,
                r.Trip.CompletedAt,
                r.Trip.VendorExpectedCompletionAt,
                r.Trip.FailureReason,
                r.Trip.PickupStationId,
                r.Trip.DropStationId))
            .ToList();

        return new TripQueuePage(items, total);
    }

    private static IQueryable<Domain.Entities.Trip> ApplySort(
        IQueryable<Domain.Entities.Trip> q, TripQueueSort sort, bool desc)
    {
        return (sort, desc) switch
        {
            (TripQueueSort.StartedAt, true)      => q.OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.CreatedAt),
            (TripQueueSort.StartedAt, false)     => q.OrderBy(t => t.StartedAt).ThenBy(t => t.CreatedAt),
            (TripQueueSort.CompletedAt, true)    => q.OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.CreatedAt),
            (TripQueueSort.CompletedAt, false)   => q.OrderBy(t => t.CompletedAt).ThenBy(t => t.CreatedAt),
            (TripQueueSort.AttemptNumber, true)  => q.OrderByDescending(t => t.AttemptNumber).ThenByDescending(t => t.CreatedAt),
            (TripQueueSort.AttemptNumber, false) => q.OrderBy(t => t.AttemptNumber).ThenBy(t => t.CreatedAt),
            (TripQueueSort.Status, true)         => q.OrderByDescending(t => t.Status).ThenByDescending(t => t.CreatedAt),
            (TripQueueSort.Status, false)        => q.OrderBy(t => t.Status).ThenBy(t => t.CreatedAt),
            (TripQueueSort.Priority, true)       => q.OrderByDescending(t => t.PriorityAtDispatch).ThenByDescending(t => t.CreatedAt),
            (TripQueueSort.Priority, false)      => q.OrderBy(t => t.PriorityAtDispatch).ThenBy(t => t.CreatedAt),
            (_, true)                            => q.OrderByDescending(t => t.CreatedAt),
            (_, false)                           => q.OrderBy(t => t.CreatedAt),
        };
    }
}
