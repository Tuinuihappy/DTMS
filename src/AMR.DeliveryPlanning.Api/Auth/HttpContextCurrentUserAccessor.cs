using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

namespace AMR.DeliveryPlanning.Api.Auth;

/// <summary>
/// Resolves the authenticated principal via <see cref="IHttpContextAccessor"/>.
/// Reads <c>User.Identity.Name</c> — populated from the JWT <c>name</c> claim
/// (per Program.cs auth config).
/// </summary>
public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
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
