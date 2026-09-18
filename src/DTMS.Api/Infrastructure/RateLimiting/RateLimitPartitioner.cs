using DTMS.Api.Auth;
using DTMS.Api.Middlewares;
using DTMS.Iam.Application.Authorization;
using Microsoft.AspNetCore.Http;

namespace DTMS.Api.Infrastructure.RateLimiting;

public enum TrafficClass
{
    /// <summary>Health, metrics, SignalR hubs, JWKS. Never limited.</summary>
    Infra,

    /// <summary>Vendor webhooks. Never limited: a rejected frame is lost for good.</summary>
    Webhook,

    System,
    AttachmentImage,
    User,
    Anonymous,
}

/// <summary>Which quota a request spends. <see cref="Key"/> is empty for classes
/// that share one partition.</summary>
public readonly record struct RateLimitPartitionKey(TrafficClass Class, string Key);

/// <summary>
/// Decides whose quota a request spends. Pure — no I/O — so every rule is unit
/// tested.
///
/// <para><b>Why identity, not IP.</b> Every browser request reaches the API
/// through the Next server and RIOT3 sends every webhook from one server, so
/// the TCP peer address names a proxy, never the caller. Keyed by IP, all users
/// shared one quota and vendor webhooks competed with them for it.</para>
///
/// <para><b>This must run after authentication</b> — after both the user JWT
/// scheme and <see cref="SystemClientAuthMiddleware"/>, which is the only thing
/// that identifies a system client. Earlier, every request looks anonymous.</para>
/// </summary>
public static class RateLimitPartitioner
{
    /// <summary>Where the resolved key is kept for the rest of the request, so
    /// the limiter, the rejection handler and the metrics all agree on it.</summary>
    public const string ItemKey = "dtms.ratelimit.partition";

    public static RateLimitPartitionKey Resolve(HttpContext ctx)
    {
        var path = ctx.Request.Path;

        if (path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/metrics") ||
            path.StartsWithSegments("/hubs") ||
            path.StartsWithSegments("/.well-known"))
            return new(TrafficClass.Infra, string.Empty);

        if (path.StartsWithSegments("/api/webhooks"))
            return new(TrafficClass.Webhook, string.Empty);

        if (ctx.Items[SystemClientAuthMiddleware.PrincipalItemKey] is SystemPrincipal system)
            return new(TrafficClass.System, system.PrincipalId);

        var caller = CallerKey(ctx);

        // Many images per page, each cheap and then cached by the browser, so a
        // quota of their own keeps a photo table from spending a user's budget
        // for everything else. The suffixes match ImageRoutes in AttachmentEndpoints.
        if (path.StartsWithSegments("/api/v1/fleet/attachments") &&
            (path.Value!.EndsWith("/thumbnail", StringComparison.Ordinal) ||
             path.Value!.EndsWith("/image", StringComparison.Ordinal)))
            return new(TrafficClass.AttachmentImage, caller);

        return caller.StartsWith("user:", StringComparison.Ordinal)
            ? new(TrafficClass.User, caller)
            : new(TrafficClass.Anonymous, caller);
    }

    /// <summary>The key resolved earlier in this request, or a fresh resolution.</summary>
    public static RateLimitPartitionKey For(HttpContext ctx)
        => ctx.Items[ItemKey] is RateLimitPartitionKey key ? key : Resolve(ctx);

    private static string CallerKey(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var userId = ctx.User.ResolveUserId();
            if (!string.IsNullOrWhiteSpace(userId))
                return $"user:{userId}";
        }

        return $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
