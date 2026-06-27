using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.CreateJobAnchor;

/// <summary>
/// Phase b8 — Create a lightweight Job (1 Leg pickup→drop, cost=0) as an
/// anchor for envelope-dispatched orders. Differs from
/// CreateJobFromOrderCommand: no IRouteCostCalculator call (no RIOT3
/// chatter), no IRouteSolver, no vehicle assignment. Used by
/// DeliveryOrderValidatedConsumer between MarkPlanning and MarkPlanned.
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
