using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public record ConfirmDeliveryOrderCommand(Guid OrderId, string? ConfirmedBy = null)
    : ICommand<ConfirmDeliveryOrderResult>;

public record ConfirmDeliveryOrderResult(
    Guid OrderId,
    IReadOnlyList<OrderQualityIssue> Warnings);
