using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

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

    public async Task<Result<string>> SendAsync(
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

        return await _riot3.SendOrderAsync(request, cancellationToken);
    }

    private static Riot3Mission MapMission(ResolvedMission m)
    {
        return new Riot3Mission
        {
            MissionIndex = m.Sequence,
            Type = m.Type,
            Category = m.Category,
            MapId = m.MapId,
            StationId = m.StationId,
            ActionType = m.ActionType,
            BlockingType = m.BlockingType ?? "NONE",
            ActionParameters = m.ActionParameters?
                .Select(p => new Riot3ActionParam { Key = p.Key, Value = p.Value?.ToString() ?? string.Empty })
                .ToList()
        };
    }
}
