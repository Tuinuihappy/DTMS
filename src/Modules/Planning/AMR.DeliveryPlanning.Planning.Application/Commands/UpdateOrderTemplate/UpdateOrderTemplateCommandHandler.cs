using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateOrderTemplate;

internal sealed class UpdateOrderTemplateCommandHandler : ICommandHandler<UpdateOrderTemplateCommand>
{
    private readonly IOrderTemplateRepository _repository;
    private readonly IActionTemplateRepository _actionRepository;

    public UpdateOrderTemplateCommandHandler(
        IOrderTemplateRepository repository,
        IActionTemplateRepository actionRepository)
    {
        _repository = repository;
        _actionRepository = actionRepository;
    }

    public async Task<Result> Handle(UpdateOrderTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"OrderTemplate {request.Id} not found.");

        // Validate ActionTemplate refs same as Create.
        foreach (var m in request.Missions.Where(m => !string.IsNullOrWhiteSpace(m.ActionTemplateName)))
        {
            var found = await _actionRepository.GetByNameAsync(m.ActionTemplateName!, cancellationToken);
            if (found is null)
                return Result.Failure(
                    $"Mission {m.Sequence}: ActionTemplate '{m.ActionTemplateName}' not found.");
        }

        try
        {
            template.Update(
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
            return Result.Failure(ex.Message);
        }

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
