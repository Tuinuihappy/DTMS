using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceAcknowledgeRobotPassCommandHandler
    : ICommandHandler<SourceAcknowledgeRobotPassCommand>
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;
    private readonly IVendorRobotOperationService _robotOps;
    private readonly ILogger<SourceAcknowledgeRobotPassCommandHandler> _logger;

    public SourceAcknowledgeRobotPassCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        IVendorRobotOperationService robotOps,
        ILogger<SourceAcknowledgeRobotPassCommandHandler> logger)
    {
        _trips = trips;
        _orders = orders;
        _robotOps = robotOps;
        _logger = logger;
    }

    public async Task<Result> Handle(SourceAcknowledgeRobotPassCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        var trip = resolved.Value;

        // Record the acknowledgment in-memory (audit + actor). PASS is AMR-only
        // and needs an in-flight robot; the domain guards on Status==InProgress
        // and a non-empty VendorVehicleKey — translate those to a clean 400.
        try
        {
            trip.AcknowledgeRobotPass(request.ActionBy, request.ActedAt);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        // Actually nudge the physical robot via the vendor (RIOT3 PASS). This is
        // the step that moves the robot — mirrors the operator command handler.
        // Persist ONLY when the vendor accepts, so a failed/unknown PASS doesn't
        // leave a misleading "acknowledged" audit row for a robot that never moved.
        var vendorResult = await _robotOps.PassAsync(trip.VendorVehicleKey!, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning(
                "[SourceTrip] Vendor PASS rejected for Trip {TripId} (vehicleKey {VehicleKey}) by {ActionBy}: {Error}",
                trip.Id, trip.VendorVehicleKey, request.ActionBy, vendorResult.Error);
            return Result.Failure($"Vendor PASS failed: {vendorResult.Error}");
        }

        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
        {
            // Trip is still in-flight — don't auto-fail. The robot may have
            // already moved on, be offline, or the vendor may have purged the
            // deviceKey. Surface the divergence so the source can decide.
            _logger.LogWarning(
                "[SourceTrip] PASS on Trip {TripId}: vendor has no record of vehicleKey {VehicleKey}",
                trip.Id, trip.VendorVehicleKey);
            return Result.Failure(
                "Vendor has no record of this vehicle key — please verify robot state at the floor.");
        }

        await _trips.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "[SourceTrip] Trip {TripId} robot PASS acknowledged by {ActionBy} via source system {Key} (vehicleKey {VehicleKey})",
            trip.Id, request.ActionBy, request.SourceSystemKey, trip.VendorVehicleKey);
        return Result.Success();
    }
}
