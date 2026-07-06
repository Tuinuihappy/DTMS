using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceCompleteTripCommandHandler
    : ICommandHandler<SourceCompleteTripCommand>
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;
    private readonly ILogger<SourceCompleteTripCommandHandler> _logger;

    public SourceCompleteTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceCompleteTripCommandHandler> logger)
    {
        _trips = trips;
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result> Handle(SourceCompleteTripCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        var trip = resolved.Value;

        // MarkVendorCompleted throws on Cancelled/Failed and no-ops if
        // already Completed — guard the throw path, let the idempotent
        // re-complete succeed.
        if (trip.Status is TripStatus.Cancelled or TripStatus.Failed)
            return Result.Failure(
                $"Cannot complete a trip in {trip.Status} status.");

        trip.MarkVendorCompleted();
        await _trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} completed by source system {Key}",
            trip.Id, request.SourceSystemKey);
        return Result.Success();
    }
}
