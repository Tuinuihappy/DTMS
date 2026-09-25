namespace DTMS.SharedKernel.Auth;

/// <summary>
/// Resolves the authenticated principal's display name for audit fields
/// (e.g. <c>createdBy</c>). Backed by <c>HttpContext.User</c> in the HTTP
/// host (EmployeeId claim first, Identity.Name as fallback — see
/// <c>HttpContextCurrentUserAccessor</c>); tests can substitute a fake.
///
/// <para>Lives in SharedKernel because three modules now need it
/// (DeliveryOrder, Planning, Dispatch). It previously existed as an
/// identical per-module copy in the first two, with a note to consolidate
/// here once a third module wanted the same accessor — this is that
/// consolidation. Sits beside <see cref="ActorContext"/> and
/// <see cref="ICurrentActorContext"/>, the other auth abstractions the
/// modules share.</para>
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
