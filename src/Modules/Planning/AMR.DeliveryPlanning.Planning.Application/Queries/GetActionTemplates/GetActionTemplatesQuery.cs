using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;

// Response shape mirrors the RIOT3 payload — actionName + actionType +
// actionParameters[] — plus DTMS metadata (Id, IsActive, audit timestamps)
// so the UI can edit/delete by id.
public sealed record ActionParameterValueDto(string Key, object? Value);

public sealed record ActionTemplateDto(
    Guid Id,
    string ActionName,
    string ActionType,
    IReadOnlyList<ActionParameterValueDto> ActionParameters,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ModifiedAt);

public record GetActionTemplatesQuery(
    bool IncludeInactive = false,
    string? ActionType = null) : IQuery<List<ActionTemplateDto>>;

public class GetActionTemplatesQueryHandler : IQueryHandler<GetActionTemplatesQuery, List<ActionTemplateDto>>
{
    private readonly IActionTemplateRepository _repo;

    public GetActionTemplatesQueryHandler(IActionTemplateRepository repo) => _repo = repo;

    public async Task<Result<List<ActionTemplateDto>>> Handle(
        GetActionTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var templates = await _repo.ListAsync(request.IncludeInactive, request.ActionType, cancellationToken);
        var dtos = templates.Select(ActionTemplateDtoFactory.From).ToList();
        return Result<List<ActionTemplateDto>>.Success(dtos);
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
            t.Id, t.Name, t.ActionType, parameters,
            t.Description, t.IsActive, t.CreatedAt, t.ModifiedAt);
    }
}
