using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Queries.GetPoolSummary;
using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Adapters;

// WMS PR-4b (PR-G) — Dispatcher pool summary. Lives in DTMS.Api because
// it joins across module boundaries (dispatch.Trips + transportmanual.Operators);
// mirrors how PoolTripsQueryHandler is placed in the composition root.
//
// Query cost: two indexed COUNTs + one MIN(DispatchedAt) over the
// partial index. Runs in ~1 ms on the current dataset; safe to poll at
// dispatcher tab refresh rate (~5 s).
internal sealed class PoolSummaryQueryHandler
    : IQueryHandler<GetPoolSummaryQuery, PoolSummaryDto>
{
    private readonly DispatchDbContext _dispatch;
    private readonly TransportManualDbContext _manual;

    public PoolSummaryQueryHandler(
        DispatchDbContext dispatch,
        TransportManualDbContext manual)
    {
        _dispatch = dispatch;
        _manual = manual;
    }

    public async Task<Result<PoolSummaryDto>> Handle(
        GetPoolSummaryQuery request, CancellationToken cancellationToken)
    {
        // Pool aggregates — one query, one index scan (IX_Trips_Pool covers
        // the WHERE + supplies the ordered DispatchedAt for MIN).
        var poolAggregate = await _dispatch.Trips
            .AsNoTracking()
            .Where(t => t.Status == TripStatus.Created
                     && t.DispatchedAt != null
                     && t.ClaimedByOperatorId == null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Depth = g.Count(),
                OldestDispatched = g.Min(t => t.DispatchedAt!.Value),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var poolDepth = poolAggregate?.Depth ?? 0;
        double? oldestWaited = null;
        if (poolAggregate is not null)
            oldestWaited = (DateTime.UtcNow - poolAggregate.OldestDispatched).TotalSeconds;

        // Claimed / in-flight count — uses the second partial index
        // IX_Trips_ClaimedByOperatorId_Active added in PR-A.
        var claimedInFlight = await _dispatch.Trips
            .AsNoTracking()
            .Where(t => t.ClaimedByOperatorId != null
                     && (t.Status == TripStatus.InProgress || t.Status == TripStatus.Paused))
            .CountAsync(cancellationToken);

        // Active operator count — potential claimants. Not the same as
        // "currently connected operator PWAs" (that would need a SignalR
        // presence scan, deferred until we build the offline-push story).
        var activeOperators = await _manual.Operators
            .AsNoTracking()
            .Where(o => o.Status == DTMS.Transport.Manual.Domain.Enums.OperatorStatus.Active)
            .CountAsync(cancellationToken);

        return Result<PoolSummaryDto>.Success(new PoolSummaryDto(
            PoolDepth: poolDepth,
            OldestWaitedSeconds: oldestWaited,
            ActiveOperators: activeOperators,
            ClaimedInFlight: claimedInFlight));
    }
}
