using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.ChargingPolicy;

public record UpsertChargingPolicyCommand(
    Guid VehicleTypeId,
    double LowThresholdPct,
    double TargetThresholdPct,
    ChargingMode Mode) : ICommand<Guid>;
