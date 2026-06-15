using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.AcknowledgeRobotPass;

// Operator-initiated robot checkpoint acknowledgment (RIOT3 PASS). Unlike
// Pause/Resume — which mutate Trip.Status — this command is a nudge: the
// Trip stays InProgress throughout. On NoVendorRecord we surface a
// warning and DO NOT auto-mark Failed (the Trip is still in-flight; only
// the operator can decide what to do next).
public class AcknowledgeRobotPassCommandHandler : ICommandHandler<AcknowledgeRobotPassCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IVendorRobotOperationService _robotOps;
    private readonly ILogger<AcknowledgeRobotPassCommandHandler> _logger;

    public AcknowledgeRobotPassCommandHandler(
        ITripRepository tripRepository,
        IVendorRobotOperationService robotOps,
        ILogger<AcknowledgeRobotPassCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _robotOps = robotOps;
        _logger = logger;
    }

    public async Task<Result> Handle(AcknowledgeRobotPassCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try { trip.AcknowledgeRobotPass(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        var vendorResult = await _robotOps.PassAsync(trip.VendorVehicleKey!, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor PASS rejected for Trip {TripId} (vehicleKey {VehicleKey}): {Error}",
                trip.Id, trip.VendorVehicleKey, vendorResult.Error);
            return Result.Failure($"Vendor PASS failed: {vendorResult.Error}");
        }

        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
        {
            // Trip is still in-flight — don't auto-fail. The operator needs
            // to see the divergence and decide (the robot may have already
            // moved on, may be offline, or the vendor may have purged the
            // deviceKey from its registry).
            _logger.LogWarning("PASS on Trip {TripId}: vendor has no record of vehicleKey {VehicleKey}",
                trip.Id, trip.VendorVehicleKey);
            return Result.Failure(
                "Vendor has no record of this vehicle key — please verify robot state at the floor.");
        }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation("Trip {TripId} robot PASS acknowledged (vehicleKey {VehicleKey})",
            trip.Id, trip.VendorVehicleKey);
        return Result.Success();
    }
}
