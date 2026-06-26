using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.DeleteOrderTemplate;

public record DeleteOrderTemplateCommand(Guid Id) : ICommand;
