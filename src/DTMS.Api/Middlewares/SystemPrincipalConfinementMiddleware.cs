using System.Text.Json;

namespace DTMS.Api.Middlewares;

/// <summary>
/// Authorization wall — confines system (machine-to-machine) principals to the
/// source data-plane.
///
/// <para>A system JWT (<c>sub = "system:{key}"</c>) is a valid principal across
/// the whole JwtBearer surface, and <c>PermissionClaimsTransformer</c> stamps
/// whatever permissions the system was granted. So without this wall, a system
/// that was ever granted an admin permission (e.g. <c>dtms:iam:system:write</c>)
/// — by mistake or over-scoping — could reach control-plane endpoints. This
/// middleware makes the split <b>structural, not permission-only</b>: an
/// authenticated system principal is rejected with 403 on every path outside
/// <c>/api/v1/source/*</c>, regardless of what it holds.</para>
///
/// <para>Runs right after authentication so <c>ctx.User</c> is populated. User
/// principals (no <c>system:</c> sub) and anonymous requests (no sub — e.g.
/// <c>/oauth/token</c>, health, JWKS) pass straight through.</para>
/// </summary>
public sealed class SystemPrincipalConfinementMiddleware : IMiddleware
{
    private const string SubjectPrefix = "system:";
    // The single surface a system principal is allowed to reach.
    private static readonly PathString SourcePrefix = new("/api/v1/source");
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sub = context.User.FindFirst("sub")?.Value;
        var isSystemPrincipal = sub is not null
            && sub.StartsWith(SubjectPrefix, StringComparison.Ordinal);

        if (isSystemPrincipal
            && !context.Request.Path.StartsWithSegments(
                   SourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new { error = "System principals are confined to /api/v1/source/*." },
                JsonOpts));
            return;
        }

        await next(context);
    }
}
