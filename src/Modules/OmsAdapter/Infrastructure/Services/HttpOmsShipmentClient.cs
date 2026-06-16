using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Services;

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

    public async Task NotifyShipmentStartedAsync(OmsShipmentNotification notification, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(ShipmentsPath, notification, cancellationToken);

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

        response.EnsureSuccessStatusCode();
    }

    public async Task NotifyShipmentArrivedAsync(string shipmentId, IReadOnlyList<OmsLot> lots, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipmentId))
            throw new ArgumentException("shipmentId is required", nameof(shipmentId));

        // shipmentId goes in the URL path; body only carries lots. Escape
        // even though shipmentId is a Guid string — defensive against any
        // future change to non-Guid identifiers.
        var path = $"{ShipmentsPath}/{Uri.EscapeDataString(shipmentId)}/arrived";
        var body = new OmsArrivedNotification(lots);

        using var response = await _http.PostAsJsonAsync(path, body, cancellationToken);

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

        response.EnsureSuccessStatusCode();
    }

    public Task NotifyShipmentFailedAsync(string shipmentId, OmsTripFailedNotification body, CancellationToken cancellationToken)
        => PostStageAsync(shipmentId, "failed", body, cancellationToken);

    public Task NotifyShipmentCancelledAsync(string shipmentId, OmsTripCancelledNotification body, CancellationToken cancellationToken)
        => PostStageAsync(shipmentId, "cancelled", body, cancellationToken);

    public Task NotifyShipmentPodCompletedAsync(string shipmentId, OmsPodCompletedNotification body, CancellationToken cancellationToken)
        => PostStageAsync(shipmentId, "pod-completed", body, cancellationToken);

    // Phase OMS B4 — shared POST path for the failed/cancelled/pod-completed
    // stages. Identical pattern to /arrived: shipmentId in URL, body
    // carries the stage-specific payload. 409 Conflict is treated as
    // idempotent success (the OMS receiver has already accepted this
    // stage's notification — re-firing the same shipmentId/stage must
    // not dead-letter the retry queue).
    private async Task PostStageAsync<TBody>(
        string shipmentId, string stage, TBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipmentId))
            throw new ArgumentException("shipmentId is required", nameof(shipmentId));

        var path = $"{ShipmentsPath}/{Uri.EscapeDataString(shipmentId)}/{stage}";
        using var response = await _http.PostAsJsonAsync(path, body, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[OmsAdapter] POST {Path} shipmentId={ShipmentId} → {Status}",
                path, shipmentId, (int)response.StatusCode);
            return;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation(
                "[OmsAdapter] POST {Path} shipmentId={ShipmentId} → 409 — already recorded, treating as no-op",
                path, shipmentId);
            return;
        }

        var errBody = await SafeReadBodyAsync(response, cancellationToken);
        _logger.LogWarning(
            "[OmsAdapter] POST {Path} shipmentId={ShipmentId} failed → {Status} body={Body}",
            path, shipmentId, (int)response.StatusCode, errBody);

        response.EnsureSuccessStatusCode();
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
