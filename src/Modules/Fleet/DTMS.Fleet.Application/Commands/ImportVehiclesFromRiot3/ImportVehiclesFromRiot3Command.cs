using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.ImportVehiclesFromRiot3;

public record ImportVehiclesFromRiot3Command(
    Dictionary<string, Guid> TypeKeyMappings,
    Guid? DefaultVehicleTypeId) : ICommand<ImportVehiclesFromRiot3Result>;

public record ImportVehiclesFromRiot3Result(
    int Imported,
    int Skipped,
    List<ImportedVehicleDetail> Details);

public record ImportedVehicleDetail(
    string DeviceKey,
    string DeviceName,
    string Status,
    Guid? VehicleId = null);
