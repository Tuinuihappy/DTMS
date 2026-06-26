using DTMS.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Queries.Admin;

// Phase 4.6 — Override approval queue feed. Pending only — decided
// requests stay in the table for audit but don't surface here.
// Sorted oldest first so dispatcher tackles the longest-waiting one
// before it hits the auto-expiry deadline.
public record ListPendingOverridesQuery() : IQuery<IReadOnlyList<OverrideQueueDto>>;

public record OverrideQueueDto(
    Guid Id,
    Guid OperatorId,
    Guid TripId,
    Guid ExpectedWarehouseId,
    double ReportedLatitude,
    double ReportedLongitude,
    double DistanceFromGeofenceM,
    string Reason,
    string? PhotoUrl,
    OverrideRequestStatus Status,
    DateTime RequestedAt,
    DateTime ExpiresAt);

internal sealed class ListPendingOverridesQueryHandler : IQueryHandler<ListPendingOverridesQuery, IReadOnlyList<OverrideQueueDto>>
{
    private readonly IGeofenceOverrideRequestRepository _overrides;
    public ListPendingOverridesQueryHandler(IGeofenceOverrideRequestRepository overrides)
        => _overrides = overrides;

    public async Task<Result<IReadOnlyList<OverrideQueueDto>>> Handle(
        ListPendingOverridesQuery request, CancellationToken cancellationToken)
    {
        var pending = await _overrides.ListPendingAsync(cancellationToken);
        var dtos = pending.Select(r => new OverrideQueueDto(
            r.Id, r.OperatorId, r.TripId, r.ExpectedWarehouseId,
            r.ReportedLatitude, r.ReportedLongitude, r.DistanceFromGeofenceM,
            r.Reason, r.PhotoUrl, r.Status, r.RequestedAt, r.ExpiresAt)).ToList();
        return Result<IReadOnlyList<OverrideQueueDto>>.Success(dtos);
    }
}
