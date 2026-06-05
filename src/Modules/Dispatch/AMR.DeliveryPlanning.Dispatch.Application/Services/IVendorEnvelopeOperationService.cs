using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Distinguishes the three vendor-side outcomes of an envelope-level
/// operation (cancel / pause / resume) so each handler can apply its
/// own policy — Cancel treats "no record" as graceful success, Pause /
/// Resume auto-reconcile the trip to Failed because the vendor's
/// missing state is a divergence that needs surfacing to the operator.
/// </summary>
public enum VendorOperationOutcome
{
    /// <summary>Vendor accepted the operation (HTTP 200, code "0").</summary>
    Accepted = 0,

    /// <summary>Vendor has no record of the upperKey — HTTP 404 or
    /// HTTP 200 with code E110014 ("order is empty"). Causes:
    /// vendor-side TTL purge, never-received dispatch, or post-terminal
    /// cleanup. The operator's intent for Cancel is met; Pause / Resume
    /// should escalate.</summary>
    NoVendorRecord = 1,

    /// <summary>Vendor rejected the operation with a non-zero, non-empty
    /// code — typically a business rule violation (e.g. trying to pause
    /// an already-finished order, vehicle offline, etc.).</summary>
    Rejected = 2
}

/// <summary>
/// Vendor-agnostic surface for envelope-level lifecycle operations
/// (cancel / pause / resume) against the RIOT3 upperKey. Implemented at
/// the composition root by a vendor-specific adapter so this assembly
/// stays free of RIOT3 references.
///
/// Returns the structured <see cref="VendorOperationOutcome"/> so each
/// handler can pick its own policy for "vendor has no record" — Cancel
/// treats it as graceful success, Pause/Resume escalate (Trip is
/// out-of-sync with vendor → mark Failed for compliance + clarity).
/// </summary>
public interface IVendorEnvelopeOperationService
{
    Task<Result<VendorOperationOutcome>> CancelAsync(string upperKey, CancellationToken cancellationToken = default);
    Task<Result<VendorOperationOutcome>> PauseAsync(string upperKey, CancellationToken cancellationToken = default);
    Task<Result<VendorOperationOutcome>> ResumeAsync(string upperKey, CancellationToken cancellationToken = default);
}
