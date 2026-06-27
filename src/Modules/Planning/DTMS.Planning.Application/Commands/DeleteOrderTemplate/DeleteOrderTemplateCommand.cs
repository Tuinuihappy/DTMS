using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.DeleteOrderTemplate;

public record DeleteOrderTemplateCommand(Guid Id) : ICommand;
