namespace DTMS.Planning.Application.Services;

// Vendor-agnostic seam for asking "does an order with this upperKey exist?".
// Mirrors IRobotOrderDispatcher: Planning.Application owns the shape, the API
// host wires a Riot3 adapter at composition time, so Planning takes no
// dependency on a specific vendor (enforced by DTMS.ArchitectureTests).
//
// Used to resolve the in-doubt case: the dispatch call timed out and we do
// not know whether the vendor created an order. Since RIOT3 does not
// de-duplicate, guessing wrong in the "retry" direction means a second robot
// really moves — so the three states below must stay distinct.
public interface IRobotOrderStatusQuery
{
    Task<RobotOrderPresence> CheckAsync(string upperKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Three-state answer. <see cref="Unknown"/> exists specifically so a failed
/// lookup is never mistaken for <see cref="NotFound"/> — collapsing those two
/// would let a timed-out dispatch be retried into a duplicate.
/// </summary>
public enum RobotOrderPresence
{
    /// <summary>Vendor confirms an order with this upperKey — do NOT re-send.</summary>
    Exists,
    /// <summary>Vendor confirms no such order — safe to retry.</summary>
    NotFound,
    /// <summary>Could not ask (vendor unreachable/error) — decide conservatively.</summary>
    Unknown
}
