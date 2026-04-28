using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.VehicleGroup;

public class CreateVehicleGroupCommandHandler : ICommandHandler<CreateVehicleGroupCommand, Guid>
{
    private readonly IVehicleGroupRepository _repo;
    public CreateVehicleGroupCommandHandler(IVehicleGroupRepository repo) => _repo = repo;

    public async Task<Result<Guid>> Handle(CreateVehicleGroupCommand request, CancellationToken cancellationToken)
    {
        var group = new Domain.Entities.VehicleGroup(request.Name, request.Description, request.Tags);
        await _repo.AddAsync(group, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(group.Id);
    }
}

public class AddVehicleToGroupCommandHandler : ICommandHandler<AddVehicleToGroupCommand>
{
    private readonly IVehicleGroupRepository _groupRepo;
    private readonly IVehicleRepository _vehicleRepo;

    public AddVehicleToGroupCommandHandler(IVehicleGroupRepository groupRepo, IVehicleRepository vehicleRepo)
    {
        _groupRepo = groupRepo;
        _vehicleRepo = vehicleRepo;
    }

    public async Task<Result> Handle(AddVehicleToGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _groupRepo.GetByIdAsync(request.GroupId, cancellationToken);
        if (group == null) return Result.Failure($"Group {request.GroupId} not found.");

        // Validate vehicle exists — FK on VehicleGroupMembers will also enforce this,
        // but a pre-check gives a clearer error message to the caller.
        var vehicleExists = await _vehicleRepo.GetByIdAsync(request.VehicleId, cancellationToken) != null;
        if (!vehicleExists) return Result.Failure($"Vehicle {request.VehicleId} not found.");

        group.AddVehicle(request.VehicleId);

        await _groupRepo.UpdateAsync(group, cancellationToken);
        await _groupRepo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public class RemoveVehicleFromGroupCommandHandler : ICommandHandler<RemoveVehicleFromGroupCommand>
{
    private readonly IVehicleGroupRepository _groupRepo;

    public RemoveVehicleFromGroupCommandHandler(IVehicleGroupRepository groupRepo)
    {
        _groupRepo = groupRepo;
    }

    public async Task<Result> Handle(RemoveVehicleFromGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _groupRepo.GetByIdAsync(request.GroupId, cancellationToken);
        if (group == null) return Result.Failure($"Group {request.GroupId} not found.");

        group.RemoveVehicle(request.VehicleId);

        await _groupRepo.UpdateAsync(group, cancellationToken);
        await _groupRepo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
