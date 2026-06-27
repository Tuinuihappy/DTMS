using DTMS.Planning.Domain.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetCostModel;

public record GetCostModelQuery(string? VehicleTypeKey = null) : IQuery<CostModelConfig>;

public class GetCostModelQueryHandler : IQueryHandler<GetCostModelQuery, CostModelConfig>
{
    private readonly ICostModelService _costModel;
    public GetCostModelQueryHandler(ICostModelService costModel) => _costModel = costModel;

    public Task<Result<CostModelConfig>> Handle(GetCostModelQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Result<CostModelConfig>.Success(_costModel.GetConfig(request.VehicleTypeKey)));
}
