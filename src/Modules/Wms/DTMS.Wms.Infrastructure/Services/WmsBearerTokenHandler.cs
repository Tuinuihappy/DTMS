using System.Net.Http.Headers;
using DTMS.Wms.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// DelegatingHandler that attaches <c>Authorization: Bearer &lt;token&gt;</c>
/// to every WMS request.
///
/// <para>Two sources, in order:</para>
/// <list type="number">
///   <item><see cref="IWmsTokenProvider"/> — the managed credential kept fresh
///   by the auto-refresh loop. Preferred, because it rotates on its own.</item>
///   <item><see cref="WmsOptions"/>.Auth.Token — the statically configured
///   token, read fresh from IOptionsMonitor. Used when no managed credential
///   exists, which is also the rollback path: delete the credential row and
///   every request goes back to configuration.</item>
/// </list>
///
/// <para>If both are empty the handler still forwards the request (without the
/// header) so the failure mode is a clean 401 from WMS — not a client-side
/// exception — which the sync service can log and retry on the next cycle.
/// This trades one extra HTTP call for observability.</para>
/// </summary>
public sealed class WmsBearerTokenHandler : DelegatingHandler
{
    private readonly IWmsTokenProvider _tokenProvider;
    private readonly IOptionsMonitor<WmsOptions> _options;
    private readonly ILogger<WmsBearerTokenHandler> _logger;

    public WmsBearerTokenHandler(
        IWmsTokenProvider tokenProvider,
        IOptionsMonitor<WmsOptions> options,
        ILogger<WmsBearerTokenHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        var source = "managed credential";

        if (string.IsNullOrWhiteSpace(token))
        {
            token = _options.CurrentValue.Auth.Token;
            source = "configuration";
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "[Wms] No bearer token from either source — WMS request to {Uri} will likely fail with 401.",
                request.RequestUri);
        }
        else
        {
            // Which source is operationally useful (it says whether the managed
            // credential has taken over) and reveals nothing about the token.
            _logger.LogDebug("[Wms] Authorizing request to {Uri} from {Source}.", request.RequestUri, source);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
