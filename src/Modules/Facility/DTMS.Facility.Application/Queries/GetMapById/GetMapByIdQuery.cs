using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Queries.GetMapById;

public record GetMapByIdQuery(Guid MapId) : IQuery<MapDto>;
