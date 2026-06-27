using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.CancelTrip;

public record CancelTripCommand(Guid TripId, string Reason) : ICommand;
