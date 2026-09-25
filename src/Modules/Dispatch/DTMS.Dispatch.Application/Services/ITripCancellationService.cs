using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Services;

/// <summary>
/// The one cancel implementation in Dispatch: apply the local transition, tell
/// the vendor to cancel the envelope (skipped when no <c>VendorOrderKey</c> was
/// ever minted), then persist — with the xmin conflict retry that keeps the
/// caller's reason when RIOT3's TASK_CANCELED echo wins the commit race.
///
/// <para>It lives behind an interface rather than inside a command handler
/// because more than one caller needs it: the operator console via
/// <c>CancelTripCommand</c>, and (next) the federated source-system cancel.
/// Cross-module callers such as <c>OrderCancelledCascadeConsumer</c> still come
/// in through <c>CancelTripCommand</c> over MediatR, which is the right
/// boundary — this service is Dispatch-internal.</para>
/// </summary>
public interface ITripCancellationService
{
    /// <summary>
    /// Returns failure and persists nothing when the domain rejects the
    /// transition or the vendor rejects the cancel. A vendor that has no record
    /// of the order counts as success — the intent is met either way.
    /// </summary>
    /// <param name="actionBy">Who cancelled, in the calling system's own terms.</param>
    /// <param name="actedAt">When they cancelled it upstream, if known.</param>
    /// <param name="origin">
    /// Which system asked. Recorded in the audit trail and kept out of
    /// <paramref name="reason"/>, because reason is what travels upstream.
    /// </param>
    Task<Result> CancelAsync(
        Guid tripId,
        string reason,
        string? actionBy = null,
        DateTime? actedAt = null,
        string? origin = null,
        CancellationToken cancellationToken = default);
}
