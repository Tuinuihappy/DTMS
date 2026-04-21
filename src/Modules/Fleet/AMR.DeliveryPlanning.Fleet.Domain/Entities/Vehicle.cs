using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public class Vehicle : AggregateRoot<Guid>
{
    public string VehicleName { get; private set; } = string.Empty;
    public Guid VehicleTypeId { get; private set; }
    public VehicleState State { get; private set; }
    public double BatteryLevel { get; private set; }
    public Guid? CurrentNodeId { get; private set; }

    private Vehicle() { }

    public Vehicle(Guid id, string vehicleName, Guid vehicleTypeId) : base(id)
    {
        VehicleName = vehicleName;
        VehicleTypeId = vehicleTypeId;
        State = VehicleState.Offline;
        BatteryLevel = 100.0;
        CurrentNodeId = null;

        AddDomainEvent(new VehicleRegisteredDomainEvent(this.Id, this.VehicleName));
    }

    public void UpdateState(VehicleState newState, double batteryLevel, Guid? currentNodeId)
    {
        var oldState = State;
        State = newState;
        BatteryLevel = batteryLevel;
        CurrentNodeId = currentNodeId;

        AddDomainEvent(new VehicleStateChangedDomainEvent(this.Id, oldState, newState, batteryLevel));
    }
}
