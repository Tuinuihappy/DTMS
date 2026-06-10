using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Repositories;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a trip by the DTMS-side correlation key (UpperKey) RIOT3
    /// echoes back on every webhook.
    /// </summary>
    Task<Trip?> GetByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a trip by the vendor-side order key RIOT3 assigns at dispatch
    /// (Trip.VendorOrderKey). RIOT3's sub-task webhooks omit the parent
    /// `task` object — so UpperKey isn't carried — but `subTask.taskKey`
    /// carries the vendor's order key, which lets us correlate back to the
    /// owning Trip.
    /// </summary>
    Task<Trip?> GetByVendorOrderKeyAsync(string vendorOrderKey, CancellationToken cancellationToken = default);

    Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists envelope-dispatched trips still awaiting a terminal vendor
    /// callback (Status ∈ {Created, InProgress, Paused}). Used by the
    /// reconciliation poller to backfill missed webhooks. Trips older than
    /// <paramref name="staleCutoffUtc"/> are excluded — past that age the
    /// poller stops chasing them and ops takes over.
    /// </summary>
    Task<List<Trip>> GetInFlightEnvelopeTripsAsync(DateTime staleCutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every Trip belonging to a DeliveryOrder, regardless of
    /// status. Used by the cancel-cascade flow to find which trips to
    /// stop on the vendor side.
    /// </summary>
    Task<List<Trip>> GetByDeliveryOrderIdAsync(Guid deliveryOrderId, CancellationToken cancellationToken = default);

    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);
}
