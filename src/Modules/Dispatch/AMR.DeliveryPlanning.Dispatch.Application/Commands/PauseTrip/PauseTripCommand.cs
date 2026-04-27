using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;

public record PauseTripCommand(Guid TripId) : ICommand;
