using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;

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

        template.Update(
            actionType: request.ActionType,
            vendorActionId: request.VendorActionId,
            param0: request.Param0,
            param1: request.Param1,
            paramStr: request.ParamStr,
            modifiedBy: _currentUser.GetCurrentUserName());

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
