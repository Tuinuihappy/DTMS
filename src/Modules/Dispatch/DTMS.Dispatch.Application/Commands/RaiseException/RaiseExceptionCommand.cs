using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.RaiseException;

public record RaiseExceptionCommand(Guid TripId, string Code, string Severity, string Detail) : ICommand<Guid>;
