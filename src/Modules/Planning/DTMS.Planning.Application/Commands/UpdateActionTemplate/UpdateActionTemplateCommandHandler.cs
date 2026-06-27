using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.UpdateActionTemplate;

internal sealed class UpdateActionTemplateCommandHandler : ICommandHandler<UpdateActionTemplateCommand>
{
    private readonly IActionTemplateRepository _repository;
    private readonly ICurrentUserAccessor _currentUser;

    public UpdateActionTemplateCommandHandler(
        IActionTemplateRepository repository,
        ICurrentUserAccessor currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateActionTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"ActionTemplate {request.Id} not found.");

        var modifiedBy = _currentUser.GetCurrentUserName();

        if (!string.Equals(template.Name, request.ActionName, StringComparison.OrdinalIgnoreCase))
        {
            if (await _repository.NameExistsAsync(request.ActionName, excludeId: request.Id, cancellationToken))
                return Result.Failure($"ActionTemplate name '{request.ActionName}' is already in use.");

            template.Rename(request.ActionName, modifiedBy);
        }

        template.Update(
            actionCategory: request.ActionCategory,
            vendorActionId: request.VendorActionId,
            param0: request.Param0,
            param1: request.Param1,
            paramStr: request.ParamStr,
            modifiedBy: modifiedBy,
            actionType: request.ActionType);

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
