using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.WarehouseLifecycle;

// Lifecycle pair — co-located because each is < 30 lines and reasoning
// about one without the other is rare. Soft-delete semantics: data
// remains intact so in-flight trips referencing the warehouse continue
// to resolve; only new order creation is blocked.

public sealed record DeactivateWarehouseCommand(Guid Id) : ICommand;

internal sealed class DeactivateWarehouseCommandHandler : ICommandHandler<DeactivateWarehouseCommand>
{
    private readonly IWarehouseRepository _repository;

    public DeactivateWarehouseCommandHandler(IWarehouseRepository repository)
        => _repository = repository;

    public async Task<Result> Handle(DeactivateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (warehouse is null) return Result.Failure($"Warehouse {request.Id} not found.");

        warehouse.Deactivate();   // domain-level idempotent
        _repository.Update(warehouse);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ReactivateWarehouseCommand(Guid Id) : ICommand;

internal sealed class ReactivateWarehouseCommandHandler : ICommandHandler<ReactivateWarehouseCommand>
{
    private readonly IWarehouseRepository _repository;

    public ReactivateWarehouseCommandHandler(IWarehouseRepository repository)
        => _repository = repository;

    public async Task<Result> Handle(ReactivateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (warehouse is null) return Result.Failure($"Warehouse {request.Id} not found.");

        warehouse.Reactivate();   // domain-level idempotent
        _repository.Update(warehouse);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
