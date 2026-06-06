using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderDispatched;

/// <summary>
/// Planned → Dispatched. Fired by the Planning consumer after at least
/// one group's vendor dispatch succeeded. Order is now "in vendor hands"
/// and the next signal will come from the TASK_PROCESSING webhook.
/// </summary>
public record MarkOrderDispatchedCommand(Guid OrderId) : ICommand;
