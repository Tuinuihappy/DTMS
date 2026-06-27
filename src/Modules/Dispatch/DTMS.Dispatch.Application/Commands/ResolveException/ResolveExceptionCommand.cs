using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ResolveException;

public record ResolveExceptionCommand(Guid TripId, Guid ExceptionId, string Resolution, string ResolvedBy) : ICommand;
