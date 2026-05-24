using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public record BulkSubmitDeliveryOrdersCommand(List<CreateDraftDeliveryOrderCommand> Orders) : ICommand<BulkSubmitResult>;

public record BulkSubmitResult(
    List<BulkSubmitSuccess> Succeeded,
    List<BulkSubmitFailure> Failures)
{
    public List<Guid> SucceededIds => Succeeded.ConvertAll(s => s.OrderId);
}

public record BulkSubmitSuccess(Guid OrderId, IReadOnlyList<OrderQualityIssue> Warnings);

public record BulkSubmitFailure(string OrderRef, string Reason);
