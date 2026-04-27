using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;

public record CancelTripCommand(Guid TripId, string Reason) : ICommand;
