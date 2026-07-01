using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public record BulkSubmitDeliveryOrdersCommand(List<CreateDraftDeliveryOrderCommand> Orders) : ICommand<BulkSubmitResult>;

public record BulkSubmitResult(
    List<BulkSubmitSuccess> Succeeded,
    List<BulkSubmitFailure> Failures)
{
    public List<Guid> SucceededIds => Succeeded.ConvertAll(s => s.Order.Id);
}

// Phase P5 — each success now carries the full order detail (mirroring
// the single-submit and system-path shapes). Frontends that just want
// the id list can pull it from BulkSubmitResult.SucceededIds; consumers
// that want to skip a follow-up GET have the confirmed order in hand.
public record BulkSubmitSuccess(
    DeliveryOrderDetailDto Order,
    IReadOnlyList<OrderQualityIssue> Warnings);

public record BulkSubmitFailure(string OrderRef, string Reason);
