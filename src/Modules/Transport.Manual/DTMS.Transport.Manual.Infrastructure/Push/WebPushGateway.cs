using System.Text.Json;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Push;

// Phase 4.3 — Web Push (VAPID) gateway. Loads the operator's active
// Web Push subscriptions, signs + sends in parallel, and tracks
// per-endpoint delivery state on the OperatorPushSubscription entity:
//
//   - 201 / 202 / 200  → MarkDeliverySucceeded
//   - 404 / 410        → MarkDeliveryFailed + ShouldEvict
//   - 429 / 500 / etc  → MarkDeliveryFailed (transient — try again next push)
//
// SaveChangesAsync runs at the end so a single push call doesn't issue
// 1+N DB writes. ConsecutiveFailures rolling counter on the entity
// flips ShouldEvict at 5 — caller can prune.
public sealed class WebPushGateway : IPushNotificationGateway
{
    private readonly IOperatorRepository _operators;
    private readonly WebPushClient _client;
    private readonly VapidOptions _vapid;
    private readonly ILogger<WebPushGateway> _logger;

    public WebPushGateway(
        IOperatorRepository operators,
        IOptions<VapidOptions> vapid,
        ILogger<WebPushGateway> logger)
    {
        _operators = operators;
        _vapid = vapid.Value;
        _logger = logger;
        _client = new WebPushClient();
        if (_vapid.IsConfigured)
        {
            _client.SetVapidDetails(new VapidDetails(
                subject: _vapid.Subject,
                publicKey: _vapid.PublicKey,
                privateKey: _vapid.PrivateKey));
        }
    }

    public async Task<PushFanoutResult> SendToOperatorAsync(
        Guid operatorId,
        PushNotificationPayload payload,
        CancellationToken ct = default)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogWarning(
                "WebPushGateway: VAPID keys not configured — push to operator {OperatorId} skipped. " +
                "Set Push:Vapid:PublicKey and Push:Vapid:PrivateKey in configuration.",
                operatorId);
            return new PushFanoutResult(0, 0, Array.Empty<PushDeliveryOutcome>());
        }

        var op = await _operators.GetByIdWithDetailsAsync(operatorId, ct);
        if (op is null)
        {
            _logger.LogWarning("WebPushGateway: operator {OperatorId} not found — push skipped.", operatorId);
            return new PushFanoutResult(0, 0, Array.Empty<PushDeliveryOutcome>());
        }

        // Only Web Push platform in 4.3 — FCM/APNS branches added when
        // PushPlatform enum gets non-WebPush entries with actual values.
        var subs = op.PushSubscriptions
                     .Where(s => s.Platform == PushPlatform.WebPush
                              && !string.IsNullOrWhiteSpace(s.PublicKey)
                              && !string.IsNullOrWhiteSpace(s.AuthSecret))
                     .ToList();
        if (subs.Count == 0)
            return new PushFanoutResult(0, 0, Array.Empty<PushDeliveryOutcome>());

        var json = JsonSerializer.Serialize(payload, JsonOpts);

        var outcomes = await Task.WhenAll(subs.Select(async sub =>
            await SendOneAsync(sub, json, ct)));

        // Single round-trip to commit the per-subscription state changes.
        await _operators.SaveChangesAsync(ct);

        var sent = outcomes.Count(o => o.Delivered);
        var failed = outcomes.Length - sent;
        return new PushFanoutResult(sent, failed, outcomes);
    }

    private async Task<PushDeliveryOutcome> SendOneAsync(
        OperatorPushSubscription sub, string payloadJson, CancellationToken ct)
    {
        var pushSubscription = new PushSubscription(sub.Endpoint, sub.PublicKey, sub.AuthSecret);
        try
        {
            await _client.SendNotificationAsync(pushSubscription, payloadJson);
            sub.MarkDeliverySucceeded();
            return new PushDeliveryOutcome(sub.Endpoint, Delivered: true, ShouldEvict: false);
        }
        catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone
                                       || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Subscription is dead — browser uninstalled the PWA,
            // user revoked notification permission, push service rotated
            // endpoint URL. Mark for eviction so caller can prune.
            sub.MarkDeliveryFailed();
            _logger.LogInformation(
                "WebPushGateway: subscription {Endpoint} returned {Status} — flagged for eviction.",
                sub.Endpoint, ex.StatusCode);
            return new PushDeliveryOutcome(sub.Endpoint, Delivered: false, ShouldEvict: true, Error: ex.Message);
        }
        catch (Exception ex)
        {
            sub.MarkDeliveryFailed();
            _logger.LogWarning(ex,
                "WebPushGateway: send to {Endpoint} failed (consecutive failures now {Count}).",
                sub.Endpoint, sub.ConsecutiveFailures);
            return new PushDeliveryOutcome(
                sub.Endpoint, Delivered: false, ShouldEvict: sub.ShouldEvict, Error: ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
