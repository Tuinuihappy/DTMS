using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetMapById;

public record GetMapByIdQuery(Guid MapId) : IQuery<MapDto>;
