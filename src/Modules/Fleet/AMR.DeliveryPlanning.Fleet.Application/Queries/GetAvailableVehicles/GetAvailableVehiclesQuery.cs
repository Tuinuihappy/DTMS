using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Queries.GetAvailableVehicles;

public record GetAvailableVehiclesQuery() : IQuery<IReadOnlyList<VehicleDto>>;
