using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.ChargingPolicy;

public class UpsertChargingPolicyCommandHandler : ICommandHandler<UpsertChargingPolicyCommand, Guid>
{
    private readonly IChargingPolicyRepository _repo;

    public UpsertChargingPolicyCommandHandler(IChargingPolicyRepository repo) => _repo = repo;

    public async Task<Result<Guid>> Handle(UpsertChargingPolicyCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetByVehicleTypeAsync(request.VehicleTypeId, cancellationToken);

        if (existing != null)
        {
            existing.Update(request.LowThresholdPct, request.TargetThresholdPct, request.Mode);
            await _repo.UpdateAsync(existing, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(existing.Id);
        }

        var policy = new Domain.Entities.ChargingPolicy(
            request.VehicleTypeId, request.LowThresholdPct, request.TargetThresholdPct, request.Mode);
        await _repo.AddAsync(policy, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(policy.Id);
    }
}
