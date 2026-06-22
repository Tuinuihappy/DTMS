using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkCancelDeliveryOrders;

// Sequential dispatch of CancelDeliveryOrderCommand per id so the
// per-row Result semantics + domain invariants are preserved exactly.
// No batching at the DB layer — each cancel is its own transaction
// (matches the single-cancel path), trading throughput for clarity
// when the batch is mixed-state (some Submitted, some already
// Cancelled, some Active).
//
// Failures don't short-circuit: every id is attempted so the operator
// gets one consolidated 207 response instead of cherry-picking through
// N retries. Empty input → 400 at the endpoint layer before we get here.
public class BulkCancelDeliveryOrdersCommandHandler
    : ICommandHandler<BulkCancelDeliveryOrdersCommand, BulkCancelResult>
{
    private readonly ISender _sender;
    private readonly ILogger<BulkCancelDeliveryOrdersCommandHandler> _logger;

    public BulkCancelDeliveryOrdersCommandHandler(
        ISender sender,
        ILogger<BulkCancelDeliveryOrdersCommandHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<BulkCancelResult>> Handle(
        BulkCancelDeliveryOrdersCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrderIds is null || request.OrderIds.Count == 0)
            return Result<BulkCancelResult>.Failure("OrderIds is required and must not be empty.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<BulkCancelResult>.Failure("Reason is required.");

        var succeeded = new List<Guid>(request.OrderIds.Count);
        var failures = new List<BulkCancelFailure>();
        // Dedup so a UI that sent the same id twice doesn't double-attempt
        // and produce a confusing "already cancelled" failure on the
        // second pass.
        var seen = new HashSet<Guid>();

        foreach (var orderId in request.OrderIds)
        {
            if (!seen.Add(orderId)) continue;

            var result = await _sender.Send(
                new CancelDeliveryOrderCommand(orderId, request.Reason),
                cancellationToken);

            if (result.IsSuccess)
                succeeded.Add(orderId);
            else
                failures.Add(new BulkCancelFailure(orderId, result.Error ?? "Cancel failed."));
        }

        _logger.LogInformation(
            "[BulkCancel] Orders: {SucceededCount} cancelled, {FailedCount} failed. Reason: {Reason}.",
            succeeded.Count, failures.Count, request.Reason);

        return Result<BulkCancelResult>.Success(new BulkCancelResult(succeeded, failures));
    }
}
