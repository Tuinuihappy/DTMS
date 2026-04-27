namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

public enum OrderStatus
{
    Submitted,
    Validated,
    ReadyToPlan,
    Planning,
    Planned,
    Dispatched,
    InProgress,
    Completed,
    Held,
    Failed,
    Amended,
    Cancelled
}
