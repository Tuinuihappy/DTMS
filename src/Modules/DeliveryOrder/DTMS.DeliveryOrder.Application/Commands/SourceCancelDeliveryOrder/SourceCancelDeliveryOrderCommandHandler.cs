using DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.SourceCancelDeliveryOrder;

/// <summary>
/// Resolves (SourceSystemKey, OrderRef) to an order id, then delegates to the
/// one cancel implementation every caller shares.
///
/// <para><b>Why two reads.</b> GetByRefAsync is AsNoTracking with no Includes —
/// right for a guard read, wrong for a mutation. Rather than adding a tracked
/// twin, this resolves the id and hands off to CancelDeliveryOrderCommand, which
/// already owns the tracked load, the unbound-item sweep, the concurrency catch
/// and the logging. A second by-ref cancel path would duplicate all four and
/// drift from them the first time one side is fixed.</para>
/// </summary>
public sealed class SourceCancelDeliveryOrderCommandHandler
    : ICommandHandler<SourceCancelDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ISender _sender;
    private readonly ILogger<SourceCancelDeliveryOrderCommandHandler> _logger;

    public SourceCancelDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ISender sender,
        ILogger<SourceCancelDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result> Handle(
        SourceCancelDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        // Both columns filter in SQL against the unique (SourceSystemKey,
        // OrderRef) index, so this IS the origin guard — there is no separate
        // ownership check to forget or to run in the wrong order.
        //
        // Case-sensitive, because `==` on a text column is SQL `=` under the
        // default collation. A ref differing only in case reads as "not found";
        // the OMS doc says so rather than us inventing a normalisation the
        // unique index does not share.
        var order = await _repository.GetByRefAsync(
            request.SourceSystemKey, request.OrderRef, cancellationToken);

        if (order is null)
        {
            _logger.LogInformation(
                "[SourceOrderCancel] {Key} asked to cancel unknown ref {OrderRef} (by {ActionBy})",
                request.SourceSystemKey, request.OrderRef, request.ActionBy);
            // 404, and deliberately not the 403 the trip route answers for a
            // foreign resource: an OrderRef is sequential and guessable, so
            // confirming one exists — let alone naming its owner — would leak
            // another system's order book.
            throw new NotFoundException(
                $"No delivery order with ref '{request.OrderRef}' for this source system.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            // Warn rather than quietly default. Reason is the only field on this
            // path that reaches durable, queryable storage — it becomes the line
            // an operator reads on the order activity timeline. Which system
            // asked is already on that row, so naming it here would read twice.
            _logger.LogWarning(
                "[SourceOrderCancel] Order {OrderId} cancel from {Key} carried no reason — falling back to a generic label",
                order.Id, request.SourceSystemKey);
            reason = "Cancelled by source system";
        }

        // ActionBy and ActedAt stop here, deliberately. The audit timeline reads
        // deliveryorder.OrderActivity, fed only from integration events, and
        // DeliveryOrderCancelledIntegrationEventV1 has no field for an upstream
        // human — its actor fields come from the ambient principal and so name
        // the SYSTEM (oms), not the person at it. Writing an OrderAuditEvent row
        // instead would persist them where nothing reads. They are logged, and
        // docs/oms-order-cancel-api.md says so rather than implying otherwise.
        _logger.LogInformation(
            "[SourceOrderCancel] {Key} cancelling order {OrderId} ({OrderRef}) — by {ActionBy}, acted {ActedAt}: {Reason}",
            request.SourceSystemKey, order.Id, request.OrderRef,
            request.ActionBy, request.ActedAt, reason);

        // No already-cancelled short-circuit, unlike the trip route:
        // DeliveryOrder.Cancel early-returns rather than throwing, so a repeat
        // already answers 204 and raises no second domain event. Delegating
        // anyway means the shared handler's idempotent item sweep still runs,
        // which self-heals an order cancelled before that sweep existed.
        return await _sender.Send(
            new CancelDeliveryOrderCommand(order.Id, reason), cancellationToken);
    }
}
