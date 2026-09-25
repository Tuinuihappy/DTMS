using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceAcknowledgeTripCommandHandler
    : SourceTripCommandHandlerBase<SourceAcknowledgeTripCommand>
{
    private readonly ILogger<SourceAcknowledgeTripCommandHandler> _logger;

    public SourceAcknowledgeTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceAcknowledgeTripCommandHandler> logger) : base(trips, orders)
    {
        _logger = logger;
    }

    protected override async Task<Result> HandleAuthorized(
        SourceAcknowledgeTripCommand request, Trip trip, CancellationToken cancellationToken)
    {
        // Terminal trips can't be (re)started. Created → InProgress transitions;
        // an already-InProgress/Paused trip no-ops idempotently (safe for
        // at-least-once delivery from the source system).
        if (trip.Status is TripStatus.Completed or TripStatus.Failed or TripStatus.Cancelled or TripStatus.Rejected)
            return Result.Failure(
                $"Cannot acknowledge a trip in {trip.Status} status.");

        trip.AcknowledgeBySource(request.AcknowledgedBy, request.AcknowledgedAt);
        await Trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} acknowledged (started) by {AcknowledgedBy} via source system {Key}",
            trip.Id, request.AcknowledgedBy, request.SourceSystemKey);
        return Result.Success();
    }
}
