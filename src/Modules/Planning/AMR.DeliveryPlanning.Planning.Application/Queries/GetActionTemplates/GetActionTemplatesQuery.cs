using System.Text.Json.Serialization;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;

// Response shape mirrors the RIOT3 payload — actionName + actionType +
// actionParameters[] — plus DTMS metadata (Id, IsActive, audit timestamps)
// so the UI can edit/delete by id.
//
// `Value` is omitted from the JSON when null so the `param_str` entry
// renders as `{ "key": "param_str" }` — matches the RIOT3 wire shape
// (param_str slot is always present so consumers know the key exists,
// but the value is dropped when unset).
public sealed record ActionParameterValueDto(
    string Key,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Value);

public sealed record ActionTemplateDto(
    Guid Id,
    string ActionName,
    ActionCategory ActionCategory,
    string ActionType,
    IReadOnlyList<ActionParameterValueDto> ActionParameters,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    string? CreatedBy,
    string? ModifiedBy);

// RIOT3-style envelope for the paged list. Field names match the vendor
// (`current`, `pages`, `size`, `total`, `records`) so a client that knows
// RIOT3 can read DTMS without remapping.
public sealed record PagedActionTemplates(
    long Current,
    long Pages,
    long Size,
    long Total,
    IReadOnlyList<ActionTemplateDto> Records);

public record GetActionTemplatesQuery(
    int Page = 1,
    int Size = 20,
    bool IncludeInactive = false,
    ActionCategory? ActionCategory = null,
    string? Search = null,
    string? SortBy = null,
    bool SortDescending = false) : IQuery<PagedActionTemplates>;

public class GetActionTemplatesQueryHandler : IQueryHandler<GetActionTemplatesQuery, PagedActionTemplates>
{
    private const int MaxPageSize = 200;

    private readonly IActionTemplateRepository _repo;

    public GetActionTemplatesQueryHandler(IActionTemplateRepository repo) => _repo = repo;

    public async Task<Result<PagedActionTemplates>> Handle(
        GetActionTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        // Clamp so a buggy client (size=0, size=-1, size=99999) can't OOM the
        // server or send back an empty page that loops forever.
        var page = request.Page < 1 ? 1 : request.Page;
        var size = request.Size < 1 ? 20 : Math.Min(request.Size, MaxPageSize);

        var (templates, total) = await _repo.ListPagedAsync(
            page,
            size,
            request.IncludeInactive,
            request.ActionCategory,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            string.IsNullOrWhiteSpace(request.SortBy) ? null : request.SortBy.Trim(),
            request.SortDescending,
            cancellationToken);

        var records = templates.Select(ActionTemplateDtoFactory.From).ToList();
        // Mybatis-Plus convention: ceil(total/size), minimum 1 even when empty.
        var pages = total == 0 ? 1 : (total + size - 1) / size;
        return Result<PagedActionTemplates>.Success(
            new PagedActionTemplates(page, pages, size, total, records));
    }
}

// System-wide catalog counters shown in the KPI strip. Always unfiltered —
// mirrors the DeliveryOrder stats pattern where the strip is a fixed
// overview, not a narrowed view of the current filter selection.
public sealed record ActionTemplateStatsDto(
    int Total,
    int Active,
    int Inactive,
    int Std,
    int Act);

public record GetActionTemplateStatsQuery() : IQuery<ActionTemplateStatsDto>;

public class GetActionTemplateStatsQueryHandler
    : IQueryHandler<GetActionTemplateStatsQuery, ActionTemplateStatsDto>
{
    private readonly IActionTemplateRepository _repo;

    public GetActionTemplateStatsQueryHandler(IActionTemplateRepository repo) => _repo = repo;

    public async Task<Result<ActionTemplateStatsDto>> Handle(
        GetActionTemplateStatsQuery request,
        CancellationToken cancellationToken)
    {
        var stats = await _repo.GetStatsAsync(cancellationToken);
        return Result<ActionTemplateStatsDto>.Success(new ActionTemplateStatsDto(
            stats.Total,
            stats.Active,
            stats.Total - stats.Active,
            stats.Std,
            stats.Act));
    }
}

// Shared projection — used by both the list query and the single-by-id query
// so the RIOT3 envelope shape stays consistent.
public static class ActionTemplateDtoFactory
{
    public static ActionTemplateDto From(Domain.Entities.ActionTemplate t)
    {
        var parameters = new List<ActionParameterValueDto>
        {
            new("id",     t.VendorActionId),
            new("param0", t.Param0),
            new("param1", t.Param1),
            // RIOT3 keeps param_str in the array even when blank so consumers
            // know the key exists; emit it with null value when unset.
            new("param_str", t.ParamStr)
        };
        return new ActionTemplateDto(
            t.Id, t.Name, t.ActionCategory, t.ActionType, parameters,
            t.IsActive, t.CreatedAt, t.ModifiedAt, t.CreatedBy, t.ModifiedBy);
    }
}
