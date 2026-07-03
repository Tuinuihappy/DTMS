using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.Admin;

// Phase 4.6 — Active Manual trips feed. Powers the right-hand column
// of the operator board: "who's carrying what right now". Active =
// not yet dropped. Sorted newest assigned first.
public record ListManualTripsQuery() : IQuery<IReadOnlyList<ManualTripBoardDto>>;

public record ManualTripBoardDto(
    Guid TripId,
    Guid OperatorId,
    DateTime AssignedAt,
    DateTime? AcknowledgedAt,
    DateTime? PickedUpAt,
    DateTime? DroppedAt,
    DateTime? PickupDeadline,
    DateTime? DropDeadline);

internal sealed class ListManualTripsQueryHandler : IQueryHandler<ListManualTripsQuery, IReadOnlyList<ManualTripBoardDto>>
{
    private readonly IManualTripExtensionRepository _extensions;
    public ListManualTripsQueryHandler(IManualTripExtensionRepository extensions)
        => _extensions = extensions;

    public async Task<Result<IReadOnlyList<ManualTripBoardDto>>> Handle(
        ListManualTripsQuery request, CancellationToken cancellationToken)
    {
        var rows = await _extensions.ListActiveAsync(cancellationToken);
        var dtos = rows.Select(e => new ManualTripBoardDto(
            e.TripId, e.OperatorId, e.AssignedAt,
            e.AcknowledgedAt, e.PickedUpAt, e.DroppedAt,
            e.PickupDeadline, e.DropDeadline)).ToList();
        return Result<IReadOnlyList<ManualTripBoardDto>>.Success(dtos);
    }
}
