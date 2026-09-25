using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourcePickupTripCommandHandler
    : SourceTripCommandHandlerBase<SourcePickupTripCommand>
{
    private readonly ILogger<SourcePickupTripCommandHandler> _logger;

    public SourcePickupTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourcePickupTripCommandHandler> logger) : base(trips, orders)
    {
        _logger = logger;
    }

    protected override async Task<Result> HandleAuthorized(
        SourcePickupTripCommand request, Trip trip, CancellationToken cancellationToken)
    {
        // MarkVendorPickedUp silently bails outside InProgress — surface it
        // as a clear API error rather than a confusing no-op 204.
        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot record pickup for a trip in {trip.Status} status — " +
                "acknowledge (start) the trip first.");

        trip.MarkVendorPickedUp(request.ActionBy, request.ActedAt);
        await Trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} pickup recorded by {ActionBy} via source system {Key}",
            trip.Id, request.ActionBy, request.SourceSystemKey);
        return Result.Success();
    }
}
