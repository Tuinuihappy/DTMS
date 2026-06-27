using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetJobStatusHistory;

public record GetJobStatusHistoryQuery(Guid JobId) : IQuery<JobStatusHistoryResponse>;

public record JobStatusHistoryEntryDto(
    Guid EventId,
    Guid JobId,
    Guid DeliveryOrderId,
    string? FromStatus,
    string ToStatus,
    DateTime OccurredAt,
    string? Reason);

public record JobStatusHistoryResponse(
    Guid JobId,
    IReadOnlyList<JobStatusHistoryEntryDto> Entries,
    DateTime? LastEventAt);
