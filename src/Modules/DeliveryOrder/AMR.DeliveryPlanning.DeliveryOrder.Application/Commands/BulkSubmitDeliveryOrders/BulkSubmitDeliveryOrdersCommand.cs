using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public record BulkSubmitDeliveryOrdersCommand(List<CreateDraftDeliveryOrderCommand> Orders) : ICommand<BulkSubmitResult>;

public record BulkSubmitResult(List<Guid> SucceededIds, List<BulkSubmitFailure> Failures);

public record BulkSubmitFailure(string OrderRef, string Reason);
