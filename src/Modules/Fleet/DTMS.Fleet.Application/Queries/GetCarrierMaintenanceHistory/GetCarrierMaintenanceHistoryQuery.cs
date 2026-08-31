using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierMaintenanceHistory;

public sealed record CarrierMaintenanceEntryDto(
    Guid Id,
    string Reason,
    DateTime StartedAt,
    string StartedBy,
    DateTime? EndedAt,
    string? EndedBy,
    string? Outcome,
    bool IsOpen);

/// <summary>Every repair episode for one carrier, newest first. Not paged — a
/// cart accumulates a handful of these over its life, not hundreds.</summary>
public record GetCarrierMaintenanceHistoryQuery(string CarrierCode)
    : IQuery<List<CarrierMaintenanceEntryDto>>;

public class GetCarrierMaintenanceHistoryQueryHandler
    : IQueryHandler<GetCarrierMaintenanceHistoryQuery, List<CarrierMaintenanceEntryDto>>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierMaintenanceLogRepository _logs;

    public GetCarrierMaintenanceHistoryQueryHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs)
    {
        _carriers = carriers;
        _logs = logs;
    }

    public async Task<Result<List<CarrierMaintenanceEntryDto>>> Handle(
        GetCarrierMaintenanceHistoryQuery request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result<List<CarrierMaintenanceEntryDto>>.Failure(
                $"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        var history = await _logs.GetHistoryAsync(carrier.Id, cancellationToken);

        return Result<List<CarrierMaintenanceEntryDto>>.Success(history.Select(l =>
            new CarrierMaintenanceEntryDto(
                l.Id, l.Reason, l.StartedAt, l.StartedBy,
                l.EndedAt, l.EndedBy, l.Outcome, l.IsOpen)).ToList());
    }
}
