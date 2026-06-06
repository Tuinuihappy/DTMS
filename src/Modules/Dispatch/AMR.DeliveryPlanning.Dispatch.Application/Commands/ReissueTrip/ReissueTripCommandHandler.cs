using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using ITripRetryEventRepository = AMR.DeliveryPlanning.Dispatch.Domain.Repositories.ITripRetryEventRepository;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReissueTrip;

public class ReissueTripCommandHandler : ICommandHandler<ReissueTripCommand, Guid>
{
    // Order statuses that block a Trip-level retry — admin / terminal
    // states the operator has already settled. Compared as strings so
    // we don't reach for DeliveryOrder.Domain just for an enum.
    private static readonly HashSet<string> NonRetryableOrderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cancelled",
        "Rejected",
        "Completed",
        "PartiallyCompleted",
    };

    private readonly ITripRepository _tripRepository;
    private readonly ITripRetryEventRepository _retryEventRepository;
    private readonly ITripRetryDispatcher _retryDispatcher;
    private readonly IDeliveryOrderStatusReader _orderStatus;
    private readonly ILogger<ReissueTripCommandHandler> _logger;

    public ReissueTripCommandHandler(
        ITripRepository tripRepository,
        ITripRetryEventRepository retryEventRepository,
        ITripRetryDispatcher retryDispatcher,
        IDeliveryOrderStatusReader orderStatus,
        ILogger<ReissueTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _retryEventRepository = retryEventRepository;
        _retryDispatcher = retryDispatcher;
        _orderStatus = orderStatus;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(ReissueTripCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RetrySource))
            return Result<Guid>.Failure("RetrySource is required (Manual / Automatic / Reopen).");

        var original = await _tripRepository.GetByIdAsync(request.OriginalTripId, cancellationToken);
        if (original is null)
            return Result<Guid>.Failure($"Trip {request.OriginalTripId} not found.");

        // Failed trips require an explicit DeliveryOrder.Reopen first — the
        // 2-step audit trail distinguishes "who reopened" from "who retried".
        // Operators cancelling intentionally use the Cancelled state, which
        // does NOT propagate to the order (retry-friendly semantic).
        if (original.Status != TripStatus.Cancelled)
            return Result<Guid>.Failure(
                $"Trip is {original.Status}. Only Cancelled trips can be retried. " +
                "For Failed trips, reopen the delivery order first.");

        // Bug fix (E2E scenario 5): a Cancelled trip on a Cancelled (or
        // otherwise terminal-admin) order must not be retried. Without
        // this guard, an open Trip drawer for the cancelled trip would
        // happily re-dispatch a robot for an order the admin already
        // killed — wasting vendor capacity and money.
        var orderStatus = await _orderStatus.GetStatusAsync(original.DeliveryOrderId, cancellationToken);
        if (orderStatus is null)
            return Result<Guid>.Failure($"Delivery order {original.DeliveryOrderId} not found.");
        if (NonRetryableOrderStatuses.Contains(orderStatus))
            return Result<Guid>.Failure(
                $"Cannot retry trip — the parent delivery order is {orderStatus}. " +
                "Reopen the order first if you want to dispatch again.");

        if (original.PickupStationId is null || original.DropStationId is null)
            return Result<Guid>.Failure(
                "Original trip predates retry support (missing route context). " +
                "Re-confirm the delivery order from the source system to dispatch fresh.");

        // Parse original UpperKey to bump the attempt counter. Fall back to
        // (original.AttemptNumber + 1) if the parse fails for any reason —
        // the column is authoritative either way.
        var nextAttempt = original.AttemptNumber + 1;
        if (EnvelopeUpperKey.TryParse(original.UpperKey, out var orderId, out var groupIndex, out var parsedAttempt))
        {
            if (orderId != original.DeliveryOrderId)
            {
                _logger.LogWarning(
                    "[ReissueTrip] UpperKey orderId mismatch on Trip {TripId}: parsed={Parsed} actual={Actual}",
                    original.Id, orderId, original.DeliveryOrderId);
            }
            // Use whichever is higher to be defensive against drift.
            nextAttempt = Math.Max(parsedAttempt, original.AttemptNumber) + 1;
        }
        else
        {
            _logger.LogWarning("[ReissueTrip] UpperKey '{UpperKey}' not parsable; using AttemptNumber+1.",
                original.UpperKey);
            groupIndex = 1;
        }

        var newUpperKey = EnvelopeUpperKey.Build(original.DeliveryOrderId, groupIndex, nextAttempt);

        _logger.LogInformation(
            "[ReissueTrip] Retrying Trip {OriginalTripId} as attempt {Attempt} (UpperKey {UpperKey}, source={Source}, by={By})",
            original.Id, nextAttempt, newUpperKey, request.RetrySource, request.RetriedBy ?? "(unknown)");

        var dispatchResult = await _retryDispatcher.ReissueAsync(
            original.DeliveryOrderId,
            original.PickupStationId.Value,
            original.DropStationId.Value,
            newUpperKey,
            nextAttempt,
            previousAttemptId: original.Id,
            cancellationToken);

        if (dispatchResult.IsFailure)
        {
            _logger.LogWarning(
                "[ReissueTrip] Dispatch rejected retry for Trip {OriginalTripId} (UpperKey {UpperKey}): {Error}",
                original.Id, newUpperKey, dispatchResult.Error);
            return Result<Guid>.Failure(dispatchResult.Error!);
        }

        var newTripId = dispatchResult.Value;

        // Append-only audit log. Persisted after the dispatch succeeded
        // so we never record a retry that didn't actually happen.
        var retryEvent = TripRetryEvent.Record(
            originalTripId: original.Id,
            newTripId: newTripId,
            deliveryOrderId: original.DeliveryOrderId,
            attemptNumber: nextAttempt,
            originalStatus: original.Status.ToString(),
            retrySource: request.RetrySource,
            retriedBy: request.RetriedBy,
            retryReason: request.RetryReason,
            correlationId: request.CorrelationId);
        await _retryEventRepository.AddAsync(retryEvent, cancellationToken);

        _logger.LogInformation(
            "[ReissueTrip] ✓ Trip {OriginalTripId} retried as Trip {NewTripId} (attempt {Attempt})",
            original.Id, newTripId, nextAttempt);

        return Result<Guid>.Success(newTripId);
    }
}
