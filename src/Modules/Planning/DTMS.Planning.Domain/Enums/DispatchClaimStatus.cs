namespace DTMS.Planning.Domain.Enums;

// Lifecycle of one manual OrderTemplate dispatch attempt (see DispatchClaim).
// Deliberately NOT the order's lifecycle — the /create path is fire-and-forget
// and never tracks vendor-side progress. This only records what WE did.
public enum DispatchClaimStatus
{
    // Claim taken, vendor call not yet confirmed. A claim can stay here
    // forever when nobody retries — that is an accepted audit state, not a
    // leak (in-doubt resolution is inline-only, by design).
    InProgress = 0,
    Succeeded = 1,
    Failed = 2
}
