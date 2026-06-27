using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;

public record ConfirmDeliveryOrderCommand(Guid OrderId, string? ConfirmedBy = null)
    : ICommand<ConfirmDeliveryOrderResult>;

public record ConfirmDeliveryOrderResult(
    Guid OrderId,
    IReadOnlyList<OrderQualityIssue> Warnings);
