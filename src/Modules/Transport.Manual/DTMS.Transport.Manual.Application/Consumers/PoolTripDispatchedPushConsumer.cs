using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Manual.Application.Consumers;

/// <summary>
/// WMS PR-4b (PR-I) — Web-push notification fan-out for pool dispatches.
///
/// Subscribes to <see cref="TripDispatchedIntegrationEventV1"/> (fires
/// when a Manual/Fleet trip enters the pool) and pushes to every
/// <see cref="OperatorStatus.Active"/> operator with an active
/// Web-Push subscription. The operator PWA's service worker renders the
/// notification even when the tab is closed — the pull-model pool is
/// invisible to offline operators otherwise.
///
/// De-duplication is not needed at this layer: the payload's
/// <c>tag = "pool-add"</c> tells the browser to coalesce back-to-back
/// notifications so a burst of dispatched trips doesn't flood the tray.
///
/// Delivery outcomes are logged but not persisted — the gateway already
/// evicts 410 Gone subscriptions inline. This consumer is pure fan-out.
/// </summary>
public sealed class PoolTripDispatchedPushConsumer : IConsumer<TripDispatchedIntegrationEventV1>
{
    private const string CoalesceTag = "pool-add";

    private readonly IOperatorRepository _operators;
    private readonly IPushNotificationGateway _push;
    private readonly ILogger<PoolTripDispatchedPushConsumer> _logger;

    public PoolTripDispatchedPushConsumer(
        IOperatorRepository operators,
        IPushNotificationGateway push,
        ILogger<PoolTripDispatchedPushConsumer> logger)
    {
        _operators = operators;
        _push = push;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripDispatchedIntegrationEventV1> ctx)
    {
        var evt = ctx.Message;

        // Every push targets the same URL — the pool page. Operator taps
        // the notification and lands on /m/pool where they can preview + claim.
        var payload = new PushNotificationPayload(
            Title: "New pool trip available",
            Body: "Tap to see pickup + drop details in the operator pool.",
            Url: "/m/pool",
            Tag: CoalesceTag);

        var operators = await _operators.ListAllAsync(ctx.CancellationToken);
        var activeIds = operators
            .Where(o => o.Status == OperatorStatus.Active)
            .Select(o => o.Id)
            .ToList();

        if (activeIds.Count == 0)
        {
            _logger.LogInformation(
                "[PoolPush] Trip {TripId} dispatched but zero Active operators to notify — skipping.",
                evt.TripId);
            return;
        }

        // Fan out in parallel — the gateway's throttling / retry policy
        // handles per-endpoint slowness so we don't sequential-wait N
        // subscribers.
        var tasks = activeIds.Select(id => SendSafeAsync(id, payload, ctx.CancellationToken));
        var results = await Task.WhenAll(tasks);

        var totalSent = results.Sum(r => r.Sent);
        var totalFailed = results.Sum(r => r.Failed);
        _logger.LogInformation(
            "[PoolPush] Trip {TripId} fanout: operators={Operators}, sent={Sent}, failed={Failed}",
            evt.TripId, activeIds.Count, totalSent, totalFailed);
    }

    private async Task<PushFanoutResult> SendSafeAsync(
        Guid operatorId, PushNotificationPayload payload, CancellationToken ct)
    {
        try
        {
            return await _push.SendToOperatorAsync(operatorId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[PoolPush] Fan-out threw for operator {OperatorId} — skipping this operator, continuing the rest.",
                operatorId);
            return new PushFanoutResult(0, 0, Array.Empty<PushDeliveryOutcome>());
        }
    }
}
