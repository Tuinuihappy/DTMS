using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceAcknowledgeTripCommandHandler
    : ICommandHandler<SourceAcknowledgeTripCommand>
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;
    private readonly ILogger<SourceAcknowledgeTripCommandHandler> _logger;

    public SourceAcknowledgeTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceAcknowledgeTripCommandHandler> logger)
    {
        _trips = trips;
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result> Handle(SourceAcknowledgeTripCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        var trip = resolved.Value;

        // Terminal trips can't be (re)started. Created → InProgress transitions;
        // an already-InProgress/Paused trip no-ops idempotently (safe for
        // at-least-once delivery from the source system).
        if (trip.Status is TripStatus.Completed or TripStatus.Failed or TripStatus.Cancelled)
            return Result.Failure(
                $"Cannot acknowledge a trip in {trip.Status} status.");

        trip.MarkVendorStarted();
        await _trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} acknowledged (started) by source system {Key}",
            trip.Id, request.SourceSystemKey);
        return Result.Success();
    }
}
