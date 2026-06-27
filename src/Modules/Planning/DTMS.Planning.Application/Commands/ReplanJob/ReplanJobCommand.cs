using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.ReplanJob;

public record ReplanJobCommand(Guid JobId, string Reason) : ICommand;
