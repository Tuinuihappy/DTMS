namespace AMR.DeliveryPlanning.Planning.Application.Projections;

public interface IJobFactsReadRepository
{
    Task<IReadOnlyList<JobFactsEntry>> QueryAsync(
        JobFactsFilters filters, CancellationToken ct);

    Task<int> CountAsync(JobFactsFilters filters, CancellationToken ct);
}

public record JobFactsFilters(
    DateTime? FromCreatedAtUtc,
    DateTime? ToCreatedAtUtc,
    string? FinalStatus,
    int? MinAttemptNumber,
    int Limit = 50_000);

public record JobFactsEntry(
    Guid JobId,
    Guid DeliveryOrderId,
    Guid? AssignedVehicleId,
    Guid? LatestTripId,
    string? VendorOrderKey,
    string FinalStatus,
    string? FailureReason,
    int AttemptNumber,
    DateTime CreatedAt,
    DateTime? AssignedAt,
    DateTime? CommittedAt,
    DateTime? DispatchedAt,
    DateTime? ExecutingAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    DateTime? CancelledAt,
    int? TimeToDispatchSec,
    int? TimeToCompleteSec,
    bool? SlaDispatchBreached,
    DateTime UpdatedAt);
