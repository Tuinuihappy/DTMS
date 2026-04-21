using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetPendingJobs;

public record GetPendingJobsQuery() : IQuery<List<Job>>;
