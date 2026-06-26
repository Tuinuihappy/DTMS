using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace DTMS.Facility.Domain.Events;

// Domain events emitted by the Warehouse aggregate. Used by:
//   - Audit / integration event mapper (publish as integration events)
//   - Local handlers (cache invalidation, projection updates)
//
// Following the established pattern: positional record + auto-populated
// EventId + OccurredOn (see MapCreatedDomainEvent for prior art).

public record WarehouseCreatedDomainEvent(Guid WarehouseId, string Code, string Name) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record WarehouseDeactivatedDomainEvent(Guid WarehouseId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record WarehouseReactivatedDomainEvent(Guid WarehouseId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record WarehouseServiceModeEnabledDomainEvent(Guid WarehouseId, TransportMode Mode) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record WarehouseServiceModeDisabledDomainEvent(Guid WarehouseId, TransportMode Mode) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public record WarehouseGeofenceUpdatedDomainEvent(Guid WarehouseId, int? RadiusM) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
