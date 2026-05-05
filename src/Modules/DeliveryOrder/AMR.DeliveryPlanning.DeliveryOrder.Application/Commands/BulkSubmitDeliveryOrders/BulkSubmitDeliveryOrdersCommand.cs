using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public record BulkSubmitDeliveryOrdersCommand(List<CreateDraftDeliveryOrderCommand> Orders) : ICommand<List<Guid>>;
