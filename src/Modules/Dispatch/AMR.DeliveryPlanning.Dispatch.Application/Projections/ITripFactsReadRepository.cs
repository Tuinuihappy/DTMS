namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

public interface ITripFactsReadRepository
{
    Task<IReadOnlyList<TripFactsEntry>> QueryAsync(
        TripFactsFilters filters, CancellationToken ct);

    Task<int> CountAsync(TripFactsFilters filters, CancellationToken ct);
}

public record TripFactsFilters(
    DateTime? FromCreatedAtUtc,
    DateTime? ToCreatedAtUtc,
    string? VendorUpperKey,
    string? FinalStatus,
    int Limit = 50_000);

public record TripFactsEntry(
    Guid TripId,
    Guid? DeliveryOrderId,
    Guid? JobId,
    Guid? VehicleId,
    string? VendorUpperKey,
    string FinalStatus,
    string? FailureReason,
    int PauseCount,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FirstPausedAt,
    DateTime? LastResumedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    DateTime? CancelledAt,
    int? TimeToStartSec,
    int? TimeToCompleteSec,
    bool? SlaCompleteBreached,
    DateTime UpdatedAt);
