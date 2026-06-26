using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanned;

/// <summary>
/// Planning → Planned. Fired by the Planning consumer after the groups
/// and templates have been resolved, before the first vendor call.
/// </summary>
public record MarkOrderPlannedCommand(Guid OrderId) : ICommand;
