using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.DeleteActionTemplate;

internal sealed class DeleteActionTemplateCommandHandler : ICommandHandler<DeleteActionTemplateCommand>
{
    private readonly IActionTemplateRepository _repository;

    public DeleteActionTemplateCommandHandler(IActionTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(DeleteActionTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"ActionTemplate {request.Id} not found.");

        _repository.Remove(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
