using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Events;

public record VehicleRegisteredDomainEvent(Guid VehicleId, string VehicleName) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record VehicleStateChangedDomainEvent(Guid VehicleId, VehicleState OldState, VehicleState NewState, double BatteryLevel) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record VehicleMaintenanceEnteredDomainEvent(Guid VehicleId, VehicleState PreviousState) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record VehicleMaintenanceExitedDomainEvent(Guid VehicleId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record VehicleBatteryLowDomainEvent(Guid VehicleId, double BatteryLevel) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
