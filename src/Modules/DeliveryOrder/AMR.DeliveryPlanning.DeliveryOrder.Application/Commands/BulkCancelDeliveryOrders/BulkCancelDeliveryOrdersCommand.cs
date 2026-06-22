using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkCancelDeliveryOrders;

// Backend Phase 2 — operator-driven bulk cancel from the orders table.
// Mirrors the BulkSubmit shape: per-id partial-failure result with a 207
// Multi-Status response when at least one row failed. The caller picks
// every OrderId from the UI selection; Reason is shared across the batch
// (the dialog forces a single reason for the lot — matches single-cancel
// semantics).
public record BulkCancelDeliveryOrdersCommand(List<Guid> OrderIds, string Reason)
    : ICommand<BulkCancelResult>;

public record BulkCancelResult(
    List<Guid> Succeeded,
    List<BulkCancelFailure> Failures);

public record BulkCancelFailure(Guid OrderId, string Reason);
