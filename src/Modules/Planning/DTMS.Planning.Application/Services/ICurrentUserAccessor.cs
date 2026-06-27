namespace DTMS.Planning.Application.Services;

/// <summary>
/// Resolves the authenticated principal's display name for audit fields
/// (e.g. <c>createdBy</c>). Backed by <c>HttpContext.User.Identity.Name</c>
/// in the HTTP host; tests can substitute a fake.
/// </summary>
// NOTE: DeliveryOrder.Application already defines an identical interface.
// Duplicated here to avoid a cross-module reference; consolidate into
// SharedKernel when a third module needs the same accessor.
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Returns the current principal's name claim, or <c>null</c> when the
    /// request has no authenticated user (e.g. background consumer that
    /// did not propagate a principal).
    /// </summary>
    string? GetCurrentUserName();
}
