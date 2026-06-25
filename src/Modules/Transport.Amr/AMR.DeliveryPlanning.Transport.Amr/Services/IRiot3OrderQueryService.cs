using AMR.DeliveryPlanning.Transport.Amr.Models;

namespace AMR.DeliveryPlanning.Transport.Amr.Services;

public interface IRiot3OrderQueryService
{
    // Returns null when RIOT3 has no record of this upperKey (404). All
    // other HTTP errors are surfaced as exceptions — callers (the
    // reconciler) log & skip the trip for this tick.
    Task<Riot3OrderQueryData?> GetOrderByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the full raw response body for an order — used to capture
    /// Trip.VendorFinalSnapshot as the authoritative forensic record
    /// (independent of any vendor schema drift in the parsed model).
    /// Returns null when RIOT3 has no record (404), empty string when
    /// the body is empty.
    /// </summary>
    Task<string?> GetRawByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default);
}
