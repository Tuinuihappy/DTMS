using DTMS.Facility.Application.Commands.AddStation;
using DTMS.Facility.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.UpdateStation;

// Patch-style command — only fields the caller sets are touched.
// UpdateActions is a tri-state flag so callers can leave the existing
// action map untouched (UpdateActions=false, the default), replace it
// (UpdateActions=true + non-empty Actions), or clear it entirely
// (UpdateActions=true + null/empty Actions).
public record UpdateStationCommand(
    Guid StationId,
    StationType? Type,
    string? Code,
    bool UpdateActions = false,
    IDictionary<string, StationActionInput>? Actions = null) : ICommand;
