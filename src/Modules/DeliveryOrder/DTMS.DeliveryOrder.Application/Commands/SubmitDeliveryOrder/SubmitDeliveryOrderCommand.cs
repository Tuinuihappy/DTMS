using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record SubmitDeliveryOrderCommand(Guid OrderId) : ICommand<SubmitDeliveryOrderResult>;

public record SubmitDeliveryOrderResult(
    Guid OrderId,
    IReadOnlyList<OrderQualityIssue> Warnings);
