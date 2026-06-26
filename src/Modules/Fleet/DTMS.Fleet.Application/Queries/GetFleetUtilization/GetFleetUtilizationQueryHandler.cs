using AMR.DeliveryPlanning.Fleet.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Queries.GetFleetUtilization;

public class GetFleetUtilizationQueryHandler
    : IQueryHandler<GetFleetUtilizationQuery, FleetUtilizationResponse>
{
    private const int MaxWindowDays = 90;

    private readonly IFleetUtilizationReadRepository _repo;

    public GetFleetUtilizationQueryHandler(IFleetUtilizationReadRepository repo) => _repo = repo;

    public async Task<Result<FleetUtilizationResponse>> Handle(GetFleetUtilizationQuery request, CancellationToken cancellationToken)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<FleetUtilizationResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<FleetUtilizationResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var buckets = await _repo.GetRangeAsync(request.FromUtc, request.ToUtc, cancellationToken);
        var latest = await _repo.GetLatestAsync(cancellationToken);

        var dtos = buckets.Select(b => new FleetUtilizationBucketDto(
            b.BucketHour, b.Active, b.Busy, b.Idle, b.Charging,
            b.Maintenance, b.LowBattery, b.Offline, b.Total)).ToList();

        var latestDto = latest is null
            ? null
            : new FleetUtilizationBucketDto(
                latest.BucketHour, latest.Active, latest.Busy, latest.Idle, latest.Charging,
                latest.Maintenance, latest.LowBattery, latest.Offline, latest.Total);

        var lastEventAt = dtos.Count > 0 ? dtos[^1].BucketHour : latest?.BucketHour;

        return Result<FleetUtilizationResponse>.Success(
            new FleetUtilizationResponse(request.FromUtc, request.ToUtc, dtos, latestDto, lastEventAt));
    }
}
