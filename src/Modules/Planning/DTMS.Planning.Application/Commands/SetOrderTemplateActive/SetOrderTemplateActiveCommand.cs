using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.SetOrderTemplateActive;

public record SetOrderTemplateActiveCommand(Guid Id, bool IsActive) : ICommand;
