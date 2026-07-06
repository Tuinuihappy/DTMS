using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.Dispatch.Application.Services;
using MediatR;

namespace DTMS.Api.Adapters;

/// <summary>
/// Composition-root bridge for Dispatch handlers that need to look up
/// the parent DeliveryOrder's current Status without taking a direct
/// reference on DeliveryOrder.Application. Routes through MediatR so
/// the existing GetDeliveryOrderQuery handler is reused intact.
/// </summary>
internal sealed class DeliveryOrderStatusReader : IDeliveryOrderStatusReader
{
    private readonly ISender _sender;

    public DeliveryOrderStatusReader(ISender sender)
    {
        _sender = sender;
    }

    public async Task<string?> GetStatusAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetDeliveryOrderQuery(orderId), cancellationToken);
        if (!result.IsSuccess || result.Value is null) return null;
        // DTO carries the enum; flatten to the same string representation
        // (.ToString()) the Trip-side guard expects. Avoids coupling
        // Dispatch.Application to DeliveryOrder.Domain just for the enum.
        return result.Value.OrderStatus.ToString();
    }

    public async Task<bool?> GetRequiresDropPodAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetDeliveryOrderQuery(orderId), cancellationToken);
        if (!result.IsSuccess || result.Value is null) return null;
        return result.Value.RequiresDropPod;
    }

    public async Task<string?> GetSourceSystemKeyAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetDeliveryOrderQuery(orderId), cancellationToken);
        if (!result.IsSuccess || result.Value is null) return null;
        // DTO exposes the slug as SourceSystem (mapped from order.SourceSystemKey).
        return result.Value.SourceSystem;
    }
}
