using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.SetOrderTemplateActive;

internal sealed class SetOrderTemplateActiveCommandHandler : ICommandHandler<SetOrderTemplateActiveCommand>
{
    private readonly IOrderTemplateRepository _repository;

    public SetOrderTemplateActiveCommandHandler(IOrderTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(SetOrderTemplateActiveCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"OrderTemplate {request.Id} not found.");

        if (request.IsActive) template.Activate();
        else template.Deactivate();

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
