using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;

public record ReplanJobCommand(Guid JobId, string Reason) : ICommand;
