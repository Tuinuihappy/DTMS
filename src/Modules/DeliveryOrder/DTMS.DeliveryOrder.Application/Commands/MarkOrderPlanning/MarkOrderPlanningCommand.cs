using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;

/// <summary>
/// Confirmed → Planning. Fired by the Planning consumer right when it
/// starts processing the DeliveryOrderConfirmedIntegrationEvent. The
/// domain method is idempotent so RabbitMQ redeliveries are safe.
/// </summary>
public record MarkOrderPlanningCommand(Guid OrderId) : ICommand;
