using DTMS.Dispatch.Domain.Entities;

namespace DTMS.Dispatch.Domain.Repositories;

/// <summary>
/// Append-only audit log for trip retries. Write-only from the domain's
/// perspective; reads are for ops dashboards and compliance queries
/// (live outside the aggregate boundary).
/// </summary>
public interface ITripRetryEventRepository
{
    Task AddAsync(TripRetryEvent retryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every retry attempt logged against an original trip
    /// (ordered by attempt number ascending).
    /// </summary>
    Task<List<TripRetryEvent>> GetByOriginalTripIdAsync(Guid originalTripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every retry attempt for any trip belonging to a delivery
    /// order. Useful for the per-order operations view.
    /// </summary>
    Task<List<TripRetryEvent>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default);
}
