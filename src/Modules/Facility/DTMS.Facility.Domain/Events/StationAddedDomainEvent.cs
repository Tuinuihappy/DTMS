using DTMS.SharedKernel.Domain;

namespace DTMS.Facility.Domain.Events;

public record StationAddedDomainEvent(Guid MapId, Guid StationId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
