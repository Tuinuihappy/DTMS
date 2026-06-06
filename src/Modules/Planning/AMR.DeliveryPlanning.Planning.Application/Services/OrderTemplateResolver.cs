using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

public sealed class OrderTemplateResolver : IOrderTemplateResolver
{
    private readonly IActionTemplateRepository _actionRepo;

    public OrderTemplateResolver(IActionTemplateRepository actionRepo)
    {
        _actionRepo = actionRepo;
    }

    public async Task<ResolvedOrder> ResolveAsync(OrderTemplate template, CancellationToken cancellationToken = default)
    {
        var resolvedMissions = new List<ResolvedMission>(template.Missions.Count);

        // Cache ActionTemplate lookups so a template that references the same
        // action multiple times only hits the repository once.
        var cache = new Dictionary<string, ActionTemplate>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in template.Missions.OrderBy(x => x.Sequence))
        {
            ResolvedMission resolved = m.Type switch
            {
                MissionType.Move => ResolveMove(m),
                MissionType.Act when !string.IsNullOrWhiteSpace(m.ActionTemplateName)
                    => await ResolveActByReferenceAsync(m, cache, cancellationToken),
                MissionType.Act => ResolveActInline(m),
                _ => throw new InvalidOperationException($"Unknown mission type at sequence {m.Sequence}.")
            };
            resolvedMissions.Add(resolved);
        }

        return new ResolvedOrder(
            Name: template.Name,
            Priority: template.Priority,
            StructureType: template.StructureType,
            TransportOrderPriority: template.TransportOrderPriority,
            Missions: resolvedMissions,
            AppointVehicleKey: template.AppointVehicleKey,
            AppointVehicleName: template.AppointVehicleName,
            AppointVehicleGroupKey: template.AppointVehicleGroupKey,
            AppointVehicleGroupName: template.AppointVehicleGroupName,
            AppointQueueWaitArea: template.AppointQueueWaitArea);
    }

    private static ResolvedMission ResolveMove(OrderTemplateMission m)
        => new(m.Sequence, "MOVE", m.Category, m.MapId, m.StationId,
               ActionType: null, BlockingType: null, ActionParameters: null);

    private static ResolvedMission ResolveActInline(OrderTemplateMission m)
        => new(
            Sequence: m.Sequence,
            Type: "ACT",
            Category: m.Category,
            MapId: null,
            StationId: null,
            ActionType: m.ActionType,
            BlockingType: m.BlockingType ?? "NONE",
            ActionParameters: (m.ActionParameters ?? Array.Empty<MissionActionParameter>())
                .Select(p => new ResolvedParam(p.Key, ParseValue(p.Value)))
                .ToList());

    private async Task<ResolvedMission> ResolveActByReferenceAsync(
        OrderTemplateMission m,
        Dictionary<string, ActionTemplate> cache,
        CancellationToken cancellationToken)
    {
        var name = m.ActionTemplateName!;
        if (!cache.TryGetValue(name, out var action))
        {
            action = await _actionRepo.GetByNameAsync(name, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Mission {m.Sequence}: ActionTemplate '{name}' not found in catalog.");
            cache[name] = action;
        }

        // Emit the four well-known RIOT3 parameter slots in canonical order.
        // ParamStr is included only when set so the outgoing payload stays
        // small for the common (id/param0/param1-only) case.
        var parameters = new List<ResolvedParam>
        {
            new("id",     action.VendorActionId),
            new("param0", action.Param0),
            new("param1", action.Param1)
        };
        if (!string.IsNullOrWhiteSpace(action.ParamStr))
            parameters.Add(new ResolvedParam("param_str", action.ParamStr));

        return new ResolvedMission(
            Sequence: m.Sequence,
            Type: "ACT",
            Category: m.Category,
            MapId: null,
            StationId: null,
            // ResolvedMission.ActionType is a free-text string passed to the
            // vendor; emit the uppercase token ("STD"/"ACT") to match the
            // RIOT3 wire vocabulary.
            ActionType: action.ActionType.ToString().ToUpperInvariant(),
            BlockingType: m.BlockingType ?? "NONE",
            ActionParameters: parameters);
    }

    // Inline action params land in storage as strings (jsonb deserialization).
    // Emit numeric-looking values as int so RIOT3 sees actual JSON numbers,
    // matching the spec example exactly.
    private static object? ParseValue(string? value)
    {
        if (value is null) return null;
        if (int.TryParse(value, out var i)) return i;
        return value;
    }
}
