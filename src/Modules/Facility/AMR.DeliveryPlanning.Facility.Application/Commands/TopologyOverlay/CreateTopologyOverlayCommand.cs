using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.TopologyOverlay;

public record CreateTopologyOverlayCommand(
    Guid MapId,
    OverlayType Type,
    string Reason,
    DateTime ValidFrom,
    DateTime ValidUntil,
    string? PolygonJson,
    Guid? AffectedStationId) : ICommand<Guid>;

public class CreateTopologyOverlayCommandHandler : ICommandHandler<CreateTopologyOverlayCommand, Guid>
{
    private readonly ITopologyOverlayRepository _repo;
    private readonly IEventBus _eventBus;

    public CreateTopologyOverlayCommandHandler(ITopologyOverlayRepository repo, IEventBus eventBus)
    {
        _repo = repo;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(CreateTopologyOverlayCommand request, CancellationToken cancellationToken)
    {
        var overlay = new Domain.Entities.TopologyOverlay(
            request.MapId, request.Type, request.Reason,
            request.ValidFrom, request.ValidUntil, request.PolygonJson, request.AffectedStationId);

        await _repo.AddAsync(overlay, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(overlay.Id);
    }
}

public record ExpireTopologyOverlayCommand(Guid OverlayId) : ICommand;

public class ExpireTopologyOverlayCommandHandler : ICommandHandler<ExpireTopologyOverlayCommand>
{
    private readonly ITopologyOverlayRepository _repo;

    public ExpireTopologyOverlayCommandHandler(ITopologyOverlayRepository repo) => _repo = repo;

    public async Task<Result> Handle(ExpireTopologyOverlayCommand request, CancellationToken cancellationToken)
    {
        var overlay = await _repo.GetByIdAsync(request.OverlayId, cancellationToken);
        if (overlay == null) return Result.Failure($"Overlay {request.OverlayId} not found.");

        overlay.ExtendUntil(DateTime.UtcNow.AddSeconds(-1));
        await _repo.UpdateAsync(overlay, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
