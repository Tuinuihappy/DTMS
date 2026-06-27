using AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplates;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateOrderTemplate;

internal sealed class CreateOrderTemplateCommandHandler : ICommandHandler<CreateOrderTemplateCommand, OrderTemplateDto>
{
    private readonly IOrderTemplateRepository _repository;
    private readonly IActionTemplateRepository _actionRepository;
    private readonly ICurrentUserAccessor _currentUser;

    public CreateOrderTemplateCommandHandler(
        IOrderTemplateRepository repository,
        IActionTemplateRepository actionRepository,
        ICurrentUserAccessor currentUser)
    {
        _repository = repository;
        _actionRepository = actionRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<OrderTemplateDto>> Handle(CreateOrderTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<OrderTemplateDto>.Failure("Name is required.");

        if (await _repository.NameExistsAsync(request.Name, excludeId: null, cancellationToken))
            return Result<OrderTemplateDto>.Failure($"OrderTemplate with name '{request.Name}' already exists.");

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
                return Result<OrderTemplateDto>.Failure(
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
                description: request.Description,
                pickupStationId: request.PickupStationId,
                dropStationId: request.DropStationId,
                createdBy: _currentUser.GetCurrentUserName());
        }
        catch (ArgumentException ex)
        {
            return Result<OrderTemplateDto>.Failure(ex.Message);
        }

        await _repository.AddAsync(template, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<OrderTemplateDto>.Success(OrderTemplateDtoFactory.From(template));
    }
}
