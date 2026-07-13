using DTMS.Dispatch.Application.Projections;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Operators;

namespace DTMS.Dispatch.Application.Queries.GetTripsQueue;

public class GetTripsQueueQueryHandler : IQueryHandler<GetTripsQueueQuery, TripsQueueResult>
{
    private const int MaxPageSize = 200;

    private readonly ITripQueueReadRepository _readRepository;
    private readonly IOperatorDirectory _operatorDirectory;
    private readonly IDeliveryOrderDirectory _deliveryOrderDirectory;

    public GetTripsQueueQueryHandler(
        ITripQueueReadRepository readRepository,
        IOperatorDirectory operatorDirectory,
        IDeliveryOrderDirectory deliveryOrderDirectory)
    {
        _readRepository = readRepository;
        _operatorDirectory = operatorDirectory;
        _deliveryOrderDirectory = deliveryOrderDirectory;
    }

    public async Task<Result<TripsQueueResult>> Handle(GetTripsQueueQuery request, CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            return Result<TripsQueueResult>.Failure("Page must be >= 1.");
        if (request.PageSize is < 1 or > MaxPageSize)
            return Result<TripsQueueResult>.Failure($"PageSize must be between 1 and {MaxPageSize}.");

        if (request.FromUtc is { } from && request.ToUtc is { } to && from > to)
            return Result<TripsQueueResult>.Failure("FromUtc must be earlier than or equal to ToUtc.");

        var filter = new TripQueueFilter(
            request.Statuses,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            string.IsNullOrWhiteSpace(request.VehicleKey) ? null : request.VehicleKey.Trim(),
            request.FromUtc,
            request.ToUtc,
            request.SortBy,
            request.SortDescending,
            request.Page,
            request.PageSize);

        var page = await _readRepository.SearchAsync(filter, cancellationToken);

        // Manual pool trips have no vendor vehicle — resolve the claiming
        // operators' names in one batched round trip so the list's
        // "Vehicle / Operator" column can show who took each job.
        var operatorIds = page.Items
            .Where(t => t.ClaimedByOperatorId is not null)
            .Select(t => t.ClaimedByOperatorId!.Value)
            .ToList();
        var operatorNames = operatorIds.Count > 0
            ? await _operatorDirectory.GetDisplayNamesAsync(operatorIds, cancellationToken)
            : (IReadOnlyDictionary<Guid, string>)new Dictionary<Guid, string>();

        // Order requester + transport mode — one batched round trip for the
        // whole page so manual / self-managed trips (no vehicle, no claiming
        // operator) can fall back to the requester, while AMR trips carry the
        // mode so the UI never shows a requester in the vehicle column.
        var deliveryOrderIds = page.Items
            .Select(t => t.DeliveryOrderId)
            .Distinct()
            .ToList();
        var orderInfo = deliveryOrderIds.Count > 0
            ? await _deliveryOrderDirectory.GetTripInfoAsync(deliveryOrderIds, cancellationToken)
            : (IReadOnlyDictionary<Guid, DeliveryOrderTripInfo>)new Dictionary<Guid, DeliveryOrderTripInfo>();

        var items = page.Items
            .Select(t => new TripQueueItemDto(
                t.Id,
                t.DeliveryOrderId,
                t.OrderRef,
                t.JobId,
                t.VehicleId,
                t.VendorVehicleKey,
                t.VendorVehicleName,
                t.ClaimedByOperatorId,
                t.ClaimedByOperatorId is { } opId && operatorNames.TryGetValue(opId, out var name)
                    ? name
                    : null,
                orderInfo.TryGetValue(t.DeliveryOrderId, out var info) ? info.RequestedBy : null,
                orderInfo.TryGetValue(t.DeliveryOrderId, out var modeInfo) ? modeInfo.TransportMode : null,
                t.Status.ToString(),
                t.AttemptNumber,
                t.PreviousAttemptId,
                t.UpperKey,
                t.VendorOrderKey,
                t.TemplateNameAtDispatch,
                t.PriorityAtDispatch,
                t.CreatedAt,
                t.StartedAt,
                t.CompletedAt,
                t.VendorExpectedCompletionAt,
                t.FailureReason,
                t.PickupStationId,
                t.DropStationId))
            .ToList();

        return Result<TripsQueueResult>.Success(
            new TripsQueueResult(items, page.TotalCount, request.Page, request.PageSize));
    }
}
