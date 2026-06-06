using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RedispatchDeliveryOrder;

public class RedispatchDeliveryOrderCommandHandler : ICommandHandler<RedispatchDeliveryOrderCommand>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<RedispatchDeliveryOrderCommandHandler> _logger;

    public RedispatchDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ITripRepository tripRepository,
        ILogger<RedispatchDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _tripRepository = tripRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(RedispatchDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RedispatchedBy))
            return Result.Failure("RedispatchedBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure("Reason is required.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return Result.Failure($"Order {request.OrderId} not found.");

        // Guard against double-dispatch: if any Trip is still in-flight
        // for this order, the operator should use Trip-level /retry on
        // the specific Trip instead. Redispatching with active Trips
        // would queue duplicate vendor orders for the same items.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var hasActiveTrip = trips.Any(t =>
            t.Status is Dispatch.Domain.Enums.TripStatus.Created
                or Dispatch.Domain.Enums.TripStatus.InProgress
                or Dispatch.Domain.Enums.TripStatus.Paused);
        if (hasActiveTrip)
            return Result.Failure(
                "Cannot redispatch — at least one trip on this order is still active. " +
                "Use /trips/{id}/retry on the specific trip instead.");

        try
        {
            order.Redispatch(request.WeightFallbackKg, request.Reason);

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderRedispatched",
                $"Order '{order.OrderRef}' redispatched by {request.RedispatchedBy}: {request.Reason}"),
                cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Redispatch] Order {OrderId} '{OrderRef}' redispatched by {By}: {Reason}",
                order.Id, order.OrderRef, request.RedispatchedBy, request.Reason);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Redispatch] Order {OrderId} rejected: {Error}", request.OrderId, ex.Message);
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Redispatch] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result.Failure("The order was modified by another process. Please retry.");
        }
    }
}
