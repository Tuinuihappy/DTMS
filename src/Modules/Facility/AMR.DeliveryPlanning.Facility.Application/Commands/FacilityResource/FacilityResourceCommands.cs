using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.FacilityResource;

public record RegisterFacilityResourceCommand(
    Guid MapId,
    string ResourceKey,
    FacilityResourceType ResourceType,
    string? VendorRef,
    string? Description) : ICommand<Guid>;

public class RegisterFacilityResourceCommandHandler : ICommandHandler<RegisterFacilityResourceCommand, Guid>
{
    private readonly IFacilityResourceRepository _repo;
    public RegisterFacilityResourceCommandHandler(IFacilityResourceRepository repo) => _repo = repo;

    public async Task<Result<Guid>> Handle(RegisterFacilityResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = new Domain.Entities.FacilityResource(
            request.MapId, request.ResourceKey, request.ResourceType, request.VendorRef, request.Description);
        await _repo.AddAsync(resource, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(resource.Id);
    }
}

public record CommandFacilityResourceCommand(Guid ResourceId, string Command) : ICommand;

public class CommandFacilityResourceCommandHandler : ICommandHandler<CommandFacilityResourceCommand>
{
    private readonly IFacilityResourceRepository _repo;
    private readonly IFacilityResourceCommandService _commandService;

    public CommandFacilityResourceCommandHandler(IFacilityResourceRepository repo, IFacilityResourceCommandService commandService)
    {
        _repo = repo;
        _commandService = commandService;
    }

    public async Task<Result> Handle(CommandFacilityResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = await _repo.GetByIdAsync(request.ResourceId, cancellationToken);
        if (resource == null) return Result.Failure($"Resource {request.ResourceId} not found.");
        if (resource.VendorRef == null) return Result.Failure("Resource has no vendor reference configured.");

        var success = await _commandService.SendCommandAsync(
            resource.ResourceType.ToString(), resource.VendorRef, request.Command, cancellationToken);

        return success ? Result.Success() : Result.Failure("Vendor command failed.");
    }
}
