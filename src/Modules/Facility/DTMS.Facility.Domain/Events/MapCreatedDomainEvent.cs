using DTMS.SharedKernel.Domain;

namespace DTMS.Facility.Domain.Events;

public record MapCreatedDomainEvent(Guid MapId, string MapName) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
