using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;

internal sealed class CreateActionTemplateCommandHandler : ICommandHandler<CreateActionTemplateCommand, ActionTemplateDto>
{
    private readonly IActionTemplateRepository _repository;
    private readonly ICurrentUserAccessor _currentUser;

    public CreateActionTemplateCommandHandler(
        IActionTemplateRepository repository,
        ICurrentUserAccessor currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<ActionTemplateDto>> Handle(CreateActionTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<ActionTemplateDto>.Failure("Name is required.");

        if (await _repository.NameExistsAsync(request.Name, excludeId: null, cancellationToken))
            return Result<ActionTemplateDto>.Failure($"ActionTemplate with name '{request.Name}' already exists.");

        ActionTemplate template;
        try
        {
            template = new ActionTemplate(
                name: request.Name,
                actionCategory: request.ActionCategory,
                vendorActionId: request.VendorActionId,
                param0: request.Param0,
                param1: request.Param1,
                paramStr: request.ParamStr,
                createdBy: _currentUser.GetCurrentUserName(),
                actionType: request.ActionType);
        }
        catch (ArgumentException ex)
        {
            return Result<ActionTemplateDto>.Failure(ex.Message);
        }

        await _repository.AddAsync(template, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<ActionTemplateDto>.Success(ActionTemplateDtoFactory.From(template));
    }
}
