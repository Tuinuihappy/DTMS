using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.RegisterVehicle;

internal sealed class RegisterVehicleCommandHandler : ICommandHandler<RegisterVehicleCommand, Guid>
{
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IVehicleTypeRepository _vehicleTypeRepository;
    private readonly ITenantContext _tenantContext;

    public RegisterVehicleCommandHandler(IVehicleRepository vehicleRepository, IVehicleTypeRepository vehicleTypeRepository, ITenantContext tenantContext)
    {
        _vehicleRepository = vehicleRepository;
        _vehicleTypeRepository = vehicleTypeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(RegisterVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicleType = await _vehicleTypeRepository.GetByIdAsync(request.VehicleTypeId, cancellationToken);
        if (vehicleType is null)
        {
            throw new NotFoundException($"VehicleType with ID {request.VehicleTypeId} not found.");
        }

        var vehicle = new Vehicle(Guid.NewGuid(), _tenantContext.TenantId, request.VehicleName, request.VehicleTypeId, request.AdapterKey);

        await _vehicleRepository.AddAsync(vehicle, cancellationToken);
        await _vehicleRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(vehicle.Id);
    }
}
