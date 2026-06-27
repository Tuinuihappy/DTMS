using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetTripRetryHistory;

public class GetTripRetryHistoryQueryHandler : IQueryHandler<GetTripRetryHistoryQuery, TripRetryHistoryDto>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITripRetryEventRepository _retryEventRepository;

    public GetTripRetryHistoryQueryHandler(
        ITripRepository tripRepository,
        ITripRetryEventRepository retryEventRepository)
    {
        _tripRepository = tripRepository;
        _retryEventRepository = retryEventRepository;
    }

    public async Task<Result<TripRetryHistoryDto>> Handle(GetTripRetryHistoryQuery request, CancellationToken cancellationToken)
    {
        var current = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (current is null)
            return Result<TripRetryHistoryDto>.Failure($"Trip {request.TripId} not found.");

        // Fetch every Trip on the same order, then narrow to the same
        // (Pickup, Drop) group. We can't trust PreviousAttemptId alone —
        // some chains may have gaps if a vendor reject killed an attempt
        // before AssignToTrip set up the link.
        var allOrderTrips = await _tripRepository.GetByDeliveryOrderIdAsync(
            current.DeliveryOrderId, cancellationToken);

        var chain = allOrderTrips
            .Where(t => t.PickupStationId == current.PickupStationId
                     && t.DropStationId == current.DropStationId)
            .OrderBy(t => t.AttemptNumber)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        // Fall back to a single-trip chain when route context is missing
        // (pre-Gap-3 rows). Better than returning empty.
        if (chain.Count == 0) chain.Add(current);

        // Index retry events by NewTripId — the event records the moment
        // the new Trip was minted, so it attaches to the resulting attempt.
        var retryEvents = await _retryEventRepository.GetByDeliveryOrderIdAsync(
            current.DeliveryOrderId, cancellationToken);
        var triggerByTripId = retryEvents
            .GroupBy(e => e.NewTripId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.OccurredAt).First());

        var attempts = chain.Select(t => new TripChainEntryDto(
            t.Id,
            t.AttemptNumber,
            t.Status.ToString(),
            t.UpperKey,
            t.VendorOrderKey,
            t.CreatedAt,
            t.StartedAt,
            t.CompletedAt,
            t.FailureReason,
            IsCurrent: t.Id == request.TripId,
            RetryTrigger: triggerByTripId.TryGetValue(t.Id, out var evt)
                ? new TripRetryTriggerDto(
                    evt.Id, evt.OccurredAt, evt.RetrySource,
                    evt.RetriedBy, evt.RetryReason, evt.OriginalStatus)
                : null))
            .ToList();

        return Result<TripRetryHistoryDto>.Success(new TripRetryHistoryDto(
            request.TripId, attempts.Count, attempts));
    }
}
