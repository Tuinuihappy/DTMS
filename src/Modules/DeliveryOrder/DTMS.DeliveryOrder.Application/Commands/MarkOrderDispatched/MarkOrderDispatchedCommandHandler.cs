using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkOrderDispatched;

public class MarkOrderDispatchedCommandHandler : ICommandHandler<MarkOrderDispatchedCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<MarkOrderDispatchedCommandHandler> _logger;

    public MarkOrderDispatchedCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<MarkOrderDispatchedCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkOrderDispatchedCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.MarkDispatched();
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[MarkOrderDispatched] Order {OrderId} → Dispatched", request.OrderId);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[MarkOrderDispatched] {OrderId}: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            // AutoPlan's last step racing another writer of the same order —
            // in practice TripStarted moving a self-managed order to
            // InProgress the instant its trip is dispatched. Unhandled, this
            // re-ran the whole AutoPlan consumer 60 s later just to abandon
            // it. Reload and re-apply once: MarkDispatched is forward-only
            // and idempotent, so an order already past Dispatched needs
            // nothing. ResetTracking first, or the failed save's queued
            // "Dispatched" outbox row would go out with the retry.
            return await ResolveConflictAsync(request.OrderId, cancellationToken);
        }
    }

    private async Task<Result> ResolveConflictAsync(Guid orderId, CancellationToken ct)
    {
        _repository.ResetTracking();
        var order = await _repository.GetByIdAsync(orderId, ct);
        if (order is null) return Result.Failure($"Order {orderId} not found.");

        // A self-managed trip can finish within seconds of dispatch, so the
        // winner may already have closed the order. Nothing left to dispatch
        // — the outcome of the race, not an error.
        if (IsTerminal(order.Status))
        {
            _logger.LogInformation(
                "[MarkOrderDispatched] Order {OrderId}: concurrent update resolved — already {Status}, nothing to dispatch",
                orderId, order.Status);
            return Result.Success();
        }

        try
        {
            order.MarkDispatched();
            // A second conflict propagates: MassTransit's retry takes over,
            // as it did for every conflict before this handler resolved them.
            await _repository.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. an admin Hold landed in between — a real reason not to dispatch.
            _logger.LogWarning("[MarkOrderDispatched] {OrderId}: {Error}", orderId, ex.Message);
            return Result.Failure(ex.Message);
        }

        _logger.LogInformation(
            "[MarkOrderDispatched] Order {OrderId}: concurrent update resolved — now {Status}",
            orderId, order.Status);
        return Result.Success();
    }

    private static bool IsTerminal(OrderStatus status) => status is
        OrderStatus.Completed or OrderStatus.PartiallyCompleted or OrderStatus.Failed
        or OrderStatus.Cancelled or OrderStatus.Rejected;
}
