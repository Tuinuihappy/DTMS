using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.AbandonStuckDeliveryOrder;

public class AbandonStuckDeliveryOrderCommandHandler : ICommandHandler<AbandonStuckDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ITripRepository _tripRepository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<AbandonStuckDeliveryOrderCommandHandler> _logger;

    public AbandonStuckDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ITripRepository tripRepository,
        IOrderAuditEventRepository auditRepo,
        ILogger<AbandonStuckDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _tripRepository = tripRepository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(AbandonStuckDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AbandonedBy))
            return Result.Failure("AbandonedBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Reason is required.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure($"Order {request.OrderId} not found.");

        if (!IsInFlight(order.Status))
            return Result.Failure(
                $"Order is in {order.Status} status — only in-flight orders (Confirmed/Planning/Planned/Dispatched/InProgress) can be abandoned.");

        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var activeTrips = trips
            .Where(t => t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Paused)
            .ToList();
        if (activeTrips.Count > 0)
            return Result.Failure(
                $"Order still has {activeTrips.Count} active trip(s). Cancel them first via the normal cancel flow.");

        try
        {
            order.Cancel($"Abandoned by {request.AbandonedBy}: {request.Reason}");
            // Stranded items (Pending/Picked/DroppedOff with no Trip
            // binding) must follow the order to a terminal state.
            var cancelledItems = order.CancelUnboundItems();

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderAbandoned",
                $"Order '{order.OrderRef}' abandoned by {request.AbandonedBy}: {request.Reason} (terminated {cancelledItems} stranded items)"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Abandon] Order {OrderId} '{OrderRef}' abandoned by {By}: {Reason} (terminated {Items} items)",
                order.Id, order.OrderRef, request.AbandonedBy, request.Reason, cancelledItems);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Abandon] Order {OrderId} abandon rejected: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Abandon] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }

    private static bool IsInFlight(OrderStatus status) => status is
        OrderStatus.Confirmed or OrderStatus.Planning or OrderStatus.Planned or
        OrderStatus.Dispatched or OrderStatus.InProgress;
}
