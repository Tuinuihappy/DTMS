using AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.BulkCancelTrips;

// Sequential dispatch of CancelTripCommand per id so domain rules
// (status guard, vendor RIOT3 cancel side effect) stay identical to
// the single-trip path. Failures don't short-circuit so the
// dispatcher gets one consolidated 207 response.
public class BulkCancelTripsCommandHandler
    : ICommandHandler<BulkCancelTripsCommand, BulkCancelTripsResult>
{
    private readonly ISender _sender;
    private readonly ILogger<BulkCancelTripsCommandHandler> _logger;

    public BulkCancelTripsCommandHandler(
        ISender sender,
        ILogger<BulkCancelTripsCommandHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<BulkCancelTripsResult>> Handle(
        BulkCancelTripsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.TripIds is null || request.TripIds.Count == 0)
            return Result<BulkCancelTripsResult>.Failure("TripIds is required and must not be empty.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<BulkCancelTripsResult>.Failure("Reason is required.");

        var succeeded = new List<Guid>(request.TripIds.Count);
        var failures = new List<BulkCancelTripFailure>();
        // Dedup so the UI sending the same id twice doesn't produce a
        // confusing "already cancelled" failure on the second pass.
        var seen = new HashSet<Guid>();

        foreach (var tripId in request.TripIds)
        {
            if (!seen.Add(tripId)) continue;

            var result = await _sender.Send(
                new CancelTripCommand(tripId, request.Reason),
                cancellationToken);

            if (result.IsSuccess)
                succeeded.Add(tripId);
            else
                failures.Add(new BulkCancelTripFailure(tripId, result.Error ?? "Cancel failed."));
        }

        _logger.LogInformation(
            "[BulkCancel] Trips: {SucceededCount} cancelled, {FailedCount} failed. Reason: {Reason}.",
            succeeded.Count, failures.Count, request.Reason);

        return Result<BulkCancelTripsResult>.Success(new BulkCancelTripsResult(succeeded, failures));
    }
}
