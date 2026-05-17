namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

public enum OrderStatus
{
    Draft,
    Submitted,  // reserved for async human-approval step between submit and validation
    Validated,  // reserved for async external-validation step before planning
    ReadyToPlan,
    Planning,
    Planned,
    Dispatched,
    InProgress,
    Completed,
    Held,
    Failed,
    Amended,   // reserved: set when order is amended after entering planning pipeline (ReadyToPlan→Dispatched)
               // requires Planning module consumer for DeliveryOrderAmendedIntegrationEvent + re-plan mechanism
    Cancelled
}
