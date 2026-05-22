namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

public enum OrderStatus
{
    Draft,
    Submitted,
    Validated,
    Confirmed,   // business/human approval (manual) or auto (upstream) — also the planning trigger
    Planning,
    Planned,
    Dispatched,
    InProgress,
    Completed,
    Held,
    Failed,
    Amended,     // reserved: set when order is amended after entering planning pipeline (ReadyToPlan→Dispatched)
                 // requires Planning module consumer for DeliveryOrderAmendedIntegrationEvent + re-plan mechanism
    Cancelled,
    Rejected     // terminal: reject after Submitted/Validated/Confirmed (distinct from user-driven Cancelled)
}
