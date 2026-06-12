using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripStatusHistory;

public record GetTripStatusHistoryQuery(Guid TripId) : IQuery<TripStatusHistoryResponse>;

public record TripStatusHistoryEntryDto(
    Guid EventId,
    Guid TripId,
    Guid? DeliveryOrderId,
    Guid? JobId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public record TripStatusHistoryResponse(
    Guid TripId,
    IReadOnlyList<TripStatusHistoryEntryDto> Entries,
    DateTime? LastEventAt);
