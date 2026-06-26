namespace AMR.DeliveryPlanning.Transport.Amr.Models;

/// <summary>
/// Outcome of a successful RIOT3 envelope dispatch. Carries both the
/// vendor-issued order key and the JSON payload DTMS actually sent —
/// the latter is captured on Trip.VendorRequestSnapshot for compliance /
/// forensic queries (vendor schema drift won't break the saved record).
/// </summary>
public sealed record Riot3DispatchResult(string OrderKey, string RequestJson);
