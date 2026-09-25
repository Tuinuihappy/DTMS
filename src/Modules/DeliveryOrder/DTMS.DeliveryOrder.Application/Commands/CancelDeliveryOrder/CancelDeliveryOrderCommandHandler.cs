using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;

public class CancelDeliveryOrderCommandHandler : ICommandHandler<CancelDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<CancelDeliveryOrderCommandHandler> _logger;

    public CancelDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ILogger<CancelDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(CancelDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            return Result.Failure($"Order {request.OrderId} not found.");

        try
        {
            order.Cancel(request.Reason);

            // Items bound to a trip follow that trip: the cascade cancels the
            // trip and TripCancelledConsumer then releases and sweeps them.
            // Unbound items have nothing to follow — the order will never
            // dispatch again, so leaving them Pending strands them forever.
            // Bites hardest on a pre-dispatch cancel (Confirmed / Planning /
            // Planned), where there is no trip at all and the trip-side sweep
            // never runs.
            //
            // Safe and idempotent by construction: CancelUnboundItems skips
            // anything with a TripId and anything already terminal, so a
            // delivered item on a part-finished order is not reversed, and a
            // repeat cancel self-heals an order cancelled before this existed.
            var cancelledItems = order.CancelUnboundItems();
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Cancel] Order {OrderId} cancelled, {ItemCount} unbound item(s) cancelled with it. Reason: {Reason}.",
                request.OrderId, cancelledItems, request.Reason);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Cancel] Order {OrderId} cannot be cancelled: {Error}.", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Cancel] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
