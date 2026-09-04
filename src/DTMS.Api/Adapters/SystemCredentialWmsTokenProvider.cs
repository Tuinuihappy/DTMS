using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Callbacks;
using DTMS.Wms.Application.Services;

namespace DTMS.Api.Adapters;

/// <summary>
/// Serves the WMS bearer token out of the managed-credential store, so the
/// token the auto-refresh loop mints is the one the WMS HTTP client presents.
/// Without this the two are unrelated: the loop would rotate a row nobody
/// reads while the client kept sending a static token until it expired.
///
/// <para><b>Scope handling.</b> <see cref="CachedCredentialReader"/> is scoped
/// (it resolves a scoped EF repository on a cache miss), but the consumer is a
/// <c>DelegatingHandler</c> that HttpClientFactory pools and reuses for minutes
/// across many requests. Injecting the reader straight into that handler would
/// capture one request's scope and go on using its disposed DbContext, so this
/// provider is a singleton that opens a scope per call instead.</para>
///
/// <para>No caching here on purpose — the reader already has an L1/L2 tier and
/// the refresher invalidates it on every rotation, so a second cache would only
/// add a window where we serve a token that was just replaced.</para>
/// </summary>
public sealed class SystemCredentialWmsTokenProvider : IWmsTokenProvider
{
    /// <summary>SystemCredentials key holding the WMS outbound credential.</summary>
    public const string SystemKey = "wms";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemCredentialWmsTokenProvider> _logger;

    public SystemCredentialWmsTokenProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemCredentialWmsTokenProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<CachedCredentialReader>();

            var cred = await reader.GetAsync(SystemKey, ct);
            if (cred is null)
                return null;

            return CallbackTokenInspector.ReadStoredToken(cred.CallbackAuthConfig);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Storage trouble must not decide whether WMS is reachable — report
            // "no managed token" and let the caller fall back to the configured
            // one. Warning, not Error: there is a working path after this.
            _logger.LogWarning(ex,
                "[Wms] Could not read the managed token for system '{SystemKey}' — falling back to configuration.",
                SystemKey);
            return null;
        }
    }
}
