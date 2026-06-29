using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b follow-up — listens for
/// <see cref="DeliveryOrderCancelledIntegrationEventV1"/> and fans
/// out per subscriber. Sibling of
/// <see cref="OrderDeliveredCallbackFanoutConsumer"/>; same
/// idempotency contract (unique <c>(PartitionKey, CorrelationId)</c>
/// catches consumer retries) and same outbox path.
/// </summary>
public sealed class OrderCancelledCallbackFanoutConsumer
    : IConsumer<DeliveryOrderCancelledIntegrationEventV1>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ILogger<OrderCancelledCallbackFanoutConsumer> _log;

    public OrderCancelledCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ILogger<OrderCancelledCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _log = log;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
    {
        var ct = ctx.CancellationToken;
        var eventType = CallbackEventTypes.OrderCancelledV1;

        // Phase S.3.1b follow-up — source-routed delivery (see sibling
        // OrderDeliveredCallbackFanoutConsumer for the rationale).
        var source = ctx.Message.SourceSystem;
        if (string.IsNullOrWhiteSpace(source))
        {
            _log.LogDebug(
                "Order {OrderId} has no SourceSystem; no external callback to route.",
                ctx.Message.DeliveryOrderId);
            return;
        }

        var allSubs = await _lookup.GetSubscribersAsync(eventType, ct);
        var subs = allSubs
            .Where(s => string.Equals(s.SystemKey, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subs.Count == 0)
        {
            _log.LogDebug(
                "Source system {Source} has no enabled subscription for {EventType}; " +
                "skipping fan-out for order {OrderId}",
                source, eventType, ctx.Message.DeliveryOrderId);
            return;
        }

        var correlationId = ctx.MessageId ?? Guid.NewGuid();

        foreach (var sub in subs)
        {
            var formatter = _sp.GetRequiredKeyedService<ICallbackPayloadFormatter>(sub.PayloadFormatKey);
            var payload = await formatter.FormatAsync(ctx.Message, ct);
            var content = System.Text.Encoding.UTF8.GetString(payload.Body);

            _outbox.OutboxMessages.Add(new OutboxMessage(
                id: Guid.NewGuid(),
                type: eventType,
                content: content,
                occurredOnUtc: DateTime.UtcNow,
                partitionKey: sub.SystemKey,
                correlationId: correlationId));
        }

        try
        {
            await _outbox.SaveChangesAsync(ct);
            _log.LogInformation(
                "Fanned out {EventType} (order {OrderId}, correlation {CorrelationId}) to {N} subscribers",
                eventType, ctx.Message.DeliveryOrderId, correlationId, subs.Count);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (IsUniqueViolation(ex))
        {
            _log.LogInformation(
                "Outbox rows for {EventType} order={OrderId} correlation={CorrelationId} " +
                "already enqueued by a prior delivery; skipping duplicate.",
                eventType, ctx.Message.DeliveryOrderId, correlationId);
        }
    }

    private static bool IsUniqueViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
    {
        for (var cur = ex.InnerException; cur is not null; cur = cur.InnerException)
        {
            if (cur is Npgsql.PostgresException pg && pg.SqlState == "23505")
                return true;
        }
        return false;
    }
}
