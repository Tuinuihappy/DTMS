using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReassignTrip;

public record ReassignTripCommand(Guid TripId, Guid NewVehicleId) : ICommand;
