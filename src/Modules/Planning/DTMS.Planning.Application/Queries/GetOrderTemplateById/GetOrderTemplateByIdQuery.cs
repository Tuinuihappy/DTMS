using DTMS.Planning.Application.Queries.GetOrderTemplates;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetOrderTemplateById;

public record GetOrderTemplateByIdQuery(Guid Id) : IQuery<OrderTemplateDto>;

public class GetOrderTemplateByIdQueryHandler : IQueryHandler<GetOrderTemplateByIdQuery, OrderTemplateDto>
{
    private readonly IOrderTemplateRepository _repo;

    public GetOrderTemplateByIdQueryHandler(IOrderTemplateRepository repo) => _repo = repo;

    public async Task<Result<OrderTemplateDto>> Handle(
        GetOrderTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var t = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (t is null) return Result<OrderTemplateDto>.Failure($"OrderTemplate {request.Id} not found.");

        return Result<OrderTemplateDto>.Success(OrderTemplateDtoFactory.From(t));
    }
}
