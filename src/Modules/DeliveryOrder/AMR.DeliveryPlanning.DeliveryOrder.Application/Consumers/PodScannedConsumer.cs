using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Listens for PodCapturedIntegrationEvent and matches scanned barcodes to OrderItems.
/// Flow: Dispatch (POD scanned) → DeliveryOrder (OrderItem statuses updated to Delivered)
/// </summary>
public class PodScannedConsumer : IConsumer<PodCapturedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<PodScannedConsumer> _logger;

    public PodScannedConsumer(IDeliveryOrderRepository repository, ILogger<PodScannedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PodCapturedIntegrationEvent> context)
    {
        var evt = context.Message;

        if (evt.ScannedIds is null || evt.ScannedIds.Count == 0)
        {
            _logger.LogInformation("[PodScan] Trip {TripId} POD captured with no scanned IDs — skipping barcode match.", evt.TripId);
            return;
        }

        _logger.LogInformation("[PodScan] Trip {TripId}: matching {Count} scanned barcode(s) to OrderItems.",
            evt.TripId, evt.ScannedIds.Count);

        var orders = await _repository.GetOrdersByItemSkusAsync(evt.ScannedIds, context.CancellationToken);

        if (orders.Count == 0)
        {
            _logger.LogWarning("[PodScan] Trip {TripId}: no OrderItems matched scanned barcodes {Barcodes}.",
                evt.TripId, string.Join(", ", evt.ScannedIds));
            return;
        }

        foreach (var chunk in orders.Chunk(50))
        {
            foreach (var order in chunk)
            {
                order.MarkItemsDelivered(evt.ScannedIds);
                _logger.LogInformation("[PodScan] Updated item statuses on Order {OrderId}.", order.Id);
            }

            try
            {
                await _repository.SaveChangesAsync(context.CancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning("[PodScan] Concurrency conflict in batch for Trip {TripId}. MassTransit will retry.", evt.TripId);
                throw;
            }
        }

        _logger.LogInformation("[PodScan] ✓ Barcode matching complete for Trip {TripId} — {OrderCount} order(s) updated.",
            evt.TripId, orders.Count);
    }
}
