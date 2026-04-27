using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;

public record RaiseExceptionCommand(Guid TripId, string Code, string Severity, string Detail) : ICommand<Guid>;
