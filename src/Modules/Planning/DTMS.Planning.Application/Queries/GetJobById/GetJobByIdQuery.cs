using AMR.DeliveryPlanning.Planning.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;

public record GetJobByIdQuery(Guid JobId) : IQuery<JobDto>;

public record JobDto(
    Guid Id,
    Guid DeliveryOrderId,
    string Status,
    string Pattern,
    Guid? AssignedVehicleId,
    double EstimatedDuration,
    double EstimatedDistance,
    string? RequiredCapability,
    DateTime? SlaDeadline,
    string? PlanningTrace,
    int LegCount,
    string? TransportMode,
    // Phase b8/b9 — envelope-anchor + Trip-lifecycle fields. Null for
    // legacy manual-Job rows (no envelope anchor was set).
    int? GroupIndex,
    Guid? PickupStationId,
    Guid? DropStationId,
    Guid? TripId,
    string? VendorOrderKey,
    string? FailureReason,
    int AttemptNumber)
{
    public static JobDto From(Job job) => new(
        job.Id, job.DeliveryOrderId,
        job.Status.ToString(), job.Pattern.ToString(),
        job.AssignedVehicleId, job.EstimatedDuration, job.EstimatedDistance,
        job.RequiredCapability, job.SlaDeadline, job.PlanningTrace,
        job.Legs.Count, job.TransportMode,
        job.GroupIndex == 1 && job.PickupStationId is null ? null : job.GroupIndex,
        job.PickupStationId, job.DropStationId,
        job.TripId, job.VendorOrderKey, job.FailureReason, job.AttemptNumber);
}
