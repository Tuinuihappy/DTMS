using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplates;

// Wire shape mirrors RIOT3 POST /order-templates body:
//   { name, priority, transportOrder { structureType, priority, missions[] },
//     appointVehicle*, appointQueueWaitArea }
// plus DTMS metadata at the top level (id, isActive, audit timestamps).

public sealed record MissionParameterDto(string Key, object? Value);

public sealed record OrderTemplateMissionDto(
    int Sequence,
    string Type,
    string Category,
    int? MapId,
    int? StationId,
    string? ActionType,
    string? BlockingType,
    IReadOnlyList<MissionParameterDto>? ActionParameters,
    string? ActionTemplateName);

public sealed record TransportOrderDto(
    string StructureType,
    int Priority,
    IReadOnlyList<OrderTemplateMissionDto> Missions);

public sealed record OrderTemplateDto(
    Guid Id,
    string Name,
    int Priority,
    TransportOrderDto TransportOrder,
    string? AppointVehicleKey,
    string? AppointVehicleName,
    string? AppointVehicleGroupKey,
    string? AppointVehicleGroupName,
    string? AppointQueueWaitArea,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    Guid? PickupStationId,
    Guid? DropStationId);

public record GetOrderTemplatesQuery(bool IncludeInactive = false) : IQuery<List<OrderTemplateDto>>;

public class GetOrderTemplatesQueryHandler : IQueryHandler<GetOrderTemplatesQuery, List<OrderTemplateDto>>
{
    private readonly IOrderTemplateRepository _repo;

    public GetOrderTemplatesQueryHandler(IOrderTemplateRepository repo) => _repo = repo;

    public async Task<Result<List<OrderTemplateDto>>> Handle(
        GetOrderTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var templates = await _repo.ListAsync(request.IncludeInactive, cancellationToken);
        var dtos = templates.Select(OrderTemplateDtoFactory.From).ToList();
        return Result<List<OrderTemplateDto>>.Success(dtos);
    }
}

public static class OrderTemplateDtoFactory
{
    public static OrderTemplateDto From(OrderTemplate t)
    {
        var missions = t.Missions
            .OrderBy(m => m.Sequence)
            .Select(m => new OrderTemplateMissionDto(
                Sequence: m.Sequence,
                Type: m.Type == MissionType.Move ? "MOVE" : "ACT",
                Category: m.Category,
                MapId: m.MapId,
                StationId: m.StationId,
                ActionType: m.ActionType,
                BlockingType: m.BlockingType,
                ActionParameters: m.ActionParameters?
                    .Select(p => new MissionParameterDto(p.Key, ParseValue(p.Value)))
                    .ToList(),
                ActionTemplateName: m.ActionTemplateName))
            .ToList();

        var transportOrder = new TransportOrderDto(
            StructureType: t.StructureType,
            Priority: t.TransportOrderPriority,
            Missions: missions);

        return new OrderTemplateDto(
            Id: t.Id,
            Name: t.Name,
            Priority: t.Priority,
            TransportOrder: transportOrder,
            AppointVehicleKey: t.AppointVehicleKey,
            AppointVehicleName: t.AppointVehicleName,
            AppointVehicleGroupKey: t.AppointVehicleGroupKey,
            AppointVehicleGroupName: t.AppointVehicleGroupName,
            AppointQueueWaitArea: t.AppointQueueWaitArea,
            Description: t.Description,
            IsActive: t.IsActive,
            CreatedAt: t.CreatedAt,
            ModifiedAt: t.ModifiedAt,
            PickupStationId: t.PickupStationId,
            DropStationId: t.DropStationId);
    }

    // Mission action params are stored as strings but RIOT3 expects integers
    // for id/param0/param1. Emit as int when the stored value parses cleanly;
    // fall back to the raw string otherwise.
    private static object? ParseValue(string? value)
    {
        if (value is null) return null;
        if (int.TryParse(value, out var i)) return i;
        return value;
    }
}
