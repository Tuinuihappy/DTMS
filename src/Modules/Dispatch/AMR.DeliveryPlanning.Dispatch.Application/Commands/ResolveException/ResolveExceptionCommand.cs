using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ResolveException;

public record ResolveExceptionCommand(Guid TripId, Guid ExceptionId, string Resolution, string ResolvedBy) : ICommand;
