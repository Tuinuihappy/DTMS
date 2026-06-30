using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Abstractions.Exceptions;
using DTMS.OmsAdapter.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace DTMS.OmsAdapter.Infrastructure.Services;

internal sealed class HttpOmsShipmentClient : IOmsShipmentClient
{
    private const string ShipmentsPath = "api/shipments";

    private readonly HttpClient _http;
    private readonly ILogger<HttpOmsShipmentClient> _logger;

    public HttpOmsShipmentClient(HttpClient http, ILogger<HttpOmsShipmentClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task NotifyShipmentStartedAsync(
        OmsCallbackTarget target,
        OmsShipmentNotification notification,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(target.BaseUrl, ShipmentsPath))
        {
            Content = JsonContent.Create(notification),
        };
        ApplyAuth(request, target.BearerToken);

        using var response = await SendWithTimeoutAsync(request, target.Timeout, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[OmsAdapter] POST {Path} shipmentId={ShipmentId} lots={LotCount} → {Status}",
                ShipmentsPath, notification.ShipmentId, notification.Lots.Count, (int)response.StatusCode);
            return;
        }

        // [Option A] 409 Conflict on duplicate shipmentId is the OMS's way
        // of saying "already registered" — treat as a no-op success so a
        // retry attempt re-posting the same shipmentId doesn't dead-letter.
        // If OMS supports upsert with 2xx we never enter this branch.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation(
                "[OmsAdapter] POST {Path} shipmentId={ShipmentId} → 409 — already registered, treating as no-op",
                ShipmentsPath, notification.ShipmentId);
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        _logger.LogWarning(
            "[OmsAdapter] POST {Path} shipmentId={ShipmentId} failed → {Status} body={Body}",
            ShipmentsPath, notification.ShipmentId, (int)response.StatusCode, body);

        ThrowMappedException(response.StatusCode, body);
    }

    public async Task NotifyShipmentArrivedAsync(
        OmsCallbackTarget target,
        string shipmentId,
        IReadOnlyList<OmsLot> lots,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipmentId))
            throw new ArgumentException("shipmentId is required", nameof(shipmentId));

        // shipmentId goes in the URL path; body only carries lots. Escape
        // even though shipmentId is a Guid string — defensive against any
        // future change to non-Guid identifiers.
        var path = $"{ShipmentsPath}/{Uri.EscapeDataString(shipmentId)}/arrived";
        var body = new OmsArrivedNotification(lots);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(target.BaseUrl, path))
        {
            Content = JsonContent.Create(body),
        };
        ApplyAuth(request, target.BearerToken);

        using var response = await SendWithTimeoutAsync(request, target.Timeout, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[OmsAdapter] POST {Path} shipmentId={ShipmentId} lots={LotCount} → {Status}",
                path, shipmentId, lots.Count, (int)response.StatusCode);
            return;
        }

        var errBody = await SafeReadBodyAsync(response, cancellationToken);
        _logger.LogWarning(
            "[OmsAdapter] POST {Path} shipmentId={ShipmentId} failed → {Status} body={Body}",
            path, shipmentId, (int)response.StatusCode, errBody);

        ThrowMappedException(response.StatusCode, errBody);
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
        => new(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), relativePath);

    private static void ApplyAuth(HttpRequestMessage request, string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)) return;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) timeout = TimeSpan.FromSeconds(10);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            return await _http.SendAsync(request, linked.Token);
        }
        catch (TaskCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new OmsTransientException(
                HttpStatusCode.RequestTimeout,
                $"OMS call timed out after {timeout.TotalMilliseconds}ms.");
        }
    }

    // Map HTTP status to permanent vs transient. MassTransit fast-fails
    // OmsPermanentException to DLQ (via .Ignore<> in retry config) so a
    // bad-data poison message can't tar-pit the queue for 80 minutes.
    //
    // Network-level failures (DNS, TLS, connection refused) bubble up
    // before we ever get a response — those raise raw HttpRequestException
    // and remain transient by default. That's intentional: a temporary
    // DNS hiccup shouldn't dead-letter the message.
    private static void ThrowMappedException(HttpStatusCode status, string body)
    {
        var code = (int)status;

        // Retry-able 4xx (RFC-defined: server can't handle now, try again)
        // plus all 5xx → transient.
        if (code is 408 or 425 or 429 || code >= 500)
            throw new OmsTransientException(status, body);

        // Other 4xx (400/401/403/404/422/...) → permanent. The request
        // is wrong in a way retry won't fix — operator must inspect.
        throw new OmsPermanentException(status, body);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return raw.Length <= 512 ? raw : raw[..512] + "…";
        }
        catch
        {
            return "(unreadable)";
        }
    }
}
