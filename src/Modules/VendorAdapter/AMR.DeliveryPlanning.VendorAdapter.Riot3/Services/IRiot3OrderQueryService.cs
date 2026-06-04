using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

public interface IRiot3OrderQueryService
{
    // Returns null when RIOT3 has no record of this upperKey (404). All
    // other HTTP errors are surfaced as exceptions — callers (the
    // reconciler) log & skip the trip for this tick.
    Task<Riot3OrderQueryData?> GetOrderByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default);
}
