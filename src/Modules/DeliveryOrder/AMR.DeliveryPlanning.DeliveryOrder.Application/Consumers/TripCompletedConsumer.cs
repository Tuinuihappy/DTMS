using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Consumers;

/// <summary>
/// Finalizes a DeliveryOrder when its Trip completes. Branches on
/// VendorUpperKey:
///   non-null (envelope flow) → MarkVendorCompleted (vendor-authoritative,
///       marks all items Delivered then moves order to Completed)
///   null (legacy flow)       → MarkAsCompleted (reads POD-driven item
///       statuses; may yield Completed / PartiallyCompleted / Failed)
///
/// Invariant for legacy: POD scan events must arrive BEFORE TripCompleted
/// for the finalization to read the right item state.
/// </summary>
public class TripCompletedConsumer : IConsumer<TripCompletedIntegrationEvent>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILogger<TripCompletedConsumer> _logger;

    public TripCompletedConsumer(IDeliveryOrderRepository repository, ILogger<TripCompletedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
    {
        var evt = context.Message;
        var isEnvelope = !string.IsNullOrEmpty(evt.VendorUpperKey);
        _logger.LogInformation(
            "Received TripCompleted event for Trip {TripId}, Job {JobId} (envelope: {Envelope}, vendorUpperKey: {UpperKey})",
            evt.TripId, evt.JobId, isEnvelope, evt.VendorUpperKey ?? "(none)");

        var order = await _repository.GetByIdAsync(evt.DeliveryOrderId, context.CancellationToken);
        if (order is null)
        {
            _logger.LogWarning("No DeliveryOrder found for DeliveryOrderId {DeliveryOrderId} (TripId {TripId}). Skipping.", evt.DeliveryOrderId, evt.TripId);
            return;
        }

        try
        {
            if (isEnvelope)
                order.MarkVendorCompleted();
            else
                order.MarkAsCompleted();

            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("DeliveryOrder {OrderId} finalized as {FinalStatus} via Trip {TripId} ({Flow})",
                order.Id, order.Status, evt.TripId, isEnvelope ? "envelope" : "legacy");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Cannot finalize DeliveryOrder {OrderId}: {Message}", order.Id, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict finalizing DeliveryOrder {OrderId}. MassTransit will retry.", order.Id);
            throw;
        }
    }
}
