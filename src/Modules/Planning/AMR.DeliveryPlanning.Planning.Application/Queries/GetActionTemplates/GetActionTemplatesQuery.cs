using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;

public sealed record ActionTemplateDto(
    Guid Id,
    string Name,
    string ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr,
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
        var dtos = templates.Select(t => new ActionTemplateDto(
            t.Id, t.Name, t.ActionType,
            t.VendorActionId, t.Param0, t.Param1, t.ParamStr,
            t.Description, t.IsActive, t.CreatedAt, t.ModifiedAt))
            .ToList();
        return Result<List<ActionTemplateDto>>.Success(dtos);
    }
}
