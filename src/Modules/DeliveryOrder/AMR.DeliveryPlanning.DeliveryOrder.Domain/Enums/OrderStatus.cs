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
    PartiallyCompleted,   // ≥1 item Delivered AND ≥1 item not-Delivered when trip finalized — terminal
    Held,
    Failed,
    // NB: there's no `Amended` status value — amendments don't change
    // an order's headline state. Amendment flow emits
    // DeliveryOrderAmendedIntegrationEvent + writes an OrderAmendment
    // history row + projects an "Amended" string entry on the activity
    // timeline; the order's Status stays whatever it was (Confirmed /
    // Dispatched / etc.). Removed in Phase b13 cleanup.
    Cancelled,
    Rejected     // terminal: reject after Submitted/Validated/Confirmed (distinct from user-driven Cancelled)
}
