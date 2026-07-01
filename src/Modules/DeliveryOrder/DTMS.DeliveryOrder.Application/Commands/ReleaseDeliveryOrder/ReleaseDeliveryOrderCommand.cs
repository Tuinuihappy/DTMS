using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;

// Release re-enters Confirmed and re-fires DeliveryOrderConfirmedIntegrationEvent, so the
// response shape mirrors the confirmation path: caller may want to surface the same quality
// warnings (e.g. missing weights) that they would have seen on the original confirmation.
//
// Phase P5 — Release used to import ConfirmDeliveryOrderResult from the retired Confirm
// command's folder. Now that the standalone Confirm command is gone (submit auto-confirms),
// the result record lives here in the folder of its only remaining caller.
public record ReleaseDeliveryOrderCommand(Guid OrderId, string? ReleasedBy = null)
    : ICommand<ReleaseDeliveryOrderResult>;

public record ReleaseDeliveryOrderResult(
    Guid OrderId,
    IReadOnlyList<OrderQualityIssue> Warnings);
