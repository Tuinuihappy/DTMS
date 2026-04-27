using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public record BulkSubmitDeliveryOrdersCommand(List<SubmitDeliveryOrderCommand> Orders) : ICommand<List<Guid>>;
