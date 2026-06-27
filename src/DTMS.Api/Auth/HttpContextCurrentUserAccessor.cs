namespace DTMS.Api.Auth;

/// <summary>
/// Resolves the authenticated principal via <see cref="IHttpContextAccessor"/>.
/// Reads <c>User.Identity.Name</c> — populated from the JWT <c>name</c> claim
/// (per Program.cs auth config).
/// </summary>
// Implements the per-module ICurrentUserAccessor interfaces. Each module
// declares its own copy to avoid cross-module references; the API host
// satisfies all of them with one implementation.
public sealed class HttpContextCurrentUserAccessor :
    DTMS.DeliveryOrder.Application.Services.ICurrentUserAccessor,
    DTMS.Planning.Application.Services.ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentUserName()
    {
        var name = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
