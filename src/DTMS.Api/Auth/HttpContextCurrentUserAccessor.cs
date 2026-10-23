namespace DTMS.Api.Auth;

/// <summary>
/// Resolves the authenticated principal via <see cref="IHttpContextAccessor"/>.
///
/// <para><b>Claim resolution</b> is <see cref="ClaimsPrincipalExtensions.ResolveUserId"/> —
/// the same chain the audit trail and the per-user rate limit use, so every
/// stamp names the same person. Real External Auth LDAP tokens carry the
/// employee id in <c>sub</c> only (no <c>EmployeeId</c>, no name claim);
/// this accessor used to stop at <c>EmployeeId</c> ?? <c>Identity.Name</c>
/// and so returned null for every real user — CreatedBy on internal orders
/// and order/action templates, a trip cancel's ActionBy, and the resend
/// attribution were all blank or "system" (found 2026-10-23). System
/// principals resolve through <c>Identity.Name</c> (SystemClient.DisplayName,
/// stamped by SystemClientAuthMiddleware) before <c>sub</c> is reached.</para>
/// </summary>
// Single implementation of the shared ICurrentUserAccessor. DeliveryOrder and
// Planning used to declare an identical copy each, to avoid a cross-module
// reference; the interface moved to SharedKernel when Dispatch became the third
// module to need it, so there is now one interface and one registration.
public sealed class HttpContextCurrentUserAccessor : DTMS.SharedKernel.Auth.ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentUserName()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;

        var name = user.ResolveUserId();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
