namespace DTMS.Api.Auth;

/// <summary>
/// Resolves the authenticated principal via <see cref="IHttpContextAccessor"/>.
///
/// <para><b>Claim resolution order:</b>
/// <list type="number">
///   <item><c>EmployeeId</c> — the short claim External Auth actually
///   emits (matches the pattern IamEndpoints.ActorOrUnknown uses).</item>
///   <item><c>User.Identity.Name</c> — populated from whatever URI
///   <c>NameClaimType</c> points at (WS-Federation URI in Program.cs).
///   Non-null when the JWT contains that specific URI claim, which
///   External Auth doesn't emit but dev-bypass tokens do.</item>
///   <item>System principals — the <c>ClaimTypes.Name</c> claim stamped
///   by SystemClientAuthMiddleware, holding SystemClient.DisplayName.</item>
/// </list>
/// This ordering fixed a P4 regression where orders created via UI got
/// null CreatedBy/RequestedBy because Identity.Name resolves to null for
/// real External Auth JWTs.</para>
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

        // External Auth carries EmployeeId as its short custom claim
        // (the WS-Federation URI is aspirational — mirror what the IDP
        // actually emits). Identity.Name is the fallback used by dev-
        // bypass tokens and system principals (SystemClientAuthMiddleware
        // stamps ClaimTypes.Name = SystemClient.DisplayName).
        var name = user.FindFirst("EmployeeId")?.Value
                   ?? user.Identity?.Name;

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
