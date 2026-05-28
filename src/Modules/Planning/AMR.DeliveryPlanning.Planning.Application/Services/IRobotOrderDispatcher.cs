using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

// Vendor-agnostic seam for sending a ResolvedOrder out to the robot fleet.
// Planning.Application owns the input shape; the API host project wires up
// a Riot3 adapter at composition time so Planning doesn't take a hard
// dependency on a specific vendor.
public interface IRobotOrderDispatcher
{
    // Returns the vendor-side order key on success so the caller can
    // correlate later callbacks back to the DTMS upperKey it supplied.
    Task<Result<string>> SendAsync(
        string upperKey,
        ResolvedOrder order,
        CancellationToken cancellationToken = default);
}
