using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmItemPod;

public class ConfirmItemPodCommandHandler : ICommandHandler<ConfirmItemPodCommand, ConfirmItemPodResult>
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Barcode", "Manual", "Signature", "Confirm"
    };

    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ILogger<ConfirmItemPodCommandHandler> _logger;

    public ConfirmItemPodCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ILogger<ConfirmItemPodCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    public async Task<Result<ConfirmItemPodResult>> Handle(
        ConfirmItemPodCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ScannedBy))
            return Result<ConfirmItemPodResult>.Failure("ScannedBy is required.");
        if (!AllowedMethods.Contains(request.Method))
            return Result<ConfirmItemPodResult>.Failure(
                $"Method '{request.Method}' is not supported. Use one of: Barcode, Manual, Signature, Confirm.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ConfirmItemPodResult>.Failure($"Order {request.OrderId} not found.");

        var item = order.Items.FirstOrDefault(i => i.Id == request.ItemId);
        if (item is null)
            return Result<ConfirmItemPodResult>.Failure($"Item {request.ItemId} not part of order {request.OrderId}.");

        // Idempotency: a second scan against an already-Delivered item is
        // a no-op but still returns success so the UI doesn't have to
        // track "was already confirmed" state separately.
        if (item.Status == ItemStatus.Delivered)
        {
            return Result<ConfirmItemPodResult>.Success(new ConfirmItemPodResult(
                Confirmed: false, ItemStatus: item.Status.ToString(),
                OrderTerminalReached: order.Status is OrderStatus.Completed
                    or OrderStatus.PartiallyCompleted));
        }

        try
        {
            var changed = order.ConfirmItemPod(item.Id, request.ScannedBy, request.Method, request.Reference);
            if (changed == 0)
                return Result<ConfirmItemPodResult>.Failure(
                    $"Item is in {item.Status} state — POD confirmation is only valid from Picked or DroppedOff.");

            // Recompute lets the order finalize when this is the last
            // outstanding item (Completed / PartiallyCompleted).
            order.RecomputeStatusFromItems();

            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, "ItemPodConfirmed",
                $"Item {item.ItemId} POD-confirmed by {request.ScannedBy} (method={request.Method}" +
                (string.IsNullOrEmpty(request.Reference) ? "" : $", ref={request.Reference}") + ")"),
                cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[PodScan] Order {OrderId} Item {ItemId} ({ItemRef}) confirmed by {By} method={Method}",
                order.Id, item.Id, item.ItemId, request.ScannedBy, request.Method);

            return Result<ConfirmItemPodResult>.Success(new ConfirmItemPodResult(
                Confirmed: true,
                ItemStatus: item.Status.ToString(),
                OrderTerminalReached: order.Status is OrderStatus.Completed
                    or OrderStatus.PartiallyCompleted));
        }
        catch (InvalidOperationException ex)
        {
            return Result<ConfirmItemPodResult>.Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[PodScan] Concurrency conflict on Order {OrderId}.", request.OrderId);
            return Result<ConfirmItemPodResult>.Failure(
                "The order was modified by another process. Please retry.");
        }
    }
}
