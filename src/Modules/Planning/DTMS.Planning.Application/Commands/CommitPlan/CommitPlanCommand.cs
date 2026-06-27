using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.CommitPlan;

public record CommitPlanCommand(Guid JobId) : ICommand;
