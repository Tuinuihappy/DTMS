namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

/// <summary>
/// Resolves the authenticated principal's display name for audit fields
/// (e.g. <c>createdBy</c>). Backed by <c>HttpContext.User.Identity.Name</c>
/// in the HTTP host; tests can substitute a fake.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Returns the current principal's name claim, or <c>null</c> when the
    /// request has no authenticated user (e.g. background consumer that
    /// did not propagate a principal).
    /// </summary>
    string? GetCurrentUserName();
}
