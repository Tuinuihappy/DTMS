using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourcePickupTripCommandHandler
    : ICommandHandler<SourcePickupTripCommand>
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;
    private readonly ILogger<SourcePickupTripCommandHandler> _logger;

    public SourcePickupTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourcePickupTripCommandHandler> logger)
    {
        _trips = trips;
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result> Handle(SourcePickupTripCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        var trip = resolved.Value;

        // MarkVendorPickedUp silently bails outside InProgress — surface it
        // as a clear API error rather than a confusing no-op 204.
        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot record pickup for a trip in {trip.Status} status — " +
                "acknowledge (start) the trip first.");

        trip.MarkVendorPickedUp(request.ActionBy, request.ActedAt);
        await _trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} pickup recorded by {ActionBy} via source system {Key}",
            trip.Id, request.ActionBy, request.SourceSystemKey);
        return Result.Success();
    }
}
