using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.SetActionTemplateActive;

internal sealed class SetActionTemplateActiveCommandHandler : ICommandHandler<SetActionTemplateActiveCommand>
{
    private readonly IActionTemplateRepository _repository;

    public SetActionTemplateActiveCommandHandler(IActionTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(SetActionTemplateActiveCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"ActionTemplate {request.Id} not found.");

        if (request.IsActive) template.Activate();
        else template.Deactivate();

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
