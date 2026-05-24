using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record SubmitDeliveryOrderCommand(Guid OrderId) : ICommand<SubmitDeliveryOrderResult>;

public record SubmitDeliveryOrderResult(
    Guid OrderId,
    IReadOnlyList<OrderQualityIssue> Warnings);
