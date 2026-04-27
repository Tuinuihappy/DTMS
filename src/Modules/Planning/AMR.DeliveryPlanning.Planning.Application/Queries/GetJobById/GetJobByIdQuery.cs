using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

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
    int LegCount)
{
    public static JobDto From(Job job) => new(
        job.Id, job.DeliveryOrderId,
        job.Status.ToString(), job.Pattern.ToString(),
        job.AssignedVehicleId, job.EstimatedDuration, job.EstimatedDistance,
        job.RequiredCapability, job.SlaDeadline, job.PlanningTrace,
        job.Legs.Count);
}
