using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;

public record CommitPlanCommand(Guid JobId) : ICommand;
