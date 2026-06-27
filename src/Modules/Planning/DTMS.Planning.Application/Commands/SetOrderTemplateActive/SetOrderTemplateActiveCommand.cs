using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.SetOrderTemplateActive;

public record SetOrderTemplateActiveCommand(Guid Id, bool IsActive) : ICommand;
