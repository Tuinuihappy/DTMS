using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetAvailableVehicles;

public record GetAvailableVehiclesQuery() : IQuery<IReadOnlyList<VehicleDto>>;
