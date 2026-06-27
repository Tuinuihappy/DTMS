using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.SetOrderTemplateActive;

internal sealed class SetOrderTemplateActiveCommandHandler : ICommandHandler<SetOrderTemplateActiveCommand>
{
    private readonly IOrderTemplateRepository _repository;
    private readonly ICurrentUserAccessor _currentUser;

    public SetOrderTemplateActiveCommandHandler(
        IOrderTemplateRepository repository,
        ICurrentUserAccessor currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SetOrderTemplateActiveCommand request, CancellationToken cancellationToken)
    {
        var template = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"OrderTemplate {request.Id} not found.");

        var actor = _currentUser.GetCurrentUserName();
        if (request.IsActive) template.Activate(actor);
        else template.Deactivate(actor);

        _repository.Update(template);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
