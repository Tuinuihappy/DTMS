using DTMS.Fleet.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Events;

/// <summary>
/// Every carrier lifecycle transition (ADR-019). Deliberately NOT mapped in
/// <c>FleetDomainEventMapper</c> yet — nothing consumes it until P3, and the
/// mapper returns an empty collection for unrecognised events, so no outbox row
/// is written. Same posture as <c>VehicleRegisteredDomainEvent</c>, which has
/// been raised-but-unmapped since the module was built.
/// </summary>
public record CarrierStatusChangedDomainEvent(
    Guid CarrierId,
    string CarrierCode,
    CarrierStatus OldStatus,
    CarrierStatus NewStatus,
    string? Reason) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
