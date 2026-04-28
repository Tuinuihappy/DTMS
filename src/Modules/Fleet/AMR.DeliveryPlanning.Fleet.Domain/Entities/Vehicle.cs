using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.Fleet.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public class Vehicle : AggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public string VehicleName { get; private set; } = string.Empty;
    public Guid VehicleTypeId { get; private set; }
    public VehicleState State { get; private set; }
    public double BatteryLevel { get; private set; }
    public Guid? CurrentNodeId { get; private set; }
    public bool IsInMaintenance => State == VehicleState.Maintenance;
    // Identifies which vendor adapter handles this vehicle ("riot3" | "feeder" | "sim")
    public string AdapterKey { get; private set; } = "riot3";

    private Vehicle() { }

    public Vehicle(Guid id, Guid tenantId, string vehicleName, Guid vehicleTypeId, string adapterKey = "riot3") : base(id)
    {
        TenantId = tenantId;
        VehicleName = vehicleName;
        VehicleTypeId = vehicleTypeId;
        AdapterKey = adapterKey;
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

}
