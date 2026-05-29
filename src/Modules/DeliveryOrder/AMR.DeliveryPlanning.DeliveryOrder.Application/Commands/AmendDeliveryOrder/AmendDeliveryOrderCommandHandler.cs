using System.Text.Json;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public class AmendDeliveryOrderCommandHandler : ICommandHandler<AmendDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _orderRepo;
    private readonly IOrderAmendmentRepository _amendmentRepo;
    private readonly ILogger<AmendDeliveryOrderCommandHandler> _logger;

    public AmendDeliveryOrderCommandHandler(
        IDeliveryOrderRepository orderRepo,
        IOrderAmendmentRepository amendmentRepo,
        ILogger<AmendDeliveryOrderCommandHandler> logger)
    {
        _orderRepo = orderRepo;
        _amendmentRepo = amendmentRepo;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(AmendDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<Guid>.Failure($"Order {request.OrderId} not found.");

        var amendedBy = string.IsNullOrWhiteSpace(request.AmendedBy) ? null : request.AmendedBy.Trim();

        var originalSnapshot = JsonSerializer.Serialize(OrderSnapshotV1.From(order));

        try
        {
            var newServiceWindow = request.NewServiceWindow is { } sw
                ? Domain.ValueObjects.ServiceWindow.Create(sw.EarliestUtc, sw.LatestUtc)
                : null;

            order.AmendServiceWindow(newServiceWindow, request.Reason);

            var newSnapshot = JsonSerializer.Serialize(OrderSnapshotV1.From(order));

            var amendment = new OrderAmendment(
                order.Id, AmendmentType.ServiceWindowChange, request.Reason,
                originalSnapshot, newSnapshot, amendedBy,
                amendmentVersion: 1);

            await _amendmentRepo.AddAsync(amendment, cancellationToken);
            await _orderRepo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Amend] Order {OrderId} amended ({AmendmentType}) by {AmendedBy}.",
                request.OrderId, amendment.Type, amendedBy ?? "system");

            return Result<Guid>.Success(amendment.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Amend] Order {OrderId} amendment failed: {Error}.", request.OrderId, ex.Message);
            return Result<Guid>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Amend] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<Guid>.Failure("The order was modified by another process. Please retry.");
        }
    }
}
