using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ResumeTrip;

public record ResumeTripCommand(Guid TripId) : ICommand;
