using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;

internal sealed class CreateActionTemplateCommandHandler : ICommandHandler<CreateActionTemplateCommand, Guid>
{
    private readonly IActionTemplateRepository _repository;

    public CreateActionTemplateCommandHandler(IActionTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(CreateActionTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<Guid>.Failure("Name is required.");

        if (await _repository.NameExistsAsync(request.Name, excludeId: null, cancellationToken))
            return Result<Guid>.Failure($"ActionTemplate with name '{request.Name}' already exists.");

        ActionTemplate template;
        try
        {
            template = new ActionTemplate(
                name: request.Name,
                actionType: request.ActionType,
                vendorActionId: request.VendorActionId,
                param0: request.Param0,
                param1: request.Param1,
                paramStr: request.ParamStr,
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
