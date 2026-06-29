using DTMS.Api.Infrastructure.Outbox;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b — listens for <see cref="DeliveryOrderCompletedIntegrationEventV1"/>
/// (our "order delivered" event) and fans out one
/// <see cref="OutboxMessage"/> per active subscriber. The downstream
/// <c>MultiPartitionOutboxProcessor</c> (S.3) then drains each row
/// through <c>ISourceCallbackDispatcher</c> (HTTP impl after S.3.1b's
/// DI swap).
///
/// <para><b>Idempotency.</b> The outbox row's <c>CorrelationId</c> is
/// set to <see cref="ConsumeContext.MessageId"/>; paired with
/// <c>PartitionKey</c>, the partial unique index on
/// <c>outbox.OutboxMessages</c> ensures a MassTransit consumer retry
/// (same MessageId on second delivery) doesn't double-enqueue. The
/// duplicate INSERT raises a Postgres unique violation; we catch it as
/// "already enqueued" and proceed.</para>
///
/// <para><b>Empty subscriber list.</b> Returns silently — common case
/// for events that exist in the registry but have no current
/// subscribers wired in DB. Cache layer makes that an O(1) check.</para>
/// </summary>
public sealed class OrderDeliveredCallbackFanoutConsumer
    : IConsumer<DeliveryOrderCompletedIntegrationEventV1>
{
    private readonly ISubscriptionLookup _lookup;
    private readonly IServiceProvider _sp;
    private readonly OutboxDbContext _outbox;
    private readonly ILogger<OrderDeliveredCallbackFanoutConsumer> _log;

    public OrderDeliveredCallbackFanoutConsumer(
        ISubscriptionLookup lookup,
        IServiceProvider sp,
        OutboxDbContext outbox,
        ILogger<OrderDeliveredCallbackFanoutConsumer> log)
    {
        _lookup = lookup;
        _sp = sp;
        _outbox = outbox;
        _log = log;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
    {
        var ct = ctx.CancellationToken;
        var eventType = CallbackEventTypes.OrderDeliveredV1;

        var subs = await _lookup.GetSubscribersAsync(eventType, ct);
        if (subs.Count == 0)
        {
            _log.LogDebug(
                "No subscribers for {EventType}; skipping fan-out for order {OrderId}",
                eventType, ctx.Message.DeliveryOrderId);
            return;
        }

        var correlationId = ctx.MessageId ?? Guid.NewGuid();

        foreach (var sub in subs)
        {
            var formatter = _sp.GetRequiredKeyedService<ICallbackPayloadFormatter>(sub.PayloadFormatKey);
            var payload = await formatter.FormatAsync(ctx.Message, ct);

            // Store payload as text — outbox.Content is text-typed; the
            // dispatcher will re-encode to bytes at HTTP time. UTF-8
            // round-trip is lossless for application/json (which is the
            // only ContentType formatters emit today).
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
            // Idempotent path — consumer retry where a previous delivery
            // already inserted the rows for this correlation. The
            // partial unique index on (PartitionKey, CorrelationId) is
            // exactly what protects us; swallow + move on, the dispatch
            // path will handle the existing rows.
            _log.LogInformation(
                "Outbox rows for {EventType} order={OrderId} correlation={CorrelationId} " +
                "already enqueued by a prior delivery; skipping duplicate.",
                eventType, ctx.Message.DeliveryOrderId, correlationId);
        }
    }

    // Postgres unique-violation = SQLSTATE 23505. Npgsql surfaces it
    // through PostgresException.SqlState; the DbUpdateException wraps it.
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
