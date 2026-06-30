using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.OmsAdapter.Infrastructure.Services;

internal sealed class OmsCallbackTargetResolver : IOmsCallbackTargetResolver
{
    private readonly CachedCredentialReader _credReader;
    private readonly UpstreamOmsOptions _envFallback;
    private readonly ILogger<OmsCallbackTargetResolver> _logger;

    public OmsCallbackTargetResolver(
        CachedCredentialReader credReader,
        IOptions<UpstreamOmsOptions> envFallback,
        ILogger<OmsCallbackTargetResolver> logger)
    {
        _credReader = credReader;
        _envFallback = envFallback.Value;
        _logger = logger;
    }

    public async Task<OmsCallbackTarget?> ResolveAsync(string systemKey, CancellationToken cancellationToken)
    {
        // 1. UI source — iam.SystemCredentials.CallbackBaseUrl (preferred)
        var cred = await _credReader.GetAsync(systemKey, cancellationToken);
        if (cred is not null && !string.IsNullOrWhiteSpace(cred.CallbackBaseUrl))
        {
            var token = ExtractBearerToken(cred.CallbackAuthConfig);
            var timeout = cred.CallbackTimeoutMs > 0
                ? TimeSpan.FromMilliseconds(cred.CallbackTimeoutMs)
                : TimeSpan.FromSeconds(10);
            return new OmsCallbackTarget(cred.CallbackBaseUrl, token, timeout);
        }

        // 2. Backward-compat env fallback — UpstreamOms__BaseUrl + BearerToken.
        // Logs once at debug so ops can see "we're still on env-based config"
        // without spamming on every callback.
        if (!string.IsNullOrWhiteSpace(_envFallback.BaseUrl))
        {
            _logger.LogDebug(
                "[OmsTargetResolver] Using env fallback for '{SystemKey}' — move CallbackBaseUrl to iam.SystemCredentials via the admin UI to retire UpstreamOms__BaseUrl.",
                systemKey);
            return new OmsCallbackTarget(
                _envFallback.BaseUrl,
                string.IsNullOrWhiteSpace(_envFallback.BearerToken) ? null : _envFallback.BearerToken,
                TimeSpan.FromSeconds(_envFallback.TimeoutSeconds > 0 ? _envFallback.TimeoutSeconds : 10));
        }

        return null;
    }

    /// <summary>
    /// Pulls the bearer token out of the credential's outbound auth config
    /// jsonb. Today only the <c>bearer</c> scheme is wired; if the admin set
    /// <c>{"token":"..."}</c> we return it, otherwise null (no auth header).
    /// </summary>
    private static string? ExtractBearerToken(string? authConfigJson)
    {
        if (string.IsNullOrWhiteSpace(authConfigJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(authConfigJson);
            if (doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch (JsonException)
        {
            // Malformed jsonb shouldn't take down outbound — caller will see
            // 401 from upstream OMS instead of crash here.
        }
        return null;
    }
}
