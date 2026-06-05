using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

// Vendor-agnostic seam for sending a ResolvedOrder out to the robot fleet.
// Planning.Application owns the input shape; the API host project wires up
// a Riot3 adapter at composition time so Planning doesn't take a hard
// dependency on a specific vendor.
public interface IRobotOrderDispatcher
{
    // Returns the vendor-side order key on success so the caller can
    // correlate later callbacks back to the DTMS upperKey it supplied,
    // plus the raw JSON payload DTMS actually transmitted so the caller
    // can persist it on Trip.VendorRequestSnapshot for forensic queries.
    Task<Result<RobotOrderDispatchResult>> SendAsync(
        string upperKey,
        ResolvedOrder order,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Vendor-agnostic dispatch outcome — the vendor's order key plus the
/// exact request payload DTMS sent (so callers can snapshot it as the
/// forensic record).
/// </summary>
public sealed record RobotOrderDispatchResult(string VendorOrderKey, string RequestJson);
