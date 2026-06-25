using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Queries.Admin;

// Phase 4.6 — Dispatcher console operator board feed. Returns every
// operator (active / on-leave / deactivated) so ops can see the whole
// roster on one screen. Active assignments are surfaced as part of
// the row (via CurrentTripId) — the board joins on the trip-extension
// list separately for the timestamps.
public record ListOperatorsQuery() : IQuery<IReadOnlyList<OperatorBoardDto>>;

public record OperatorBoardDto(
    Guid Id,
    string EmployeeCode,
    string DisplayName,
    OperatorRole Role,
    OperatorStatus Status,
    Guid? PrimaryWarehouseId,
    Guid? CurrentTripId,
    DateTime LastSyncedAt);

internal sealed class ListOperatorsQueryHandler : IQueryHandler<ListOperatorsQuery, IReadOnlyList<OperatorBoardDto>>
{
    private readonly IOperatorRepository _operators;
    public ListOperatorsQueryHandler(IOperatorRepository operators) => _operators = operators;

    public async Task<Result<IReadOnlyList<OperatorBoardDto>>> Handle(ListOperatorsQuery request, CancellationToken cancellationToken)
    {
        var ops = await _operators.ListAllAsync(cancellationToken);
        var dtos = ops.Select(o => new OperatorBoardDto(
            o.Id, o.EmployeeCode, o.DisplayName, o.Role, o.Status,
            o.PrimaryWarehouseId, o.CurrentTripId, o.LastSyncedAt)).ToList();
        return Result<IReadOnlyList<OperatorBoardDto>>.Success(dtos);
    }
}
