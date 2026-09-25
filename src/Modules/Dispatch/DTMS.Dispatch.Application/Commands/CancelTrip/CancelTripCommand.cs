using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.CancelTrip;

/// <param name="ActionBy">
/// Who cancelled, in the calling system's own terms. Null for callers with no
/// human behind them (the order-cancelled cascade, bulk cancel).
/// </param>
/// <param name="ActedAt">When they cancelled it upstream, if known.</param>
/// <param name="Origin">
/// Which system asked — recorded in the audit trail, kept out of
/// <paramref name="Reason"/> because Reason travels upstream to the source
/// system on shipment.cancelled.v1. Null for the operator console.
/// </param>
public record CancelTripCommand(
    Guid TripId,
    string Reason,
    string? ActionBy = null,
    DateTime? ActedAt = null,
    string? Origin = null) : ICommand;
