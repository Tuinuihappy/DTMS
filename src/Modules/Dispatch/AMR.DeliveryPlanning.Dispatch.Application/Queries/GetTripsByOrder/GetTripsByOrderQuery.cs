using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripsByOrder;

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
    string Status,
    string UpperKey,
    string? VendorOrderKey,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
