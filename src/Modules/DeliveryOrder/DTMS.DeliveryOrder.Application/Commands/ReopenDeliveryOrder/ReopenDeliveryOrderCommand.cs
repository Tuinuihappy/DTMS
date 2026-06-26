using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;

/// <summary>
/// Move a Failed delivery order back to Confirmed so an operator can
/// trigger a Trip-level retry. Admin action — audited via the
/// "OrderReopened" event on the audit trail. Does not auto-retry any
/// trips; the operator must call /trips/{id}/retry separately.
/// </summary>
public record ReopenDeliveryOrderCommand(
    Guid OrderId,
    string ReopenedBy,
    string Reason
) : ICommand;
