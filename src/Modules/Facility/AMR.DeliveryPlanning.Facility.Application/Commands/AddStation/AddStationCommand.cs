using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.AddStation;

public record AddStationCommand(
    Guid MapId,
    string Name,
    double X,
    double Y,
    double? Theta,
    StationType Type,
    string? VendorRef = null,
    string? Code = null,
    // Optional ACT mission configuration for stations where the robot
    // does more than just stop (e.g. lift/drop). When null, the station
    // is a pure MOVE waypoint. Category defaults to "agv" on the entity
    // side when actionType is set.
    string? ActionType = null,
    string? ActionCategory = null,
    IDictionary<string, string>? ActionParameters = null) : ICommand<Guid>;
