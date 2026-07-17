using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.CreateJobAnchor;

/// <summary>
/// Phase b8 — Create a lightweight Job (1 Leg pickup→drop, cost=0) as an
/// anchor for envelope-dispatched orders. Deliberately does NO route
/// costing, NO solving, NO vehicle assignment — routing and robot choice
/// are RIOT3's job (the legacy manual-planning stack that did those was
/// deleted 2026-07-17). Used by DeliveryOrderValidatedConsumer between
/// MarkPlanning and MarkPlanned.
/// </summary>
public record CreateJobAnchorCommand(
    Guid DeliveryOrderId,
    int GroupIndex,
    Guid PickupStationId,
    Guid DropStationId,
    string Priority,
    string? RequestedTransportMode,
    DateTime? SlaDeadline
) : ICommand<Guid>;
