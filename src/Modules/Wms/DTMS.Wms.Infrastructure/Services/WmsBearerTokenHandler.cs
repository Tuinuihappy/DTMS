using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// DelegatingHandler that attaches <c>Authorization: Bearer &lt;token&gt;</c>
/// to every WMS request. Token is read fresh from IOptionsMonitor so a
/// config reload picks up a rotated token without process restart.
///
/// If the token is empty, the handler still forwards the request (without
/// header) so the failure mode is a clean 401 from WMS — not a client-side
/// InvalidOperationException — which the sync service can log and retry
/// on the next cycle. This trades one extra HTTP call for observability.
/// </summary>
public sealed class WmsBearerTokenHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<WmsOptions> _options;
    private readonly ILogger<WmsBearerTokenHandler> _logger;

    public WmsBearerTokenHandler(
        IOptionsMonitor<WmsOptions> options,
        ILogger<WmsBearerTokenHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = _options.CurrentValue.Auth.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "[Wms] Bearer token not configured — WMS request to {Uri} will likely fail with 401.",
                request.RequestUri);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
