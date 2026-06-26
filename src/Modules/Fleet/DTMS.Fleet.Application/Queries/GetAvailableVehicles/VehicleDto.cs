using DTMS.Fleet.Domain.Enums;

namespace DTMS.Fleet.Application.Queries.GetAvailableVehicles;

public record VehicleDto(
    Guid Id,
    string VehicleName,
    Guid VehicleTypeId,
    string AdapterKey,
    string? VendorVehicleKey,
    VehicleState State,
    double BatteryLevel,
    Guid? CurrentNodeId);
