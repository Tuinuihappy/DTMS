using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetTripsByOrder;

/// <summary>
/// Lists every Trip belonging to a delivery order (all attempts, all
/// statuses) so the operator UI can drill from an order into the
/// dispatch lineage. Sorted by AttemptNumber asc — older attempts
/// first, latest at the bottom.
/// </summary>
public record GetTripsByOrderQuery(Guid OrderId) : IQuery<List<TripSummaryDto>>;

public sealed record TripSummaryDto(
    Guid Id,
    Guid DeliveryOrderId,
    // Phase b8 — populated for envelope trips dispatched after Job
    // 1:1 anchoring landed. Pre-b8 rows carry Guid.Empty here; the UI
    // should treat that as "no Job link" and not show the chip.
    Guid JobId,
    string Status,
    string UpperKey,
    string? VendorOrderKey,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
