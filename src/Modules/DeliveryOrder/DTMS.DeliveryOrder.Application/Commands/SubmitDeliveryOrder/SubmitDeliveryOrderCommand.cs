using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

public record SubmitDeliveryOrderCommand(Guid OrderId) : ICommand<SubmitDeliveryOrderResult>;

// Phase P5 — SubmitDeliveryOrderResult mirrors the shape of
// UpstreamOrderAckDto used by the system path: full order detail plus
// weight warnings. The submit handler auto-confirms in the same
// transaction so callers get the final Confirmed order without a
// follow-up GET.
public record SubmitDeliveryOrderResult(
    DeliveryOrderDetailDto Order,
    IReadOnlyList<OrderQualityIssue> Warnings);
