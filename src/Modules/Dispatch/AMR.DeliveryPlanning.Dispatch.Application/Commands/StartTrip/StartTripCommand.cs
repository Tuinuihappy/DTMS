using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.StartTrip;

public record StartTripCommand(Guid TripId) : ICommand;
