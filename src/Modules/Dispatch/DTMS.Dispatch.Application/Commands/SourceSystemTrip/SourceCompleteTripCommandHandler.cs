using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceCompleteTripCommandHandler
    : SourceTripCommandHandlerBase<SourceCompleteTripCommand>
{
    private readonly ILogger<SourceCompleteTripCommandHandler> _logger;

    public SourceCompleteTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceCompleteTripCommandHandler> logger) : base(trips, orders)
    {
        _logger = logger;
    }

    protected override async Task<Result> HandleAuthorized(
        SourceCompleteTripCommand request, Trip trip, CancellationToken cancellationToken)
    {
        // MarkVendorCompleted throws on Cancelled/Failed and no-ops if
        // already Completed — guard the throw path, let the idempotent
        // re-complete succeed.
        if (trip.Status is TripStatus.Cancelled or TripStatus.Failed or TripStatus.Rejected)
            return Result.Failure(
                $"Cannot complete a trip in {trip.Status} status.");

        trip.MarkVendorCompleted(request.ActionBy, request.ActedAt);
        await Trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} completed by {ActionBy} via source system {Key}",
            trip.Id, request.ActionBy, request.SourceSystemKey);
        return Result.Success();
    }
}
