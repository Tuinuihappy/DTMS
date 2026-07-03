using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetPoolTrips;

// WMS PR-4b (PR-D) — GET /api/operator/trips/pool
//
// Returns Manual/Fleet trips currently in the shared pool, FIFO-ordered
// by DispatchedAt (oldest first). Universal visibility — no operator zone
// or warehouse filter (per the P4b design decision "ทุกคนทำได้ทุกที่").
//
// Predicate:
//   Trip.Status = 'Created'
//   AND Trip.DispatchedAt IS NOT NULL
//   AND Trip.ClaimedByOperatorId IS NULL
// Covered by partial index dispatch."IX_Trips_Pool".
public record GetPoolTripsQuery : IQuery<IReadOnlyList<PoolTripDto>>;

// Wire shape the operator PWA card renders. Kept flat (no nested item
// objects) so SignalR broadcasts serialize the same DTO shape used by
// the REST list — the frontend reducer treats an ADDED event exactly
// like a fresh REST row.
public sealed record PoolTripDto(
    Guid TripId,
    Guid DeliveryOrderId,
    string OrderRef,
    string PickupCode,
    string DropCode,
    int ItemCount,
    double TotalWeightKg,
    DateTime DispatchedAt,
    int? Priority);
