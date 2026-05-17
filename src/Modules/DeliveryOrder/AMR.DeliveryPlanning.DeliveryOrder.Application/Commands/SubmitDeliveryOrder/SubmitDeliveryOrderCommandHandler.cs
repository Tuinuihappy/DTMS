using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public class SubmitDeliveryOrderCommandHandler : ICommandHandler<SubmitDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IStationValidationService _stationValidation;
    private readonly ILogger<SubmitDeliveryOrderCommandHandler> _logger;

    public SubmitDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        IStationValidationService stationValidation,
        ILogger<SubmitDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _stationValidation = stationValidation;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(SubmitDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<Guid>.Failure($"Order {request.OrderId} not found.");

        var stationMap = await _stationValidation.BuildStationMapAsync(order.Items, cancellationToken);
        if (stationMap.IsFailure) return Result<Guid>.Failure(stationMap.Error);

        try
        {
            order.Submit();
            order.MarkAsValidated(stationMap.Value);
            order.MarkReadyToPlan();

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderSubmitted",
                $"Order '{order.OrderRef}' submitted with priority {order.Priority}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Submit] Order {OrderId} '{OrderRef}' submitted and ready to plan.",
                order.Id, order.OrderRef);

            return Result<Guid>.Success(order.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Submit] Order {OrderId} submit failed: {Error}.", request.OrderId, ex.Message);
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Submit] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
