using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Dispatch.Infrastructure.Repositories;

public sealed class TripRetryEventRepository : ITripRetryEventRepository
{
    private readonly DispatchDbContext _context;

    public TripRetryEventRepository(DispatchDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TripRetryEvent retryEvent, CancellationToken cancellationToken = default)
    {
        await _context.TripRetryEvents.AddAsync(retryEvent, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<TripRetryEvent>> GetByOriginalTripIdAsync(Guid originalTripId, CancellationToken cancellationToken = default)
        => _context.TripRetryEvents
            .Where(e => e.OriginalTripId == originalTripId)
            .OrderBy(e => e.AttemptNumber)
            .ToListAsync(cancellationToken);

    public Task<List<TripRetryEvent>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default)
        => _context.TripRetryEvents
            .Where(e => e.DeliveryOrderId == deliveryOrderId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
}
