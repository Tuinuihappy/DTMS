using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateOrderTemplate;

internal sealed class CreateOrderTemplateCommandHandler : ICommandHandler<CreateOrderTemplateCommand, Guid>
{
    private readonly IOrderTemplateRepository _repository;
    private readonly IActionTemplateRepository _actionRepository;

    public CreateOrderTemplateCommandHandler(
        IOrderTemplateRepository repository,
        IActionTemplateRepository actionRepository)
    {
        _repository = repository;
        _actionRepository = actionRepository;
    }

    public async Task<Result<Guid>> Handle(CreateOrderTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<Guid>.Failure("Name is required.");

        if (await _repository.NameExistsAsync(request.Name, excludeId: null, cancellationToken))
            return Result<Guid>.Failure($"OrderTemplate with name '{request.Name}' already exists.");

        // Validate any ActionTemplate references — fail fast with a friendly
        // 400 instead of letting the dispatcher hit a missing-action error
        // mid-trip later.
        var refMissions = request.Missions
            .Where(m => !string.IsNullOrWhiteSpace(m.ActionTemplateName))
            .ToList();
        foreach (var m in refMissions)
        {
            var found = await _actionRepository.GetByNameAsync(m.ActionTemplateName!, cancellationToken);
            if (found is null)
                return Result<Guid>.Failure(
                    $"Mission {m.Sequence}: ActionTemplate '{m.ActionTemplateName}' not found.");
        }

        OrderTemplate template;
        try
        {
            template = new OrderTemplate(
                name: request.Name,
                priority: request.Priority,
                structureType: request.StructureType,
                transportOrderPriority: request.TransportOrderPriority,
                missions: request.Missions,
                appointVehicleKey: request.AppointVehicleKey,
                appointVehicleName: request.AppointVehicleName,
                appointVehicleGroupKey: request.AppointVehicleGroupKey,
                appointVehicleGroupName: request.AppointVehicleGroupName,
                appointQueueWaitArea: request.AppointQueueWaitArea,
                description: request.Description);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        await _repository.AddAsync(template, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(template.Id);
    }
}
