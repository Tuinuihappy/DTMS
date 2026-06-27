using DTMS.Planning.Application.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Composition-root bridge: maps Planning.Application's vendor-agnostic
// ResolvedOrder into the RIOT3-specific Riot3OrderRequest, then forwards
// to Riot3CommandService. Lives in the API project so neither
// Planning.Application nor VendorAdapter.Riot3 has to know about the other.
internal sealed class Riot3OrderDispatcherAdapter : IRobotOrderDispatcher
{
    private readonly Riot3CommandService _riot3;

    public Riot3OrderDispatcherAdapter(Riot3CommandService riot3)
    {
        _riot3 = riot3;
    }

    public async Task<Result<RobotOrderDispatchResult>> SendAsync(
        string upperKey,
        ResolvedOrder order,
        CancellationToken cancellationToken = default)
    {
        var request = new Riot3OrderRequest
        {
            UpperKey = upperKey,
            OrderName = order.Name,
            Priority = order.Priority,
            StructureType = order.StructureType,
            AppointVehicleKey = order.AppointVehicleKey,
            AppointVehicleName = order.AppointVehicleName,
            AppointVehicleGroupKey = order.AppointVehicleGroupKey,
            AppointVehicleGroupName = order.AppointVehicleGroupName,
            Missions = order.Missions.Select(MapMission).ToList()
        };

        var result = await _riot3.SendOrderAsync(request, cancellationToken);
        return result.IsSuccess
            ? Result<RobotOrderDispatchResult>.Success(
                new RobotOrderDispatchResult(result.Value.OrderKey, result.Value.RequestJson))
            : Result<RobotOrderDispatchResult>.Failure(result.Error!);
    }

    private static Riot3Mission MapMission(ResolvedMission m)
    {
        // Field selection per RIOT3 POST /api/v4/orders spec example:
        //   MOVE: { type, category, mapId, stationId }
        //   ACT : { type, category, actionType, blockingType, actionParameters }
        // Anything we don't populate stays null and is dropped by the
        // serializer (configured with WhenWritingNull). Notably we DO NOT
        // emit actionName — RIOT3 looks up names against its own catalog
        // and an incidental match would override the inline params we just
        // resolved. Likewise missionIndex is not in the spec example.
        var isMove = string.Equals(m.Type, "MOVE", StringComparison.OrdinalIgnoreCase);
        return new Riot3Mission
        {
            Type = m.Type,
            Category = m.Category,
            MapId = isMove ? m.MapId : null,
            StationId = isMove ? m.StationId : null,
            ActionType = isMove ? null : m.ActionType,
            BlockingType = isMove ? null : (m.BlockingType ?? "NONE"),
            ActionParameters = isMove
                ? null
                : m.ActionParameters?
                    .Select(p => new Riot3ActionParam { Key = p.Key, Value = p.Value })
                    .ToList()
        };
    }
}
