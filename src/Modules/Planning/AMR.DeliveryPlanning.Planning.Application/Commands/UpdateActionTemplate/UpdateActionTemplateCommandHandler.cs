using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;

internal sealed class UpdateActionTemplateCommandHandler : ICommandHandler<UpdateActionTemplateCommand>
{
    private readonly IActionTemplateRepository _repository;

    public UpdateActionTemplateCommandHandler(IActionTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(UpdateActionTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"ActionTemplate {request.Id} not found.");

        try
        {
            template.Update(
                actionType: request.ActionType,
                vendorActionId: request.VendorActionId,
                param0: request.Param0,
                param1: request.Param1,
                paramStr: request.ParamStr,
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
