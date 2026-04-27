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
    public bool IsInMaintenance => State == VehicleState.Maintenance;

    private readonly List<Guid> _groupIds = new();
    public IReadOnlyCollection<Guid> GroupIds => _groupIds.AsReadOnly();

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
        if (IsInMaintenance && newState != VehicleState.Maintenance)
            throw new InvalidOperationException("Vehicle is in maintenance. Complete maintenance before changing state.");

        var oldState = State;
        State = newState;
        BatteryLevel = batteryLevel;
        CurrentNodeId = currentNodeId;

        AddDomainEvent(new VehicleStateChangedDomainEvent(this.Id, oldState, newState, batteryLevel));
    }

    public void EnterMaintenance()
    {
        if (IsInMaintenance) throw new InvalidOperationException("Vehicle is already in maintenance.");
        var oldState = State;
        State = VehicleState.Maintenance;
        AddDomainEvent(new VehicleMaintenanceEnteredDomainEvent(Id, oldState));
    }

    public void ExitMaintenance()
    {
        if (!IsInMaintenance) throw new InvalidOperationException("Vehicle is not in maintenance.");
        State = VehicleState.Idle;
        AddDomainEvent(new VehicleMaintenanceExitedDomainEvent(Id));
    }

    public void AddToGroup(Guid groupId)
    {
        if (!_groupIds.Contains(groupId)) _groupIds.Add(groupId);
    }

    public void RemoveFromGroup(Guid groupId) => _groupIds.Remove(groupId);
}
