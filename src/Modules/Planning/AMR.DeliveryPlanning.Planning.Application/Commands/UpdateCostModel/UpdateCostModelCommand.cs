using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateCostModel;

public record UpdateCostModelCommand(
    double TravelDistanceWeight,
    double BatteryBurnWeight,
    double SlaPenaltyWeight,
    double LowBatteryThresholdPct,
    double CriticalBatteryThresholdPct,
    string? VehicleTypeKey = null) : ICommand;

public class UpdateCostModelCommandHandler : ICommandHandler<UpdateCostModelCommand>
{
    private readonly ICostModelService _costModel;

    public UpdateCostModelCommandHandler(ICostModelService costModel) => _costModel = costModel;

    public Task<Result> Handle(UpdateCostModelCommand request, CancellationToken cancellationToken)
    {
        var config = new CostModelConfig(
            request.TravelDistanceWeight,
            request.BatteryBurnWeight,
            request.SlaPenaltyWeight,
            request.LowBatteryThresholdPct,
            request.CriticalBatteryThresholdPct);

        _costModel.UpdateConfig(config, request.VehicleTypeKey);
        return Task.FromResult(Result.Success());
    }
}
