using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplates;

// Wire shape mirrors RIOT3 POST /order-templates body:
//   { name, priority, transportOrder { structureType, priority, missions[] },
//     appointVehicle*, appointQueueWaitArea }
// plus DTMS metadata at the top level (id, isActive, audit timestamps + user).

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
    string? CreatedBy,
    string? ModifiedBy,
    Guid? PickupStationId,
    Guid? DropStationId);

// RIOT3-style envelope for the paged list. Field names match the vendor
// (`current`, `pages`, `size`, `total`, `records`) so a client that knows
// RIOT3 can read DTMS without remapping.
public sealed record PagedOrderTemplates(
    long Current,
    long Pages,
    long Size,
    long Total,
    IReadOnlyList<OrderTemplateDto> Records);

public record GetOrderTemplatesQuery(
    int Page = 1,
    int Size = 20,
    bool IncludeInactive = false) : IQuery<PagedOrderTemplates>;

public class GetOrderTemplatesQueryHandler : IQueryHandler<GetOrderTemplatesQuery, PagedOrderTemplates>
{
    private const int MaxPageSize = 200;

    private readonly IOrderTemplateRepository _repo;

    public GetOrderTemplatesQueryHandler(IOrderTemplateRepository repo) => _repo = repo;

    public async Task<Result<PagedOrderTemplates>> Handle(
        GetOrderTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        // Clamp so a buggy client (size=0, size=-1, size=99999) can't OOM the
        // server or send back an empty page that loops forever.
        var page = request.Page < 1 ? 1 : request.Page;
        var size = request.Size < 1 ? 20 : Math.Min(request.Size, MaxPageSize);

        var (templates, total) = await _repo.ListPagedAsync(
            page, size, request.IncludeInactive, cancellationToken);

        var records = templates.Select(OrderTemplateDtoFactory.From).ToList();
        // Mybatis-Plus convention: ceil(total/size), minimum 1 even when empty.
        var pages = total == 0 ? 1 : (total + size - 1) / size;
        return Result<PagedOrderTemplates>.Success(
            new PagedOrderTemplates(page, pages, size, total, records));
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
            CreatedBy: t.CreatedBy,
            ModifiedBy: t.ModifiedBy,
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
