using DTMS.Facility.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.AddStation;

// Caller-facing shape for an action entry inside AddStationCommand.Actions.
// Plain DTO mirroring StationAction value object so the Application layer
// can be called without importing Domain types.
public sealed record StationActionInput(
    string ActionType,
    string? Category = null,
    IDictionary<string, string>? Parameters = null);

public record AddStationCommand(
    Guid MapId,
    string Name,
    double X,
    double Y,
    double? Theta,
    StationType Type,
    string? VendorRef = null,
    string? Code = null,
    // Optional action map keyed by intent ("lift", "drop", "charge", ...).
    // Null/empty = pure MOVE waypoint. A station can carry multiple intents
    // (e.g. a DOCK that serves both pickup and dropoff).
    IDictionary<string, StationActionInput>? Actions = null) : ICommand<Guid>;
