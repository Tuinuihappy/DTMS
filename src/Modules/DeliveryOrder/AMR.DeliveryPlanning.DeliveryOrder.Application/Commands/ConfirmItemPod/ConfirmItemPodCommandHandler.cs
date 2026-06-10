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

        try
        {
            var changed = order.RecordItemPod(
                item.Id, request.ScanType, request.ScannedBy, request.Method, request.Reference);

            // changed == 0 covers two idempotent paths:
            //   • Same checkpoint already scanned (duplicate scan).
            //   • Drop scan against an item already Delivered.
            // Both are no-ops; the UI doesn't need to differentiate.
            if (changed == 0)
            {
                return Result<ConfirmItemPodResult>.Success(new ConfirmItemPodResult(
                    Confirmed: false, ItemStatus: item.Status.ToString(),
                    OrderTerminalReached: order.Status is OrderStatus.Completed
                        or OrderStatus.PartiallyCompleted));
            }

            // Drop scans can drive the order to a terminal state when this
            // is the last outstanding item. Pickup scans don't change
            // Item.Status so the recompute is a no-op but cheap.
            order.RecomputeStatusFromItems();

            var auditType = request.ScanType == PodScanType.Pickup
                ? "ItemPickupPodConfirmed"
                : "ItemDropPodConfirmed";
            await _auditRepo.AddAsync(new OrderAuditEvent(
                order.Id, auditType,
                $"Item {item.ItemId} {request.ScanType} POD recorded by {request.ScannedBy} (method={request.Method}" +
                (string.IsNullOrEmpty(request.Reference) ? "" : $", ref={request.Reference}") + ")"),
                cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[PodScan] Order {OrderId} Item {ItemId} ({ItemRef}) {ScanType} POD by {By} method={Method}",
                order.Id, item.Id, item.ItemId, request.ScanType, request.ScannedBy, request.Method);

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
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "[PodScan] Concurrency conflict on Order {OrderId}. Entries: {Entries}",
                request.OrderId,
                string.Join("; ", ex.Entries.Select(e => $"{e.Entity.GetType().Name}={e.State}")));
            return Result<ConfirmItemPodResult>.Failure(
                "The order was modified by another process. Please retry.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "[PodScan] Database update failed on Order {OrderId}. Inner: {Inner}",
                request.OrderId, ex.InnerException?.Message);
            return Result<ConfirmItemPodResult>.Failure(
                $"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }
    }
}
