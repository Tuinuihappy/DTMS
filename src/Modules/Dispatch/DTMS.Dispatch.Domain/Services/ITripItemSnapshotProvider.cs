using DTMS.Dispatch.IntegrationEvents;

namespace DTMS.Dispatch.Domain.Services;

// Phase P5.3 — Bounded-context bridge. Dispatch needs to enrich
// TripStartedDomainEvent with the snapshot of items bound to a trip,
// but the Items aggregate lives in the DeliveryOrder context. The
// implementation (DeliveryOrder.Infrastructure) queries the read-side
// of DeliveryOrder; Dispatch only sees this interface.
//
// Returns an empty list when the trip has no bound items yet —
// never null, never throws on "not found". Vendor adapter callers
// pass the result directly into Trip.MarkVendorStarted(items: ...).
public interface ITripItemSnapshotProvider
{
    Task<IReadOnlyList<TripItemSnapshot>> GetForTripAsync(Guid tripId, CancellationToken cancellationToken);
}
