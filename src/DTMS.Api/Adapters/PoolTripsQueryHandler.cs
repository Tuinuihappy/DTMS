using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Projections;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Queries.GetPoolTrips;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Adapters;

// WMS PR-4b (PR-D) — Read-side handler for GET /api/operator/trips/pool.
//
// Lives in DTMS.Api/Adapters (composition root) rather than in the
// Transport.Manual module because the query joins across module
// boundaries — dispatch.Trips owns the pool signal (Status + DispatchedAt
// + ClaimedByOperatorId) while dispatch.TripItems (populated by
// TripItemsProjector at dispatch time via TripDispatchedIntegrationEventV1)
// carries the display context (order ref, pickup/drop codes, weights).
// Both live in DispatchDbContext so this is a single-context read.
//
// Ordering: DispatchedAt ASC (oldest first — FIFO fairness). Hits the
// partial index dispatch."IX_Trips_Pool" so the query is O(log n) even
// with millions of terminal trips in the table.
internal sealed class PoolTripsQueryHandler
    : IQueryHandler<GetPoolTripsQuery, IReadOnlyList<PoolTripDto>>
{
    private readonly DispatchDbContext _dispatch;

    public PoolTripsQueryHandler(DispatchDbContext dispatch)
    {
        _dispatch = dispatch;
    }

    public async Task<Result<IReadOnlyList<PoolTripDto>>> Handle(
        GetPoolTripsQuery request, CancellationToken cancellationToken)
    {
        // Pool predicate — matches IX_Trips_Pool partial index WHERE clause
        // so the planner picks the index without a seq scan.
        var poolTrips = await _dispatch.Trips
            .AsNoTracking()
            .Where(t => t.Status == TripStatus.Created
                     && t.DispatchedAt != null
                     && t.ClaimedByOperatorId == null)
            .OrderBy(t => t.DispatchedAt)
            .Select(t => new
            {
                t.Id,
                t.DeliveryOrderId,
                t.DispatchedAt,
                t.PriorityAtDispatch,
            })
            .ToListAsync(cancellationToken);

        if (poolTrips.Count == 0)
            return Result<IReadOnlyList<PoolTripDto>>.Success(Array.Empty<PoolTripDto>());

        var tripIds = poolTrips.Select(t => t.Id).ToList();

        // One-shot aggregation across the (per-trip) items projection.
        // GroupBy in EF Core 8 translates to SQL GROUP BY, so this is one
        // query, not N+1. Uses the natural (TripId) index on TripItemsRow.
        var itemSummaries = await _dispatch.Set<TripItemsRow>()
            .AsNoTracking()
            .Where(i => tripIds.Contains(i.TripId))
            .GroupBy(i => i.TripId)
            .Select(g => new
            {
                TripId = g.Key,
                ItemCount = g.Count(),
                TotalWeightKg = g.Sum(i => i.WeightKg ?? 0.0),
                OrderRef = g.Min(i => i.OrderRef)!,
                PickupCode = g.Min(i => i.PickupCode) ?? string.Empty,
                DropCode = g.Min(i => i.DropCode) ?? string.Empty,
            })
            .ToListAsync(cancellationToken);

        var byTripId = itemSummaries.ToDictionary(s => s.TripId);

        var dtos = poolTrips
            .Select(t =>
            {
                byTripId.TryGetValue(t.Id, out var s);
                return new PoolTripDto(
                    TripId: t.Id,
                    DeliveryOrderId: t.DeliveryOrderId,
                    OrderRef: s?.OrderRef ?? string.Empty,
                    PickupCode: s?.PickupCode ?? string.Empty,
                    DropCode: s?.DropCode ?? string.Empty,
                    ItemCount: s?.ItemCount ?? 0,
                    TotalWeightKg: s?.TotalWeightKg ?? 0.0,
                    DispatchedAt: t.DispatchedAt!.Value,
                    Priority: t.PriorityAtDispatch);
            })
            .ToList();

        return Result<IReadOnlyList<PoolTripDto>>.Success(dtos);
    }
}
