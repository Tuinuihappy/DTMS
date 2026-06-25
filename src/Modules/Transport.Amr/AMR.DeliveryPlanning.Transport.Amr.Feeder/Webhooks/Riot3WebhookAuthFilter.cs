using AMR.DeliveryPlanning.Transport.Amr.Feeder.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Transport.Amr.Feeder.Webhooks;

/// <summary>
/// Authenticates inbound RIOT3 webhooks via IP allowlist + URL-path
/// secret. Lives as an endpoint filter so the auth logic stays out of
/// the business path in <see cref="Riot3Webhooks"/>.
///
/// Behaviour by config (<see cref="Riot3WebhookOptions"/>):
///   RequireAuth = false  → log warnings on auth gaps, ALLOW through.
///                          Use during rollout to discover the actual
///                          remote IP the container sees.
///   RequireAuth = true   → enforce both gates. IP fail → 403,
///                          path-secret fail → 404 (hide endpoint
///                          existence from path scanners).
///
/// The filter reads options via IOptionsMonitor so config can be
/// hot-reloaded without restarting the API.
/// </summary>
public sealed class Riot3WebhookAuthFilter : IEndpointFilter
{
    private readonly IOptionsMonitor<Riot3WebhookOptions> _options;
    private readonly ILogger<Riot3WebhookAuthFilter> _logger;

    public Riot3WebhookAuthFilter(
        IOptionsMonitor<Riot3WebhookOptions> options,
        ILogger<Riot3WebhookAuthFilter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var opts = _options.CurrentValue;
        var http = ctx.HttpContext;
        var enforce = opts.RequireAuth;

        // ── IP allowlist check ───────────────────────────────────────────
        if (opts.AllowedIPs.Length > 0)
        {
            var remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? "(null)";
            // IPv4-mapped IPv6 ("::ffff:10.x.x.x") strip down so configs
            // can be written in plain dotted-quad form.
            if (remoteIp.StartsWith("::ffff:", StringComparison.Ordinal))
                remoteIp = remoteIp[7..];

            var ipOk = Array.Exists(opts.AllowedIPs, a => string.Equals(a, remoteIp, StringComparison.Ordinal));
            if (!ipOk)
            {
                if (enforce)
                {
                    _logger.LogWarning("[WebhookAuth] REJECTED: remoteIp={RemoteIp} not in allowlist (path={Path})",
                        remoteIp, http.Request.Path);
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                _logger.LogWarning("[WebhookAuth] WARN-ONLY: remoteIp={RemoteIp} not in allowlist (would 403 in enforce mode)",
                    remoteIp);
            }
        }
        else if (enforce)
        {
            // Enforce mode with empty allowlist is a misconfiguration.
            _logger.LogError("[WebhookAuth] RequireAuth=true but AllowedIPs is empty — refusing all webhook requests.");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // ── URL-path secret check ────────────────────────────────────────
        // Match against ANY entry in UrlSecrets so secrets can be rotated
        // without downtime (overlap window where both old + new accept).
        if (opts.UrlSecrets.Length > 0)
        {
            var routeSecret = ctx.HttpContext.Request.RouteValues["secret"]?.ToString();
            var pathOk = routeSecret is not null
                && Array.Exists(opts.UrlSecrets, s => string.Equals(s, routeSecret, StringComparison.Ordinal));
            if (!pathOk)
            {
                if (enforce)
                {
                    _logger.LogWarning("[WebhookAuth] REJECTED: path secret matched none of {Count} accepted (provided={Provided}, path={Path})",
                        opts.UrlSecrets.Length, routeSecret ?? "(missing)", http.Request.Path);
                    // 404 instead of 401/403 — don't confirm endpoint exists at this path.
                    return Results.NotFound();
                }
                _logger.LogWarning("[WebhookAuth] WARN-ONLY: path secret matched none of {Count} accepted (provided={Provided}, would 404 in enforce mode)",
                    opts.UrlSecrets.Length, routeSecret ?? "(missing)");
            }
        }

        return await next(ctx);
    }
}
