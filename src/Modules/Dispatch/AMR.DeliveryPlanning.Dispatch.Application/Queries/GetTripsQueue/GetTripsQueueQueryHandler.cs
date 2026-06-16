using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripsQueue;

public class GetTripsQueueQueryHandler : IQueryHandler<GetTripsQueueQuery, TripsQueueResult>
{
    private const int MaxPageSize = 200;

    private readonly ITripQueueReadRepository _readRepository;

    public GetTripsQueueQueryHandler(ITripQueueReadRepository readRepository)
    {
        _readRepository = readRepository;
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

        var items = page.Items
            .Select(t => new TripQueueItemDto(
                t.Id,
                t.DeliveryOrderId,
                t.OrderRef,
                t.JobId,
                t.VehicleId,
                t.VendorVehicleKey,
                t.VendorVehicleName,
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
