using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.UpdateStation;

// Patch-style command — only fields the caller sets are touched.
// ActionType uses a tri-state through the dedicated UpdateAction* flag
// so callers can explicitly clear the action config (set null) vs leaving
// it untouched (don't include in payload at all).
public record UpdateStationCommand(
    Guid StationId,
    StationType? Type,
    string? Code,
    bool UpdateAction = false,
    string? ActionType = null,
    string? ActionCategory = null,
    IDictionary<string, string>? ActionParameters = null) : ICommand;
