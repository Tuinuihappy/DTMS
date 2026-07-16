using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.Dispatch.Application.Commands.ReissueTrip;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;

public class ReopenDeliveryOrderCommandHandler
    : ICommandHandler<ReopenDeliveryOrderCommand, ReopenOrderResult>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ITripRepository _tripRepository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ISender _sender;
    private readonly ILogger<ReopenDeliveryOrderCommandHandler> _logger;

    public ReopenDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ITripRepository tripRepository,
        IOrderAuditEventRepository auditRepo,
        ISender sender,
        ILogger<ReopenDeliveryOrderCommandHandler> logger)
    {
        _repository = repository;
        _tripRepository = tripRepository;
        _auditRepo = auditRepo;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<ReopenOrderResult>> Handle(ReopenDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReopenedBy))
            return Result<ReopenOrderResult>.Failure("ReopenedBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<ReopenOrderResult>.Failure("Reason is required.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ReopenOrderResult>.Failure($"Order {request.OrderId} not found.");

        // Race guard: reopening while the cancel cascade is still tearing
        // down trips (vendor cancel in flight) would let the follow-up
        // /retry dispatch a second robot alongside the not-yet-cancelled
        // one. Require every trip to be settled first — same rule the
        // abandon-after-trip-cancel flow enforces. For Failed orders all
        // trips are terminal by construction, so this is a no-op there.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var activeTrips = trips
            .Count(t => t.Status is TripStatus.Created or TripStatus.InProgress or TripStatus.Paused);
        if (activeTrips > 0)
            return Result<ReopenOrderResult>.Failure(
                $"Order still has {activeTrips} active trip(s). Wait for the cancel cascade to finish (or cancel them), then reopen.");

        int reinstated;
        try
        {
            reinstated = order.Reopen(request.Reason);

            var reinstatedNote = reinstated > 0 ? $" (reinstated {reinstated} cancelled items)" : string.Empty;
            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "OrderReopened",
                $"Order '{order.OrderRef}' reopened by {request.ReopenedBy}: {request.Reason}{reinstatedNote}"), cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Reopen] Order {OrderId} '{OrderRef}' reopened by {By}: {Reason} (reinstated {Items} items)",
                order.Id, order.OrderRef, request.ReopenedBy, request.Reason, reinstated);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Reopen] Order {OrderId} reopen rejected: {Error}", request.OrderId, ex.Message);
            return Result<ReopenOrderResult>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[Reopen] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<ReopenOrderResult>.Failure("The order was modified by another process. Please retry.");
        }

        if (!request.AutoRetry)
            return Result<ReopenOrderResult>.Success(new ReopenOrderResult(reinstated, 0, []));

        // Auto-retry (opt-in from the dialog): reissue the tip of each retry
        // chain through the SAME path as the manual retry button, so
        // PreviousAttemptId linking (→ stable OMS shipmentId), attempt
        // numbering and Job rebind all behave identically. The reopen is
        // already committed — a retry failure is reported, never rolled back.
        var previousIds = trips
            .Where(t => t.PreviousAttemptId is not null)
            .Select(t => t.PreviousAttemptId!.Value)
            .ToHashSet();
        var chainTips = trips
            .Where(t => t.Status is TripStatus.Cancelled or TripStatus.Failed)
            .Where(t => !previousIds.Contains(t.Id))
            .ToList();

        var retried = 0;
        var errors = new List<string>();
        foreach (var tip in chainTips)
        {
            var retryResult = await _sender.Send(new ReissueTripCommand(
                tip.Id,
                RetrySource: "Reopen",
                RetriedBy: request.ReopenedBy,
                RetryReason: $"Auto-retry after reopen: {request.Reason}"), cancellationToken);

            if (retryResult.IsSuccess)
            {
                retried++;
                _logger.LogInformation(
                    "[Reopen] Auto-retried Trip {TripId} as {NewTripId} on Order {OrderId}",
                    tip.Id, retryResult.Value, order.Id);
            }
            else
            {
                errors.Add($"Trip {tip.Id}: {retryResult.Error}");
                _logger.LogWarning(
                    "[Reopen] Auto-retry of Trip {TripId} on Order {OrderId} failed: {Error}",
                    tip.Id, order.Id, retryResult.Error);
            }
        }

        return Result<ReopenOrderResult>.Success(new ReopenOrderResult(reinstated, retried, errors));
    }
}
