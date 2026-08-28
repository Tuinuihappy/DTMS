using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.RegisterCarrierType;

public record RegisterCarrierTypeCommand(
    string Code,
    string DisplayName,
    string AmrCapability,
    double? MaxWeightKg = null,
    int? MaxSlots = null,
    string? Description = null) : ICommand<Guid>;

internal sealed class RegisterCarrierTypeCommandHandler : ICommandHandler<RegisterCarrierTypeCommand, Guid>
{
    private readonly ICarrierTypeRepository _repository;

    public RegisterCarrierTypeCommandHandler(ICarrierTypeRepository repository)
        => _repository = repository;

    public async Task<Result<Guid>> Handle(RegisterCarrierTypeCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
            return Result<Guid>.Failure($"CarrierType '{request.Code.ToUpperInvariant()}' already exists.");

        var carrierType = new CarrierType(
            request.Code, request.DisplayName, request.AmrCapability,
            request.MaxWeightKg, request.MaxSlots, request.Description);

        await _repository.AddAsync(carrierType, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(carrierType.Id);
    }
}
