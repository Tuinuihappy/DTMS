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
    // External robot identity used by the vendor adapter, for example RIOT3 deviceKey.
    public string? VendorVehicleKey { get; private set; }

    private Vehicle() { }

    public Vehicle(Guid id, Guid tenantId, string vehicleName, Guid vehicleTypeId, string adapterKey = "riot3", string? vendorVehicleKey = null) : base(id)
    {
        TenantId = tenantId;
        VehicleName = vehicleName;
        VehicleTypeId = vehicleTypeId;
        AdapterKey = NormalizeAdapterKey(adapterKey);
        VendorVehicleKey = NormalizeVendorVehicleKey(vendorVehicleKey);
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

        AddDomainEvent(new VehicleStateChangedDomainEvent(this.Id, VehicleTypeId, oldState, newState, batteryLevel, currentNodeId));
    }

    public void EnterMaintenance(Guid maintenanceRecordId)
    {
        if (IsInMaintenance) throw new InvalidOperationException("Vehicle is already in maintenance.");
        var oldState = State;
        State = VehicleState.Maintenance;
        AddDomainEvent(new VehicleMaintenanceEnteredDomainEvent(Id, maintenanceRecordId, oldState));
    }

    public void ExitMaintenance()
    {
        if (!IsInMaintenance) throw new InvalidOperationException("Vehicle is not in maintenance.");
        State = VehicleState.Idle;
        AddDomainEvent(new VehicleMaintenanceExitedDomainEvent(Id));
    }

    private static string NormalizeAdapterKey(string? adapterKey)
        => string.IsNullOrWhiteSpace(adapterKey) ? "riot3" : adapterKey.Trim().ToLowerInvariant();

    private static string? NormalizeVendorVehicleKey(string? vendorVehicleKey)
        => string.IsNullOrWhiteSpace(vendorVehicleKey) ? null : vendorVehicleKey.Trim();
}
