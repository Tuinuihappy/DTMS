using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.DeleteOrderTemplate;

internal sealed class DeleteOrderTemplateCommandHandler : ICommandHandler<DeleteOrderTemplateCommand>
{
    private readonly IOrderTemplateRepository _repository;

    public DeleteOrderTemplateCommandHandler(IOrderTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(DeleteOrderTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"OrderTemplate {request.Id} not found.");

        _repository.Remove(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
