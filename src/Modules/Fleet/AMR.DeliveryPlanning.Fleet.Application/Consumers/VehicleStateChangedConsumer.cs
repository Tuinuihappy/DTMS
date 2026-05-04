using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Fleet.Application.Consumers;

public class VehicleStateChangedConsumer : IConsumer<VehicleStateChangedIntegrationEvent>
{
    private readonly IVehicleRepository _vehicleRepo;
    private readonly IChargingPolicyRepository _policyRepo;
    private readonly IFleetOutbox _outbox;
    private readonly ILogger<VehicleStateChangedConsumer> _logger;

    public VehicleStateChangedConsumer(
        IVehicleRepository vehicleRepo,
        IChargingPolicyRepository policyRepo,
        IFleetOutbox outbox,
        ILogger<VehicleStateChangedConsumer> logger)
    {
        _vehicleRepo = vehicleRepo;
        _policyRepo = policyRepo;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VehicleStateChangedIntegrationEvent> context)
    {
        var evt = context.Message;

        var vehicle = await _vehicleRepo.GetByIdAsync(evt.VehicleId, context.CancellationToken);
        if (vehicle == null) return;

        var policy = await _policyRepo.GetByVehicleTypeAsync(vehicle.VehicleTypeId, context.CancellationToken);
        if (policy == null) return;

        if (policy.ShouldCharge(evt.BatteryLevel))
        {
            _logger.LogWarning("Vehicle {VehicleId} battery at {Battery}% — below threshold {Threshold}%",
                evt.VehicleId, evt.BatteryLevel, policy.LowThresholdPct);

            await _outbox.AddAsync(new VehicleBatteryLowIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow,
                evt.VehicleId, vehicle.VehicleTypeId, evt.BatteryLevel), context.CancellationToken);
            await _outbox.SaveChangesAsync(context.CancellationToken);
        }
    }
}
