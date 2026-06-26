using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplateById;

public record GetActionTemplateByIdQuery(Guid Id) : IQuery<ActionTemplateDto>;

public class GetActionTemplateByIdQueryHandler : IQueryHandler<GetActionTemplateByIdQuery, ActionTemplateDto>
{
    private readonly IActionTemplateRepository _repo;

    public GetActionTemplateByIdQueryHandler(IActionTemplateRepository repo) => _repo = repo;

    public async Task<Result<ActionTemplateDto>> Handle(
        GetActionTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var t = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (t is null) return Result<ActionTemplateDto>.Failure($"ActionTemplate {request.Id} not found.");

        return Result<ActionTemplateDto>.Success(ActionTemplateDtoFactory.From(t));
    }
}
