using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Events;

public record TopologyOverlayActivatedDomainEvent(Guid EventId, DateTime OccurredOn, Guid MapId, Guid OverlayId, string OverlayType) : IDomainEvent;
public record TopologyOverlayExpiredDomainEvent(Guid EventId, DateTime OccurredOn, Guid MapId, Guid OverlayId) : IDomainEvent;
public record FacilityResourceCommandedDomainEvent(Guid EventId, DateTime OccurredOn, Guid ResourceId, string Command) : IDomainEvent;
