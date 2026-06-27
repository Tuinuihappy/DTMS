using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.PauseTrip;

public record PauseTripCommand(Guid TripId) : ICommand;
