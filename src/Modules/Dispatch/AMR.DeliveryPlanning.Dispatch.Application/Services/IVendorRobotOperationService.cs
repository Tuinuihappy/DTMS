using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Vendor-agnostic surface for robot-level operations targeting the vendor
/// deviceKey (Trip.VendorVehicleKey) — distinct from
/// <see cref="IVendorEnvelopeOperationService"/> which addresses the
/// orderKey. Implemented at the composition root by a vendor-specific
/// adapter so this assembly stays free of RIOT3 references.
///
/// Reuses <see cref="VendorOperationOutcome"/> for outcome semantics —
/// Accepted / NoVendorRecord / Rejected map cleanly across the two
/// operation surfaces.
///
/// Used by operator-initiated checkpoint acknowledgments (PASS) where
/// the robot is waiting on a human signal mid-trip; the Trip stays
/// InProgress throughout — PASS is a nudge, not a state transition.
/// </summary>
public interface IVendorRobotOperationService
{
    Task<Result<VendorOperationOutcome>> PassAsync(string vendorVehicleKey, CancellationToken cancellationToken = default);
}
