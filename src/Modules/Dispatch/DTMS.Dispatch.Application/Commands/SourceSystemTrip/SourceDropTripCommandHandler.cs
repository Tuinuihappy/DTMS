using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceDropTripCommandHandler
    : SourceTripCommandHandlerBase<SourceDropTripCommand>
{
    private readonly ILogger<SourceDropTripCommandHandler> _logger;

    public SourceDropTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceDropTripCommandHandler> logger) : base(trips, orders)
    {
        _logger = logger;
    }

    protected override async Task<Result> HandleAuthorized(
        SourceDropTripCommand request, Trip trip, CancellationToken cancellationToken)
    {
        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot record drop for a trip in {trip.Status} status — " +
                "the trip must be in progress.");

        // Same POD-policy resolution the AMR drop webhook and ForceDrop do,
        // so the drop integration event carries the right RequiresDropPod.
        var requiresDropPod = await Orders.GetRequiresDropPodAsync(
            trip.DeliveryOrderId, cancellationToken);

        trip.MarkVendorDropCompleted(requiresDropPod, request.ActionBy, request.ActedAt);
        await Trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} drop recorded by {ActionBy} via source system {Key} (requiresDropPod={Pod})",
            trip.Id, request.ActionBy, request.SourceSystemKey, requiresDropPod);
        return Result.Success();
    }
}
