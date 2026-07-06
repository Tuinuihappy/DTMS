using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// Shared guard for the source-system trip endpoints. A <c>Trip</c> carries
/// no source-system key of its own, so a machine caller's authority over a
/// trip is always derived from the parent <see cref="DeliveryOrder"/>'s
/// <c>SourceSystemKey</c>. This mirrors how the outbound callback fan-out
/// routes by <c>order.SourceSystem</c> and how the operator commands verify
/// operator ownership — the origin match is the machine-caller equivalent.
/// </summary>
internal static class SourceTripOriginAuthorizer
{
    /// <summary>
    /// Loads the trip and confirms <paramref name="callerSourceSystemKey"/>
    /// (pinned from the authenticated <c>SystemPrincipal</c>, never the wire
    /// body) owns it. On success the trip is returned; on any mismatch a
    /// <see cref="Result{Trip}"/> failure the handler can return verbatim.
    /// </summary>
    public static async Task<Result<Trip>> ResolveAsync(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        Guid tripId,
        string callerSourceSystemKey,
        CancellationToken ct)
    {
        var trip = await trips.GetByIdAsync(tripId, ct);
        if (trip is null)
            return Result<Trip>.Failure($"Trip {tripId} not found.");

        var ownerKey = await orders.GetSourceSystemKeyAsync(trip.DeliveryOrderId, ct);
        if (ownerKey is null)
            return Result<Trip>.Failure(
                $"Parent order {trip.DeliveryOrderId} for trip {tripId} not found.");

        if (!string.Equals(ownerKey, callerSourceSystemKey, StringComparison.OrdinalIgnoreCase))
            return Result<Trip>.Failure(
                $"Trip {tripId} belongs to source system '{ownerKey}', " +
                $"not '{callerSourceSystemKey}' — caller may only act on its own trips.");

        return Result<Trip>.Success(trip);
    }
}
