using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.VehicleType;

internal sealed class CreateVehicleTypeCommandHandler : ICommandHandler<CreateVehicleTypeCommand, Guid>
{
    private readonly IVehicleTypeRepository _vehicleTypeRepository;

    public CreateVehicleTypeCommandHandler(IVehicleTypeRepository vehicleTypeRepository)
    {
        _vehicleTypeRepository = vehicleTypeRepository;
    }

    public async Task<Result<Guid>> Handle(CreateVehicleTypeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TypeName))
            return Result<Guid>.Failure("TypeName is required.");

        if (request.MaxPayload <= 0)
            return Result<Guid>.Failure("MaxPayload must be greater than zero.");

        var capabilities = request.Capabilities
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();

        if (capabilities.Length == 0)
            return Result<Guid>.Failure("At least one capability is required.");

        var vehicleType = new Domain.Entities.VehicleType(
            Guid.NewGuid(),
            request.TypeName.Trim(),
            request.MaxPayload,
            capabilities);

        await _vehicleTypeRepository.AddAsync(vehicleType, cancellationToken);
        await _vehicleTypeRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(vehicleType.Id);
    }
}
