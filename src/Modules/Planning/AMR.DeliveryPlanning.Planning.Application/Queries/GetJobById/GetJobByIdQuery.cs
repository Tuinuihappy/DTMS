using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;

public record GetJobByIdQuery(Guid JobId) : IQuery<Job>;
