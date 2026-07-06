using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceDropTripCommandHandler
    : ICommandHandler<SourceDropTripCommand>
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;
    private readonly ILogger<SourceDropTripCommandHandler> _logger;

    public SourceDropTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ILogger<SourceDropTripCommandHandler> logger)
    {
        _trips = trips;
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result> Handle(SourceDropTripCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        var trip = resolved.Value;

        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot record drop for a trip in {trip.Status} status — " +
                "the trip must be in progress.");

        // Same POD-policy resolution the AMR drop webhook and ForceDrop do,
        // so the drop integration event carries the right RequiresDropPod.
        var requiresDropPod = await _orders.GetRequiresDropPodAsync(
            trip.DeliveryOrderId, cancellationToken);

        trip.MarkVendorDropCompleted(requiresDropPod);
        await _trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} drop recorded by source system {Key} (requiresDropPod={Pod})",
            trip.Id, request.SourceSystemKey, requiresDropPod);
        return Result.Success();
    }
}
