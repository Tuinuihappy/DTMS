using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// Shared guard for the source-system trip endpoints. A <c>Trip</c> carries
/// no source-system key of its own, so a machine caller's authority over a
/// trip is always derived from the parent <see cref="DeliveryOrder"/>'s
/// <c>SourceSystemKey</c>. This mirrors how the outbound callback fan-out
/// routes by <c>order.SourceSystem</c> and how the operator commands verify
/// operator ownership — the origin match is the machine-caller equivalent.
///
/// <para>Throws rather than returning a failure so the three outcomes reach the
/// caller as three different statuses. They used to collapse into one 400,
/// which told a partner nothing about whether to fix the id, fix its token, or
/// call us.</para>
/// </summary>
internal static class SourceTripOriginAuthorizer
{
    /// <summary>
    /// Loads the trip and confirms <paramref name="callerSourceSystemKey"/>
    /// (pinned from the authenticated <c>SystemPrincipal</c>, never the wire
    /// body) owns it.
    /// </summary>
    /// <exception cref="NotFoundException">No such trip — 404.</exception>
    /// <exception cref="ForbiddenException">Someone else's trip — 403.</exception>
    /// <exception cref="InvalidOperationException">
    /// The trip's parent order is missing — 500. That is our data being wrong,
    /// not the caller's request.
    /// </exception>
    public static async Task<Trip> ResolveAsync(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        Guid tripId,
        string callerSourceSystemKey,
        CancellationToken ct)
    {
        var trip = await trips.GetByIdAsync(tripId, ct);
        if (trip is null)
            throw new NotFoundException($"Trip {tripId} not found.");

        var ownerKey = await orders.GetSourceSystemKeyAsync(trip.DeliveryOrderId, ct);
        if (ownerKey is null)
            // A trip whose parent order has vanished is a data fault. Surfacing
            // it as a scrubbed 500 with a traceId is honest; telling the caller
            // they sent something wrong is not.
            throw new InvalidOperationException(
                $"Parent order {trip.DeliveryOrderId} for trip {tripId} not found.");

        if (!string.Equals(ownerKey, callerSourceSystemKey, StringComparison.OrdinalIgnoreCase))
            // Naming the owner is safe here and saves a support round-trip: a
            // trip id is an unguessable guid, so a caller able to ask this
            // question already had the id. The source ORDER route deliberately
            // does the opposite — an OrderRef can be enumerated, so there a
            // foreign resource is simply "not found".
            throw new ForbiddenException(
                $"Trip {tripId} belongs to source system '{ownerKey}', " +
                $"not '{callerSourceSystemKey}' — caller may only act on its own trips.");

        return trip;
    }
}
