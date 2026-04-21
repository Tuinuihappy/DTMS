using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;

public record DispatchTripCommand(Guid JobId, Guid VehicleId, Guid PickupStationId, Guid DropStationId) : ICommand<Guid>;
